# Stars! Clean-Room Reimplementation — Status

## What this is
A clean-room reimplementation of the 1995-2000 4X space strategy game "Stars!" in C#/.NET.
The original `stars.exe` is closed, commercial, abandoned software (not open source, not public domain —
the maker stopped selling it and released activation serials for free, but that does not waive copyright).

**2026-09-04 pivot:** rather than continuing the from-scratch `Stars.Core` effort, this project is now
a fork of **Stars! Nova** (github.com/ekolis/stars-nova, GPL v2), an existing open-source Stars! clone,
adopted as the base and evolved from here — the intent is to fix/complete it against this project's
clean-room behavior specs and contribute improvements back to that community. The original from-scratch
work is preserved under `archive/clean-room-stars-core/` rather than deleted. See "Layout" below for
where Nova's own source lives (it occupies most of the repo root: `Common/`, `Nova/`, `ServerState/`,
etc.) and "Working on Nova" for how this session builds/tests it and the punch list of fixed vs.
still-open items.

## Ground rule (read before touching anything)
This implementation is written **only** from documented external behavior — the manual, community
FAQs/wikis, and our own empirical play-testing of the running original game. It must **never** be
written by reading the original code, disassembly, or decompilation. A Ghidra project analyzing
`stars.exe` exists elsewhere on the original machine for unrelated exploration — it must not be
consulted for anything that feeds into this implementation. This separation is what makes the
reimplementation defensible; collapsing it turns the code into a derivative work.

## Layout
- `docs/behavior-specs/*.md` — six behavior specs, each documenting one subsystem in original wording,
  cited to public sources, with worked numeric examples. **All six are complete**, and several open
  questions have since been resolved by direct empirical testing (see below) or by reading Stars!
  Nova's own source as a secondary cross-check.
  - `population-growth.md` — habitability, population growth, mineral mining
  - `production-queue.md` — production queues, resource allocation
  - `research-tech-tree.md` — tech fields, research point allocation, cost curve
  - `combat-resolution.md` — battle mechanics, targeting, damage
  - `fleet-movement-scanning-cargo.md` — warp/fuel, scanning, cargo
  - `race-traits.md` — PRTs, LRTs, race customization sliders
- `docs/ui-reference/*.png` — reference screenshots of the real game's UI, gathered via the
  automation harness (see "Testing setup" below), for later front-end design work.
- `tools/game-automation/` — the PowerShell + Win32 API harness used to drive the real game.
- `archive/clean-room-stars-core/` — the original from-scratch `Stars.Core` (.NET 9 class library) +
  xUnit tests, parked as of the 2026-09-04 pivot to Stars! Nova. Still builds (`dotnet test`); kept
  for reference and in case any of its clean-room-derived formulas are useful again later.
- **Everything else at the repo root** (`Common/`, `Nova/`, `ServerState/`, `ControlLibrary/`,
  `GameFiles/`, `Documentation/`, `Tests/`, etc.) is Stars! Nova's own source tree, forked in whole
  with its git history intact (`git log` shows commits from the original project before this fork's
  first commit). `Common/` is the shared domain model (game objects, production, research, race
  definitions — the layer most of this session's fixes touched); `Nova/` is the WinForms
  client+AI+server-hosting exe; `ServerState/` is turn-processing/persistence logic.

## Working on Nova
**Build**: old-style (non-SDK) `.csproj` files targeting .NET Framework 4.8. On this session's
machine (no Visual Studio, no NuGet cache), the working build command was the legacy Framework
MSBuild directly on `Nova\Nova.csproj` (which pulls in `Common`, `ControlLibrary`, and `ServerState`
via project references):
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" Nova\Nova.csproj /p:Configuration=Debug
```
Building from the `Z:\StarsClone` network-share path itself hit a spurious `MSB3821` ("mark of the
web"/zone) error on `.resx` files that `Unblock-File` didn't fix (no actual Zone.Identifier stream
was present — looked like an MSBuild/UNC-path quirk, not a real per-file block). Worked around by
building from a local copy (`robocopy` mirror) instead of changing any system zone-security settings.
`Tests\Tests.csproj` additionally needs `NuGet Package Restore` for `NUnit3TestAdapter` (not attempted
this session — no NuGet access).

**Fixed this session (2026-09-04), each as its own commit** — see `git log` for full detail per item:
mineral concentration depletion (was a stub using `12500/concentration` uniformly instead of the real
tiered curve), two dead-code trait-string bugs (Hyper Expansion growth bonus, No Ram Scoop Engines
component gating), the research tech-cost table (was a wrong Fibonacci formula, up to ~16x too
expensive at high levels), Generalized Research (was comment-only, never applied), War
Monger/Claim Adjuster missing starting-tech bonuses (both empirically confirmed against the real
game), a bug where completed research never actually deducted its cost, the production-queue
population-cap incorrectly blocking construction instead of just idling it, a cost-undercharging bug
on every production unit after the first in a batch, Mineral Alchemy and Terraforming (both were
`NotImplementedException` stubs), the ExtraTech LRT applying to every field instead of just
"Expensive" ones, the growth-rate slider's minimum, Alternate Reality's entire distinct
resource/scan-range formula (was silently using the standard formulas), a planetary-scanner-upgrade
bug (type upgraded but not range), cloak reduction of fleet detection range (was set but never read),
warp-10 destruction risk and ramscoop fuel generation (both entirely absent), War
Monger/Inner-Strength weapon cost modifiers and Inner Strength's defense discount, Improved
Starbases' cost discount (was dead commented-out code), No Advanced Scanners' range doubling, and
tech trading via scrapping/invasion (entirely absent; the battle-triggered variant is deferred to the
combat rework below).

**Still open / deferred** (roughly in the order they're likely worth tackling):
- **Combat resolution — the big one.** Nova has a real end-to-end battle loop (10x10 grid, 16-round
  cap, move-then-fire) but almost every quantitative formula `combat-resolution.md` cares about is
  either a stub, dead/commented-out code, or missing outright: the attractiveness/targeting formula,
  initiative-based firing order (currently sorts by raw weapon initiative *ascending*, i.e.
  backwards, ignoring hull/computer bonuses), beam range falloff and deflector stacking (written but
  commented out), capital-missile double damage, the accuracy formula (computer/jammer contributions
  are a bare TODO), all six movement tactics (only one hardcoded behavior exists), the 256-token cap,
  and salvage. This needs a dedicated pass, likely the single largest remaining piece of work.
- Auto-build production orders: the `IsAutoBuild` plumbing exists but nothing (AI or GUI) ever
  actually creates one — every real order is a manual one-shot that blocks the queue if unaffordable.
- Slow Tech Advance (doubles research cost) and Bleeding Edge Technology: no game-setting/mechanic
  exists for either.
- Most remaining PRT/LRT mechanics beyond what's listed as fixed above: Super Stealth's cloak/passive
  research, Space Demolition's mine mechanics, Packet Physics, Interstellar Traveler's stargate perks,
  Regenerating Shields, Ultimate Recycling (implemented — worth double-checking its exact
  percentages), Advanced Remote Mining's starting units.
- Stargates (data stub only, no teleport/overgating-damage logic) and wormholes (absent entirely).
- Conditional cargo load/unload ("load up to X" / "unload down to X") — only fixed-amount transfers
  exist.
- Fleet-wide scanner range combination: deliberately left as-is (best single ship, not 4th-root
  combined) after determining the spec only sources that formula for combining scanners *within one
  ship design*, not *across* different ships in a fleet — flagged in code rather than guessed at.

## Open empirical questions (from cross-referencing public sources — need real testing to resolve)
Each spec flags its own open items in detail; the cross-cutting ones worth testing first in an actual
game are:
1. ~~Generalized Research trait: 115% vs 125%~~ **Partially resolved 2026-09-03**: read the actual
   in-game wizard description verbatim (see `race-traits.md` Open Questions) — it doesn't state
   either percentage, and is itself ambiguous on "15% each" vs "15% split total." Still needs a
   multi-turn research test to pin down the real per-field split, and a search for where "115%"
   might actually appear (a different help topic, or nowhere real).
2. ~~War Monger PRT: starting Weapons tech level — sources say 5 vs 6~~ **RESOLVED 2026-09-03**:
   confirmed **6** directly from the in-game Race Wizard description text.
3. ~~Claim Adjuster PRT: starting Construction tech level — sources say 0 vs 2~~ **RESOLVED
   2026-09-03**: confirmed **2** directly from the in-game Research screen (Energy 1, Weapons 1,
   Propulsion 1, Construction 2, Electronics 0, Biotechnology 6).
4. Negative-habitability ("red planet") population death rate — undocumented anywhere found — not
   yet tested empirically.
5. ~~Production queue: turn behavior when an item is resource-affordable but mineral-blocked~~
   **RESOLVED 2026-09-04**: yes, partial fulfillment happens at whole-unit granularity (see
   `production-queue.md` Open Questions for the full test: `Factory x100` queued against 265kT
   Germanium on hand completed 3 factories + 9% progress toward a 4th in one turn, rather than
   blocking at zero). Narrower sub-question still open: whether an auto-build item behind a
   *still*-blocking manual item gets funded the same turn (our test's auto-build item was already
   satisfied before the turn started, so it didn't actually test this).
6. Fleet movement: turn behavior when fuel can't cover the ordered warp speed (partial move vs
   auto-downgrade to a sustainable speed) — not yet tested empirically.
7. Combat: exact accuracy formula combining base accuracy/computers/jammers (only empirical
   before/after numbers found, not the formula) — not yet tested empirically.

## Testing setup (this session, 2026-09-03 — supersedes the "original machine" setup below)
Got the actual game running and scriptable on the `ROBSAMD` machine (not the "original machine"
described below, which turned out to be unreachable from this session):
- Game: Stars! v2.70j, JRC3 community patch (`stars27jrc3.zip` from starsfaq.com's wiki mirror,
  matches the wiki's cited "JRC4" fuel-table caveat's neighboring patch — worth double-checking
  which patch our specs should target). Requires `wavemix.dll` (also from starsfaq.com) dropped
  next to `stars.exe` or the game errors out on startup.
- Runtime: otvdm v0.9.0 (github.com/otya128/winevdm releases) — no install needed, run directly as
  `otvdm.exe stars.exe`.
- Both live at `C:\StarsGame\` on the `ROBSAMD` machine (game in `Game\`, otvdm in `otvdm\app\`).
  Not committed to the repo (copyrighted binary + large otvdm bundle) — `stars27jrc3.zip` briefly
  landed in the repo root during setup and should be deleted before any commit.
- Serial number used: a publicly-donated one from starsfaq.com/download.htm (not the user's
  personal serial) — fine to reuse, not treated as secret, but still not hardcoded into any spec.
- **Automation harness**: `C:\StarsGame\automation.ps1`, also checked into this repo at
  `tools/game-automation/automation.ps1` (+ `README.md` with full usage notes and lessons learned)
  — PowerShell + Win32 API (P/Invoke) driving the game entirely from Claude Code, no dedicated
  computer-use tool needed. Copy it back to `C:\StarsGame\automation.ps1` (or wherever the game
  lives) before reusing — it's not meant to run from inside the repo checkout.
  - `Pin-GameWindow` moves whatever the frontmost game window is to a fixed screen origin (100,100)
    so click coordinates stay stable across calls.
  - `Click-InGame x y` / `Type-InGame text` click/type relative to that pinned origin, always
    re-focusing the game window first (otherwise keystrokes can leak to whatever else has focus —
    happened once, typed a serial into the Claude Code chat box instead of the game).
  - `Click-Absolute x y` for popup dialogs Windows places on its own (not relative to the pinned
    window) — e.g. the Ship Design dialog.
  - `Screenshot path` captures the full screen to a PNG, read back via Claude's `Read` tool.
  - **Key lesson**: mouse clicks on this game's menu bar became unreliable partway through the
    session for no clear reason; keyboard shortcuts (F5 for Research, etc.) and Alt+mnemonic
    accelerators worked reliably throughout — prefer those over clicking menus.
  - Clicking the same map location repeatedly cycles selection through every fleet stacked there,
    then the planet — no single click jumps straight to "the planet" if fleets are in orbit.
  - The Production queue panel (select the planet, not a fleet) reads "--- Queue is Empty ---" when
    nothing is queued. **User-supplied fix (2026-09-03):** to add an item, click the **"Change"**
    button next to the queue (just to the left of the "Clear" button) — not the toolbar "Add"
    button. This "Change" button apparently has a UI flicker bug where it can visually disappear;
    if that happens, it's still there (same position, left of "Clear") and clickable.
  - **User tip (2026-09-03):** right-click a planet in the universe view to choose which target on
    it to select (the planet itself vs. any fleet in orbit there) — a real menu, unlike repeatedly
    left-clicking to cycle through occupants one at a time (which does also work, just slower).
  - Reference screenshots from this session live in `docs/ui-reference/` (race wizard steps,
    research screen, ship design screen, planet view, fleet report, plus cropped close-ups of
    specific findings).
- A **Windows Sandbox** config (`StarsSandbox.wsb`) exists for isolated play-testing on the
  original machine, mapping that folder in at `C:\Stars`. Mid-session this hit some rough edges (a
  screenshot briefly showed what looked like host network drives rather than an isolated desktop —
  not fully root-caused, treat with caution / verify isolation before reusing). Not used this
  session (superseded by the ROBSAMD setup above).
- Cross-machine/cross-session Claude-to-Claude messaging proved unreliable in this environment
  (confirmed again this session via `mcp__ccd_session_mgmt__list_sessions` finding nothing) — don't
  rely on it; coordinate via files and direct conversation with the user instead.

## Testing setup (original machine — unreachable this session, kept for reference)
- The original game supposedly runs via **otvdm** (Win16-on-Win64 shim) at
  `C:\Downloads\Games\Stars\`, launched via `Play Stars.bat`, on some other machine the user has
  physical/VNC access to. This session could not locate or reach that machine (checked the
  `\\10.1.1.60` share visible from `Z:\` — it's an unrelated home-server box, not this one).
  Serial number: see the user, do not commit it anywhere.

## Next steps
1. **Combat resolution rework** — see the "Still open" list above. Largest remaining piece.
2. Continue the empirical punch list above (items 1, 4, 6-7) using the automation harness at
   `C:\StarsGame\automation.ps1` on ROBSAMD — items 2, 3, 5 are resolved.
3. Work through the rest of the "Still open / deferred" list above (auto-build wiring, Slow Tech
   Advance, BET, remaining PRT/LRT mechanics, stargates/wormholes, conditional cargo transfers).
4. Keep building out `docs/ui-reference/` screenshots as a UI/UX reference for Nova's own (currently
   WinForms) front-end, or a future rewrite of it.
5. Set up a real GitHub fork of ekolis/stars-nova (this repo currently just has it as a `nova` git
   remote with its history merged in locally) so work here can actually be contributed back, per the
   2026-09-04 decision to evolve Nova rather than replace it. Needs the user's GitHub auth — not set
   up this session (no `gh` CLI available on ROBSAMD).
6. Get `Tests\Tests.csproj` building (NuGet restore for `NUnit3TestAdapter`) so Nova's existing test
   suite can actually run and catch regressions from this session's changes going forward.
