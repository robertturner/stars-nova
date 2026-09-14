# Behavior-Specs-4 Audit — v3→v4 changes vs. current implementation

Audit date: 2026-09-11. Compares `docs/behavior-specs-4/` (18 files, several rewritten 2-6x
larger than v3, plus two brand-new files: `dynamic-string-table.md`, `ship-design-and-components.md`)
against the current C# implementation across `Common/`, `ServerState/`, `Nova.Ai/`, `Nova.Avalonia/`,
and the WinForms reference (`Nova/WinForms/`).

Legend: **CONTRADICTION** = code currently does something that conflicts with the spec (a bug).
**GAP** = spec describes something not implemented at all, or only partially. **CONFIRMED** = code
already matches. **AMBIGUOUS** = spec itself leaves a real implementation choice open.

---

## Headline results against the two stated test criteria

**"AI logic is better defined"** — Confirmed. v4 adds two entirely new AI subsystems (§9 colonizer
commitment/tech-upgrade budgeting, §10 stale-slot sweeps/pool redistribution) and retracts the
evidentiary basis for one existing claim (§8, the Fisher-Yates fleet-shuffle attribution). None of
this has been ported to `Nova.Ai/` yet — the spec is ahead of the code, which is the expected
outcome of "define the spec better first."

**"UI for starbases around planets is clear"** — Confirmed, with a caveat. The "distinguished
starbase design" second color is still explicitly unresolved in v4 (byte-for-byte unchanged spec
text) — my existing single-color-dot implementation is correct as-is, nothing to change. The
fleet-owner enclosing circle you described is still **not** documented anywhere in v4 — it needs a
further spec update before I can build it. One new, unrelated contradiction was found in this same
area: the Battle Plans limit.

---

## Fix status (updated 2026-09-11)

Of the 18 contradictions below, 15 are now fixed; 3 are deliberately left as-is because the spec
itself doesn't pin down the exact number needed to fix them correctly (guessing would just trade
one wrong constant for another). Two more bugs were found and fixed along the way while addressing
these (a `SecondPlaceScore` runtime formula error, and missing stochastic rounding in mining output).

**Fixed:** 1 (turn order), 4 (AR research penalty), 5 (victory condition defaults), 6
(`TargetsToMeet`), 7 (invented overfull decline), 8 (habitability malus cap), 9 (battle plan limit),
10/11 (defense mineral cost), 14 (bombing rounding), 15/16/17 (Race Designer tolerance band), 18
(research forecast).

**Also fixed (2026-09-11, second pass):** 2 (`LayMinesTask` — see below).

**Deliberately left as-is (spec doesn't identify which of several decompiled values applies to the
current no-trait/default case):** 3 (minefield regrowth — spec gives no formula at all, only
"regenerates some mine-unit strength"), 12 (terraform cost), 13 (scrap-fleet recovery rates). 6
(diplomacy relations decay, under GAPS below) is the same story — spec explicitly says "the exact
decay rate, threshold, and bounds... were not fully quantified."

### `LayMinesTask` plumbing (item 2), fixed

`Common/Waypoints/LayMinesTask.cs` couldn't reach `ServerData.AllMinefields` (Common has no
dependency on ServerState), so its `Perform()` was a complete no-op with the real logic commented
out. Fixed by adding a new `ServerState/LayMines.cs` (mirroring `Bombing.cs`/`CheckForMinefields.cs`,
which live in this layer for the same reason), called from `TurnGenerator.cs` right after
`LayMinesTask.Perform()` succeeds — the same place every other waypoint task's real effect is
already dispatched. It finds an existing minefield of ours nearby and adds to it, or starts a new
one with a proper per-empire key (`EmpireData.GetNextMinefieldKey()`, new — mirrors
`GetNextFleetKey`/`GetNextDesignKey` and resolves `Minefield.cs`'s own "lacks a non-static unique
id" TODO along the way).

Two bugs surfaced while wiring this up and were fixed too, since both directly undermined the new
feature's correctness:
- `ServerState/Persistence/ServerData.cs` parsed a minefield's saved key as an `int`, which throws
  `OverflowException` for any owned minefield (an owner-encoded key almost always exceeds
  `int.MaxValue`) — now reads `Minefield.Key` directly, like `Designs.Add(design.Key, design)` does.
- `ShipDesign.Update()` never reset its `Weapons`/`ConventionalBombs`/`SmartBombs`/`StandardMines`/
  `HeavyMines`/`SpeedBumbMines` fields before re-summing components each call — since at least 9
  different property getters (`MineCount`, `HasWeapons`, etc.) call `Update()` on every access, mine
  counts (and weapon lists) silently accumulated further with each read instead of being
  recomputed fresh. This would have made the new mine-laying path roughly double-count on its very
  first use (`LayMinesTask.IsValid` and `LayMines.Lay` both read `Fleet.NumberOfMines` in the same
  turn). Confirmed via two new tests in `Tests/UnitTests/TurnGeneratorTest.cs`
  (`Generate_LayMines_CreatesAMinefield`,
  `LayMines_AddsToAnExistingNearbyFieldOfOurs_InsteadOfStartingANewOne`).

Left alone (per the "Deliberately left as-is" note above): the exact mine-laying RATE table is
mostly already correct as-is — `components.xml`'s real `LayerRate` values already vary per
component (20-200), consistent with the spec's description of a per-component rate rather than a
flat design constant — but the spec's "ship-count-weighted average with a diminishing-returns curve
toward ~98% for large fleets" is explicitly unresolved ("not conclusively resolved"), so
`Fleet.NumberOfMines` keeps its existing flat per-ship sum rather than guessing at that curve's
shape.

---

## CONTRADICTIONS (real bugs — ranked roughly by gameplay impact)

1. **Turn phase ordering: combat resolves after fleet movement, not before.**
   `ServerState/TurnGenerator.cs:95-159` runs fleet movement/waypoint-tasks (`ProcessFleet`,
   122-125) *then* `BattleEngine.Run()` (134). `turn-generation-engine.md` §1 places combat
   detection/resolution at phase 9, well before the economic/waypoint-task pass (11) and fleet
   movement execution (13). Real consequence: a fleet that should be destroyed in combat can still
   execute its orders that same turn.

2. **`LayMinesTask.Perform()` is a complete no-op.** `Common/Waypoints/LayMinesTask.cs:83-112` —
   the code that would actually create/update a minefield is commented out, even though `IsValid`
   (66-81) validates the order as if it should work. Mine-laying orders silently do nothing.

3. **Minefields never regrow, only decay.** `ServerState/CheckForMinefields.cs:63-71` only shrinks
   minefields each turn; `turn-generation-engine.md` §3 documents a separate regrowth step. No
   regrowth term exists at all.

4. **Alternate Reality research-cost penalty is inverted.**
   `Common/RaceDefinition/RaceAdvantagePointCalculator.cs:386` applies a −100 point penalty when
   Energy research is set to **Cheap** (`== 50`); `race-traits.md` §1a confirms the penalty should
   apply when Energy is **Expensive**. Every other item in this calculator matches the spec almost
   line-for-line, which makes this single inversion stand out as a genuine bug rather than a
   deliberate simplification.

5. **Victory condition defaults are wrong, in some cases by 10x.**
   `Common/Files/GameSettings.cs:58-67` vs. `victory-conditions.md` §1's newly-confirmed real
   defaults (confirmed 2026-09-10 by direct observation of the running client):
   - TechLevels/NumberOfFields should default **enabled** at 22/4 — code has them disabled.
   - TotalScore should default to **11000** — code has **1000** (10x off).
   - SecondPlaceScore should default **enabled** at 100% — code has disabled/0.
   - ProductionCapacity's valid range is ~10-100 per the spec's own scaling table
     (`(raw+1)×10`) — code's default of **1000** is outside that range entirely, suggesting a
     units mixup.

6. **`TargetsToMeet` isn't derived from how many conditions are actually enabled.**
   Spec (`victory-conditions.md`): `min(raw, count of enabled conditions 1-7 excluding 3)` — the
   game prevents requiring more simultaneous conditions than are turned on.
   `Nova.Avalonia/ViewModels/NewGameViewModel.cs:305-308` just clamps to a fixed 1-8 range
   regardless of how many conditions are enabled, so a player can set 8 while only 1 condition is
   on, making victory unreachable.

7. **Invented "overfull population decline" mechanic.** `Common/GameObjects/Star.cs:369-378`
   declines population once capacity exceeds 100%. `population-growth.md` Example 3 states plainly
   the exe shows **no** such mechanic exists from exceeding capacity alone on an otherwise-positive
   -habitability world. This looks like a Nova-only invention with no source, old or new.

8. **Habitability malus cap is doubled by a trait, but spec says it's a hard, unconditional
   constant.** `population-growth.md` §2 confirms the single-axis penalty is capped at exactly
   **15**, full stop. `Common/RaceDefinition/Race.cs:240-248` (`GetMaxMalus()`) returns **30** for
   races with the "TT" trait.

9. **Battle Plan limit: code allows 10, spec confirms exactly 15.**
   `Common/GlobalDefinitions.cs:140` (`MaxBattlePlans = 10`), consumed by
   `Nova.Avalonia/ViewModels/Panels/BattlePlansViewModel.cs:78,113` and mirrored in the WinForms
   original (`Nova/WinForms/Gui/Dialogs/BattlePlans.cs:137,199`). `client-interface.md:183` is a
   brand-new v4 finding giving the exact number 15. Single-constant fix.

10. **Defense unit cost is wrong, and defenses shouldn't cost minerals at all.**
    `Common/GlobalDefinitions.cs:119-122` charges 5kT each of 3 minerals + 15 Energy = 15 total
    resources per defense. `production-queue.md` §5 treats defenses like mines/terraforming as
    needing **only resources** (no minerals), and its own "discrepancy found by inspection" note
    says the exe shows three race-derived branches of **25/44/48** total resources — neither the
    15-resource figure nor the mineral charge matches. `Common/Production/DefenseProductionUnit.cs:81`
    is the mineral-charging code to remove.

11. **Terraform cost doesn't match any decompiled branch.**
    `Common/Production/TerraformProductionUnit.cs:63` uses 100 resources (no trait) / 70 (Total
    Terraforming). Spec's decompiled branches are 70/110/120 — the 70 that *is* used only
    coincidentally matches one branch via unrelated arithmetic (100 × 0.7 discount), not by using
    the actual branch value for the no-trait case.

12. **Scrap-fleet recovery rates likely don't match.** (Moderate confidence — two possibly
    different sources.) `Common/Waypoints/ScrapTask.cs:109-131` recovers 80%/90% (with
    starbase/Ultimate Recycling). `fleet-movement-scanning-cargo.md`'s newly-decompiled
    "Segment 23" note gives recovery divisors of 3/4/5/10/20 (≈33%/25%/20%/10%/5%) — none of the
    five decompiled values is 80% or 90%.

13. **Bombing damage rounds deterministically (down) instead of the spec's random proportional
    rounding.** `ServerState/Bombing.cs:87,108,113,118` truncates with `(int)`; `combat-resolution.md`
    §9 (a brand-new subsystem in v4) documents a bounded-random-roll-against-the-remainder
    algorithm instead.

14. **Race Designer: tolerance band can go to 0 width; spec requires a minimum of 20.**
    `Nova.Avalonia/ViewModels/EnvironmentToleranceViewModel.cs:30-58` only clamps
    `MinValue ≤ MaxValue`; `race-designer-ui-and-availability.md:49` requires re-centering to force
    a width of at least 20 rather than allowing anything narrower.

15. **Race Designer: default reset band is 15-85, spec confirms 20-80.**
    `Common/DataStructures/EnvironmentTolerance.cs:41-42` — pre-existing, not new this session, but
    a direct numeric contradiction of a newly-confirmed default.

16. **Race Designer: toggling Immune off doesn't restore any interval.**
    `EnvironmentToleranceViewModel.cs:60-71`'s `Immune` setter has no side effect on `MinValue`/
    `MaxValue` at all; spec says turning immunity off should restore the ordinary editable interval.

17. **Research "years to complete" forecast has a dimensional bug.**
    `Nova.Avalonia/ViewModels/Panels/ResearchViewModel.cs:216-218` computes
    `resourcesRequired = targetCost - currentLevel[targetField] - bankedResources` — subtracting a
    raw tech-level integer (e.g. `5`) from a resource-cost figure (e.g. `1330`) in addition to
    `bankedResources`, an erroneous extra term with no basis in the spec's formula
    (`research-tech-tree.md:217-224`: outstanding cost ÷ per-turn contribution). Straightforward fix
    — drop the `- currentLevel[targetField]` term.

---

## GAPS (documented, not implemented — grouped, highest-value first)

- **AI: §9 colonizer commitment/tech-upgrade budgeting — partially fixed (2026-09-11, fifth pass).
  §10 stale-slot sweeps remain undone, and rightly so.**

  §10 is left alone entirely: the spec itself says "the concrete real-world meaning of the
  redistributed pool and the race stat being monitored could not be determined from structure
  alone" — there's no reasonable implementation to write here, only invention.

  §9 turned out to bundle two very different things: two concrete, numeric, unambiguous gates
  (a 5,000 cargo-capacity threshold, and a ~100-unit/squared-distance-10,000 proximity check to
  "whatever is funding the decision"), and a much softer, unwired "design upgrade ambition" ladder
  with no given modulation formula and no existing mechanism to attach its result to. Implemented
  the two concrete gates as new `Nova.Ai/ColonizerCommitmentAdvisor.cs`, wired into
  `DefaultAi.HandleColonizing()` right after `ColonizationTargetSelector` (§2) picks a target: a
  fleet whose cargo *capacity* (not however much is currently loaded — checked before `Colonise()`
  has loaded anything) is under 5,000, or whose target is too far from the fleet's own position
  (the most direct available stand-in for "the funding source," which the spec doesn't pin down
  further), now leaves that target unclaimed for a fitter fleet instead of committing to it
  regardless. This fixes a real, pre-existing gap: `DefaultFleetAI.Colonise()` previously sent
  *any* colonize-capable fleet at *any* distance, including ones too small to ever found a viable
  colony. Verified with 5 new tests in `Tests/UnitTests/ColonizerCommitmentAdvisorTest.cs`.

  Left undone, and disclosed in `ColonizerCommitmentAdvisor`'s own comment: the tech-field-sum
  vs. 59/71/84/95/108 "upgrade ambition" ladder and its research-cost budget (capped at 5,000, or
  3,500 for an unnamed "specific design category" the spec itself couldn't identify) — no formula
  is given for how cargo surplus modulates the ladder, and there's no existing "request a research
  retarget for this design" action anywhere in the codebase to hand the result to, so building it
  now would mean inventing both the formula and its game effect from nothing. Also left undone:
  the persistent per-decision "audit trail" bitmask ("used to avoid re-deciding the same case
  identically every turn") — Nova's AI runs as a fresh, stateless process each turn with no
  persisted scratch state, and recomputing the decision fresh every turn is functionally
  equivalent (same inputs, same outputs), just without the original's log/audit bookkeeping.
- **Stargates and Wormholes — both fixed (2026-09-11, fourth and sixth passes).** Both were
  confirmed entirely absent by three separate audits (fleet movement, turn generation,
  client-interface's map overlays).

  Stargates now work: `Star.GetStargate()` reads a star's Starbase design for an installed Gate
  component (mirroring the existing "Gate" property check already used for the map's Stargate
  dot); `TurnGenerator.TryStargateJump` (called at the top of the same per-waypoint loop that
  handles ordinary warp movement) lets an eligible fleet skip straight to its destination the same
  turn, using the spec's documented formulas: distance checked only against the *sending* gate's
  rated range, mass checked against *both* gates, "overgating" allowed up to 5x over either rating
  (beyond that the gate simply refuses the jump, falling through to ordinary warp movement), always
  damaging the ship per the given `RangeDamagePercent`/`MassDamagePercent`/`CombinedDamagePercent`
  formulas (applied to the same persistent per-token Armor field combat damage already uses), and
  destroying it outright past 100% cumulative damage. Cargo restriction (fuel-only unless
  Interstellar Traveler) and per-ship destruction rolls (matching the existing per-ship, not
  per-fleet, Warp 10 destruction pattern) are both implemented.

  **Worth flagging explicitly**: the spec's own "vanish chance" formula for mass-overgating is
  labeled a lower-confidence "community-fitted approximation," and as transcribed it has a
  genuinely counter-intuitive shape — vanish chance is *highest* (~100%) the instant a ship starts
  overgating on mass at all, and *decreases* toward the fitted floor (~68%) as it's pushed further
  toward the 5x cap, rather than increasing with severity as intuition would suggest. I implemented
  the formula exactly as given rather than "fixing" what might be a real, if odd, game behavior —
  but flagging it since it means any ship that mass-overgates even slightly faces near-certain
  destruction per this formula, which is worth double-checking against further primary-source
  evidence before trusting in a live game. Interstellar Traveler's "reduced but not quantified"
  vanish-chance discount uses an explicit, clearly-commented placeholder (50%) pending a real
  number. Verified with 4 new tests in `Tests/UnitTests/StargateJumpTest.cs`, deliberately scoped
  to only the deterministic parts of the mechanic (safe jump, the 5x refusal boundary, and cargo
  rules) rather than asserting anything about the vanish-chance rolls themselves.

  Wormholes now exist too, as a deliberately-scoped-down core slice: new `Common/GameObjects/
  Wormhole.cs` (paired via `PairedKey`, a 0-6 `StabilityTier`), generated in linked pairs during
  galaxy creation (`StarMapinitializer.GenerateWormholes`, called right after star placement) kept
  a minimum distance from every star and every other wormhole, and a new per-turn
  `WormholeDriftStep` that nudges each end's position a little every year (more for less-stable
  ends), clamped to the map bounds. A fleet arriving at (or near) either opening transits to the
  paired end that same turn - matched purely by arrival *position* since there's no player-facing
  "target this wormhole" order or UI in this port, unlike Stargates' explicit gate-to-gate order;
  a plain waypoint set to an empty-space point already works this way everywhere else in this
  codebase, so no new order type was needed.

  Left out entirely, and disclosed in `Wormhole.cs`'s own comment: the spec's own placement uses
  an unquantified "four squared-distance tiers" scoring system (replaced here with a plain
  minimum-distance check) and per-turn drift uses an unconfirmed exact chance/magnitude (replaced
  with a simple, disclosed linear tier-scaled chance) rather than the spec's "0-99 roll vs.
  stability tier" gate whose precise thresholds don't survive. Cloak-based per-empire discovery
  (wormholes are simply visible once generated, with no parallel Intel/report pipeline built for
  them - a disproportionate side-investment for one object type) and the entire heavy-mineral-
  cargo transit side effect (probabilistic relocation for gas-tolerant races; habitat-tolerance
  drift for ordinary ones) are not implemented at all - several of the traits/thresholds that
  mechanic depends on are explicitly unidentified even in the source material, same as AI §10.
  Verified with 3 new tests in `Tests/UnitTests/WormholeTest.cs`: paired placement respecting the
  minimum star distance, drift never leaving the map bounds across 500 simulated turns, and a
  fleet correctly teleporting through a manually-placed pair.
- **Remote-mining fleets** — **fixed (2026-09-11, third pass).** No code path previously existed
  for an orbiting fleet to mine a planet at all (`Star.Mine()` only ever applied the planet's own
  mines, and was itself gated to owned/colonized stars only). Added:
  - `ShipDesign.MineEquivalents` (a "Mining Robot" component's value was parsed from XML but never
    actually summed anywhere — a real, separate gap, now fixed by adding it to the existing
    scalar-sum property list) and `Fleet.MineEquivalents` (sums across composition, capped at the
    spec-confirmed 4,000/fleet via new `Global.MaxRemoteMiningEquivalents`).
  - `Star.MineForFleet` — extracted the depletion-application logic out of the existing `Mine()`
    (used by a planet's own mines) into a shared `ApplyMining` helper, so a remote-mining fleet's
    contribution depletes concentration through the exact same mechanism, confirming the spec's
    "planet's own mines and each orbiting fleet, applied in sequence" behavior at the code level.
  - New `ServerState/TurnSteps/RemoteMiningStep.cs`, registered as its own turn step (11, just
    before the planet's-own-mines step at 12) since it must run for **every** star regardless of
    ownership — the whole point of a remote-mining fleet is mining planets nobody has colonized,
    unlike `StarUpdateStep` which explicitly skips unowned/uninhabited stars. Mined minerals go
    into the fleet's own cargo (capped at free capacity; a full fleet doesn't bother mining, and a
    partially-full one loads Ironium/Boranium/Germanium in that order until full), not a planet
    stockpile, since an unowned planet has none.

  Verified with 3 new tests in `Tests/UnitTests/RemoteMiningStepTest.cs`: mining at an unowned star,
  cargo-capacity capping while concentration still depletes regardless, and a non-mining fleet
  being correctly ignored.

  Left alone, per the same "spec doesn't resolve this" reasoning as elsewhere in this document: the
  fleet-wide diminishing-returns/98%-cap curve for *laying* mines doesn't apply here (that's a
  different mechanic - `Fleet.NumberOfMines`), but the mining depletion-curve breakpoints
  (`KtToDropOnePoint`, 27/462/1000/2000 community-sourced vs. the spec's newly-found 25/10 with a
  fixed-point byte carry) are unchanged, so remote mining shares whatever imprecision that
  already-deferred item has - fixing the curve itself remains a separate, deferred item.
- **Random turn events (comet strikes, mineral deposits, Mystery Trader, attrition) are entirely
  absent** — `TurnGenerator`'s registered turn-steps are only Scan/Bombing/StarUpdate.
  Same for **mass packets** (no decay-in-flight or blast-radius delivery).
- **Tutorial/coach system is entirely absent** (v4 adds a whole new §3a "Stars! Tutor" dialog with
  Hide/Hint/Panic buttons). This session's Help panel is a static reference browser, not the
  turn-gated step-advancing coach the spec describes — a different, non-overlapping feature.
  **Note:** this appears unrelated to your test criteria and I'd deprioritize it unless you say
  otherwise.
- **8 preset race archetypes / 32 portrait variants / in-wizard Random-race generator** — brand new
  in v4, nothing like it exists in either WinForms or Avalonia Race Designer. The project also only
  ships 6 `DefaultRaces/*.race` files, not 8.
- **Galaxy generation uses a structurally different algorithm than the spec documents** —
  continuous MapWidth/Height/StarDensity/StarUniformity sliders feeding a density-function
  generator, vs. the spec's discrete Tiny/Small/Medium/Large/Huge × Sparse/Normal/Dense/Packed
  enum-driven formulas. This pre-dates this session (already in the WinForms wizard) and is a
  scope-level architectural divergence, not a quick fix — flagging for a decision on whether to
  ever converge these, rather than as an actionable item.
- **6 of 7 confirmed boolean game-option flags are missing** (only "Accelerated Start" exists;
  spec names Beginner: Max Minerals, Slower Tech Advances, Accelerated BBS Play, No Random Events,
  Computer Alliances, Public Scores, Galaxy Clumping).
- **Slow Tech Advance doubling, Miniaturization, and Bleeding Edge Technology research discounts**
  — all unimplemented (BET is a known, pre-existing documented gap; Slow Tech is new to flag).
  **Starbase-upgrade half-cost/~20% discount** likewise not found.
- **No "lowest field" auto-research-target option**, and **no PRT-conditional field exclusion**
  (Alternate Reality excluding Energy/Weapons/Propulsion) in the Research panel's field picker.
- **Combat**: no target-type classifier is actually wired up (`BattlePlan.PrimaryTarget`/
  `SecondaryTarget` are set but never read by `BattleEngine.SelectTargets`); initiative formula is
  a large simplification (no PRT bonus, no clamp to 0-8, no per-token randomization for ties); no
  diminishing-returns cap on cloak/jam stacking; no tech-gain-from-battle/bombing.
- **Ship/Starbase design**: no per-race design-slot cap (16 hull + 10 starbase, confirmed exact in
  v4), no 9-component-slot cap, no confirmation prompt when editing a design already in use by
  fleets, no graded (vs. binary) tech-availability feedback.
- **Production queue**: no 200-entry queue cap / 1,023-per-line cap, no completion-time simulator
  with a "practically never" warning, no 4-slot template manager, no Alternate Reality automatic
  Mineral Alchemy conversion of leftover resources.
- **Population/mining**: negative-habitability decline formula and its persisted fractional-remainder
  carry not implemented (current code uses a different, simpler formula); mining depletion-curve
  breakpoints (25/10 in spec vs. code's older 27/462/1000/2000 community-sourced numbers) likely
  wrong. **Fixed:** mining output now uses the same unbiased stochastic rounding the spec confirms
  (`Common/GameObjects/Star.cs`'s `GetMiningRate`, via the new shared `Global.StochasticRound` —
  also used by bombing's population-kill/defense/factory/mine destruction, which had the identical
  always-round-down bug).
- **CargoTask** only supports flat Load-All/Unload-All — no percentage-fill, no "Load Optimal," no
  conditional waypoint-amount targeting.
- **SplitMergeTask.MergeFleets** does an unconditional full merge with none of the spec's
  probabilistic fuel-stranding/incompatible-design-abort/scanner-cap logic.
- **Diplomacy**: no automatic relationship decay toward war over time (relationships are purely
  player-set-and-forget today).
- **Save/turn format**: no 31-character name cap enforced anywhere; no persisted score history
  (spec confirms 100-turn retention, code only ever computes the current turn).
- **`dynamic-string-table.md`**: this is reverse-engineering of the original binary's proprietary
  string-compression format — not applicable to Nova's clean-room .NET string handling. No action
  needed here; flagged only for completeness.

## CONFIRMED highlights (representative, not exhaustive — a lot already matches)

Race-wizard advantage-point formula (`RaceAdvantagePointCalculator.cs`) matches the v4
reconstruction almost line-for-line aside from item 4 above. Battle-plan first-plan-protection and
name-suffix-wraparound logic matches exactly. World-availability-estimator formula matches exactly.
Research cost curve, Generalized Research's 50/15% split, and ExtraTech floor logic all match
exactly. Combat's 256-token cap, 1/3 salvage divisor, ramscoop fuel formula, cloak-vs-scan formula,
NAS scanner doubling, warp-10 destruction, and 16-round battle cap all match exactly. Player
Relations' 3-state model and Battle Plan's 5-category enemy resolution both already cite the prior
spec revision in their own code comments as intentional fixes. Fleet-merge, terraform axis-stepper,
and partial-build carry-forward across all four production unit types all match.

## AMBIGUOUS (spec leaves a real choice open, or two spec sources conflict)

- Population growth: spec's own "verification against the exported client" note flags a live,
  unresolved discrepancy between the community-sourced crowding-factor formula (currently
  implemented) and a different ceiling-clamp formula the exe inspection suggests — the spec itself
  says this needs resolving before either can be called correct.
  Same goes for the exact no-trait Defense/Terraform cost branch, whether the 63%-cap stacking
  formula applies to cloak or jamming, and fuel-per-LY (two candidate formulas in the spec itself).
  Also flagged: AI's decision-making architecture (stateless per-turn CLI) would need real
  structural work before §9/§10's cross-turn memory requirements could be met at all.

---

## Recommended next step

This is far more than one pass can safely fix blind — 17 contradictions plus dozens of gaps span
turn order, combat, population, victory conditions, race design, and research simultaneously, and
several interact (e.g. the turn-order fix changes when combat casualties are visible to the
economic phase). I'd suggest tackling this in batches rather than all at once. See chat for a
proposed batch breakdown.
