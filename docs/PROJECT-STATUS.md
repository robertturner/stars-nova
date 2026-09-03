# Stars! Clean-Room Reimplementation — Status

## What this is
A clean-room reimplementation of the 1995-2000 4X space strategy game "Stars!" in C#/.NET.
The original `stars.exe` is closed, commercial, abandoned software (not open source, not public domain —
the maker stopped selling it and released activation serials for free, but that does not waive copyright).

## Ground rule (read before touching anything)
This implementation is written **only** from documented external behavior — the manual, community
FAQs/wikis, and our own empirical play-testing of the running original game. It must **never** be
written by reading the original code, disassembly, or decompilation. A Ghidra project analyzing
`stars.exe` exists elsewhere on the original machine for unrelated exploration — it must not be
consulted for anything that feeds into this implementation. This separation is what makes the
reimplementation defensible; collapsing it turns the code into a derivative work.

## Layout
- `docs/behavior-specs/*.md` — six behavior specs, each documenting one subsystem in original wording,
  cited to public sources, with worked numeric examples. **All six are complete.**
  - `population-growth.md` — habitability, population growth, mineral mining
  - `production-queue.md` — production queues, resource allocation
  - `research-tech-tree.md` — tech fields, research point allocation, cost curve
  - `combat-resolution.md` — battle mechanics, targeting, damage
  - `fleet-movement-scanning-cargo.md` — warp/fuel, scanning, cargo
  - `race-traits.md` — PRTs, LRTs, race customization sliders
- `Stars.Core` (`src/Stars.Core`) / `Stars.Core.Tests` (`tests/Stars.Core.Tests`) — C#/.NET 9 class
  library + xUnit test project, scaffolded and building (`dotnet test` from the repo root; solution
  file is `StarsClone.sln`). Pure domain logic, no UI.
  - `Population/` — habitability (`Habitability`), population growth (`PopulationGrowth`), and
    mineral concentration/mining (`MineralMining`) implemented from `population-growth.md`, with
    unit tests built from that spec's worked examples (28 tests passing). No other subsystem's
    domain code exists yet.

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
1. Continue the empirical punch list above (items 1, 4-7) using the now-working automation harness
   at `C:\StarsGame\automation.ps1` on ROBSAMD.
2. Keep building out `docs/ui-reference/` screenshots as a UI/UX reference for eventually building
   a real front-end for `Stars.Core` (user request, 2026-09-03) — capture every distinct screen
   (Score, Production queue, Battle VCR, Fleet waypoints, Planet report detail, etc.), not just the
   ones needed for empirical questions.
3. Implement the remaining five specs into `Stars.Core`, each with worked-example-derived xUnit
   tests, following the pattern established by `Population/`: production-queue, research-tech-tree,
   combat-resolution, fleet-movement-scanning-cargo, race-traits (race-traits is mostly data/config
   feeding the other four rather than its own formulas).
4. Not yet a git repository — consider `git init` once there's a shared remote to push to. If/when
   that happens, make sure `C:\StarsGame\` and any stray copies of the game zip are never added.
