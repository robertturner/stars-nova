# Headless whole-game simulation (Nova.Sim)

`Nova.Sim` plays complete games with no UI and no LLM: every player is the game's own
`Nova.Ai.DefaultAi`, driven in-process through the real file-based turn cycle

    DefaultAi.Initialize (reads <race>.intel) -> DoMove -> OrderWriter (<race>.orders)
      -> OrderReader -> TurnGenerator.Generate (writes the new .intel files) -> ServerData.Save
      -> every K turns: ServerData.Restore from disk and play on from the reloaded state

and checks a set of named invariants every turn, collects per-empire metrics, and hashes the
saved state. It is the same sequence `Nova.Avalonia/TurnHost.cs` and the WinForms console run, so
whatever the simulation finds is a real game bug.

## Layout

| Where | What |
|---|---|
| `Nova.Sim/` (console project, net9.0, in `Nova.sln`) | the library and the CLI |
| `SimulationConfig.cs` | seed, galaxy size/density/star count, starting distance, players (`PlayerSpec`: AI template archetype x tier, optional category override), turns, accelerated start and the other game options, reload interval, limits |
| `SimulationRunner.cs` | creates the game (`Gameinitializer.Initialize`, AI races from `AiRaceTemplates`, category = archetype, the New Game screen's rule) in a work folder and runs the loop; `Resume(folder)` continues a saved game; `BeforeTurn` / `AfterTurn` hooks |
| `AutoTurnGenerator.cs` | the host's batch generation loop (10 / 100 / 1000 turns, abort check between turns; turn-generation-engine.md, coverage row 45). The runner's loop is built on it |
| `SimTurnGenerator.cs` | the real `TurnGenerator` with its protected seams overridden: counts commands rejected by `ICommand.IsValid`, captures the turn's messages, skips the per-year backup copy; `SeededDefaultAi` seeds the AI's `Random` from (seed, player, year) |
| `SimulationEnvironment.cs` | isolates a run from process-wide state: a private `nova.conf` (via `PlatformHooks.NovaRootOverride`), `Report.Error` capture, `Report.FatalError` turned into an exception, a fresh `GameSettings` |
| `InvariantChecker.cs` | the invariant rules |
| `SimulationMetrics.cs`, `SimulationResult.cs` | per-turn per-empire metrics, CSV/JSON export, CSV compare, one-screen summary |
| `AiBehaviourAssertions.cs` | sanity expectations for how the AI plays a normal game |
| `StateHasher.cs` | SHA-256 of the saved state with the machine-specific paths blanked |
| `Tests/Simulation/` (in `Tests.csproj`) | NUnit suites (below) and `Goldens/seed-*.json` |

Simulations share the game's static state (`GameSettings.Data`, `PlatformHooks`, `AllComponents`), so
one process runs one simulation at a time; `batch --jobs N` runs seeds in parallel child processes.

## Running

Build (`dotnet build Nova.Sim/Nova.Sim.csproj`) and run `Build/Debug/Nova.Sim.exe` (or
`dotnet Build/Debug/Nova.Sim.dll`). `components.xml` is found next to or above the executable or the
current directory; pass `--components <path>` (or set `NOVA_COMPONENTS`) otherwise. Running the exe
straight from `Build/Debug` locks the assemblies there: copy the `Nova.*` files elsewhere for long
runs if other builds are happening.

    Nova.Sim run --seed 1 --players 4 --turns 200 --out sim-out/seed-1
    Nova.Sim run --seed 3 --players "Robotoids/Expert,Macinti/Easy,2/1@7" --size Small --accelerated --turns 100 --out x
    Nova.Sim batch --seeds 1-20 --jobs 4 --players 4 --turns 150 --out sim-batch
    Nova.Sim compare sim-out/a/metrics.csv sim-out/b/metrics.csv
    Nova.Sim replay --game sim-out/seed-1/game --turns 50 --out sim-out/seed-1-more
    Nova.Sim generate --game <folder> --turns 10          (host batch loop; any key aborts)

Players: `--players N` gives archetypes 0..N-1 (Robotoids, Turindrones, Automitrons, Rototills,
Cybertrons, Macinti, wrapping) at `--tier` (default Standard); or a list of
`archetype[/tier][@category]`, names or numbers, `Random` allowed (resolved from the seed as the New
Game screen does). `@7` plays the economy-only driver, `@6` no driver.

Other options: `--size Tiny|Small|Medium|Large|Huge`, `--density Sparse|Normal|Dense|Packed`,
`--stars N`, `--distance`, `--accelerated`, `--no-random-events`, `--slow-tech`, `--max-minerals`,
`--clumping`, `--reload-every K` (default 5; 0 = never), `--no-roundtrip`, `--stop-on-violation`,
`--disable Rule1,Rule2`, `--max-turn-seconds S`, `--max-messages N`, `--keep-backups`,
`--behaviour` (evaluate the AI expectations; runs of 50+ turns always print them).

`run`/`replay` write `metrics.csv` (one row per empire per turn), `result.json` (config, every
turn's record and hash, every violation), `summary.txt` and keep the game folder (`<out>/game`),
which `replay` or the normal client can open. `batch` writes `<out>/seed-N/...` and `batch.csv`.

Exit codes: 0 ok, 1 an invariant failed or the run crashed, 2 a behaviour expectation failed
(`--behaviour`), 3 `compare` found differences, 64 usage error.

`profile-save --game <folder>` (diagnostics) times restoring and saving a state and its parts.

## Invariants

Each rule has a name; a violation reports `[Rule] turn T (year Y) empire E <object>: message`.
Disable rules by name with `--disable` / `SimulationConfig.DisabledInvariants`; add your own
`InvariantRule` to `SimulationRunner.Checker.Rules`.

| Rule | Checks |
|---|---|
| NoException | (reported by the runner) an AI move or the turn generation threw |
| NoReportedErrors | no `Report.Error` during the turn (except `Limits.AllowedErrorSubstrings`) |
| AiOrdersAccepted | every AI's orders file was accepted by OrderReader, no order failed to parse, no command failed `IsValid` (count must be 0) |
| StockpilesNonNegative | planet minerals, colonists, factories, mines, defences; fleet cargo and fuel; research resources |
| NumbersFinite | fleet fuel, bearing, cloak, target distance are not NaN/infinite |
| TechLevelsInRange | 0..26 |
| FleetCap / DesignCap | at most 512 fleets, 16 ship designs, 10 starbase designs per empire |
| FleetsInsideMap | fleets, minefields, packets inside 0..MapWidth/Height (+ `MapMargin`) |
| WaypointTargetsResolvable | waypoint positions on the map, planet targets name a star, fleet targets name a live fleet |
| NoEmptyFleets / ShipDesignsExist | no fleet without ships or with a 0-ship token; every token's design is its owner's |
| KeysUnique | fleet / design / minefield keys match their dictionary keys and owners, no key or design name reused |
| PlanetOwnersExist | owners exist and list their planets, and vice versa |
| PlanetRaceMatchesOwner | an owned planet's `ThisRace` is its owner's race |
| StarbaseConsistent | a starbase belongs to its planet's owner and is one of its fleets; unowned planets have none |
| OrbitConsistent | a fleet in orbit sits on its star |
| ProductionQueuesValid | no negative quantities, no ship orders for unknown designs, at most `MaxQueueLength` entries, nothing queued on unowned planets |
| PopulationWithinCapacity | population below `PopulationCapacityFactor` x max(capacity, `PopulationFloor`) |
| ScoreEliminationConsistent | scores >= 0, ranks 1..N, elimination flag matches the elimination test, score history recorded |
| SaveRoundTrip | on reload turns, reload + re-save gives identical text |
| MessageCountBounded | at most `MaxMessagesPerEmpirePerTurn` messages to one empire in a turn |
| TurnTimeBounded | a whole turn within `MaxTurnSeconds` |

## AI behaviour expectations

`AiBehaviourAssertions.ForNormalGame(thresholds)` (defaults in `BehaviourThresholds`) on a 4-AI
100-turn game: every driven AI owns >= 2 planets by turn 50; builds ships (or colonises) by turn
30; >= 50% of driven AIs build a warship beyond their starting ships by turn 80; every AI's
research total rises by >= 3 over the first 50 turns; no AI goes more than 10 turns with an empty
production queue everywhere, or more than 3 turns producing no resources; no victory and no
elimination before turn 30; every AI ends with at least its starting population. The freighter
expectation's share defaults to 0 because today's AI never builds a freighter (see the observed
numbers in the T2 report). `ObservedBehaviour(result)` prints the headline numbers per empire.

## Tests

    dotnet test Tests/Tests.csproj                                  # normal suite: includes Category=Simulation (~1 min)
    dotnet test Tests/Tests.csproj --filter Category=Simulation     # only the simulation tests
    dotnet test Tests/Tests.csproj --filter Category=Soak           # the long runs (explicit; tens of minutes)
    UPDATE_GOLDENS=1 dotnet test Tests/Tests.csproj --filter Category=Golden   # regenerate the goldens

* `SimulationSmokeTest` - Tiny galaxy, 3 AIs, 30 turns, reload every 5: every invariant, the
  short-game behaviour expectations, no rejected orders, CSV/JSON export and compare.
* `InvariantCheckerTest` - plays one turn, then for each rule corrupts a reloaded copy of the state
  and checks that exactly the named rule catches it (and that a clean state breaks none).
* `DeterminismTest` - two runs of the same config give the same hash every turn.
* `GoldenSnapshotTest` (`Category=Golden`) - three fixed seeds, the state hash every 4 turns of 12,
  against `Tests/Simulation/Goldens/seed-*.json`. Each first checks two identical runs agree and
  reports Inconclusive ("DETERMINISM INCOMPLETE") if not. Any intended change to rules, AI or save
  format changes the hashes: regenerate with `UPDATE_GOLDENS=1` and commit the files.
* `AutoTurnGeneratorTest` - the 10/100/1000 batch loop, abort, a non-submitting player, resume.
* `SimulationFoundBugsTest` - regression tests for the bugs fixed in place.
* `SimulationSoakTest` (`[Explicit]`, `Category=Soak`) - seeds 1-3 x 2/4/8 players x 300/200/150
  turns (accelerated start alternating), the 24 AI templates in six 4-player 150-turn games, the
  normal 4-AI 100-turn game's behaviour expectations for three seeds, and "reloading every turn
  plays the same game as never reloading".

`Tests/Simulation/SimulationTestSupport.KnownIssues` lists violations that are reported but not yet
fixed (each a BUG FOUND entry); matching violations are printed instead of failing. Remove an entry
when its fix lands.
