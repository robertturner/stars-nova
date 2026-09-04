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

**Running the tests**: `Tests\Tests.csproj` uses old-style `packages.config` NuGet restore (NUnit
3.12.0 + NUnit3TestAdapter 3.16.1), which `dotnet restore` doesn't handle — no NuGet.exe or Visual
Studio was present on this session's machine, so both `nuget.exe` (from
`dist.nuget.org/win-x86-commandline/latest/nuget.exe`) and the `NUnit.ConsoleRunner` NuGet package
(no vstest/VS available to run tests otherwise) had to be fetched to actually execute anything:
```
nuget.exe restore Nova.sln -PackagesDirectory packages
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" Tests\Tests.csproj /p:Configuration=Debug
nunit3-console.exe Build\Debug\Nova.Tests.dll
```
As of 2026-09-04, **65/67 tests pass** (up from a 59/67 baseline on the original pre-fork Nova code,
confirmed by building and running that exact commit for comparison — this session's changes fixed
6 previously-non-running tests and introduced zero regressions). Two pre-existing failures remain,
not investigated further as out of scope: `BattleEngineTest.Test4SelectTargets` (some fixture
asymmetry) and `RaceAdvantagePointCalculatorTest.calculateAdvantagePointsForStandardJoat` (in code
untouched this session). See the `ec90e63` commit message for the full story, including a genuine
shared-mutable-test-state isolation bug found and fixed in `StarTest.cs` along the way.

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
tech trading via scrapping/invasion (entirely absent; the battle-triggered variant is deferred, see
below).

**Combat resolution rework (2026-09-04), also done this session** — was the single largest gap:
Nova had a real end-to-end battle loop (10x10 grid, 16-round cap, move-then-fire) but almost every
quantitative formula was a stub, dead/commented-out code, or missing outright. Fixed/implemented:
the real attractiveness/targeting formula (was a placeholder), initiative-based firing order (was
sorting by raw weapon initiative *ascending* — backwards — and ignoring hull/computer bonuses), beam
range falloff and deflector stacking (written but entirely commented out, and inverted — subtraction
instead of multiplication — even in the dead code), capital-missile double damage (was a bare FIXME),
an accuracy approximation for computer/jammer effects (previously ignored entirely; explicitly
documented as unverified since no source states the original formula precisely), all six movement
tactics (previously one hardcoded "always close to point-blank" behavior — Disengage/Disengage-if-
Challenged/Minimise-Damage-to-Self now actually retreat), the 256-token cap (absent), and salvage
(absent; deposited on-planet only, deep-space decay isn't modeled). Also found and fixed two
consequential bugs while doing this: `Fleet.TotalCost` didn't multiply by ship quantity per token
(unlike the otherwise-identical `Mass` property), and — the big one — `CalculateWeaponPower` never
multiplied by the firing stack's ship count at all, so a 10-ship stack dealt exactly the same damage
per shot as a single ship of that design; fixing it made the pre-existing "whole token dies the
instant pooled armor hits zero" bug much more consequential, so whole-ship kill accounting (destroy
whole ships first via floor(damage/current-armor-per-ship), spread remainder across survivors) was
implemented alongside it.

**Still open in combat**: torpedoes/missiles resolve a whole weapon slot's damage as one hit/miss
roll rather than each individual missile independently (spec: "Each individual missile/torpedo in a
shot is resolved as an independent hit/miss check") — not fixed because Nova's data model collapses
multiple identical weapons in a slot into one combined `Power` value, losing the per-missile count
needed to loop over them; would need a small data-model change first. The persistent-tie-break-stays-
fixed-for-the-rest-of-the-battle rule for identical-initiative firing order isn't tracked (falls back
to whatever order the sort produces). The three "close toward target" movement tactics (Maximise
Damage / Net Damage / Damage Ratio) are all treated identically rather than modeling their documented
differences. Per-shot dynamic retargeting (a shot can hit a different target than the stack's overall
movement target if the intended one drifted out of range) isn't modeled — Nova tracks one target per
stack, not per weapon slot. Energy capacitors' beam-damage bonus isn't modeled (no source found gives
the exact percentage). The tech-trading-via-battle-kill trigger (§6 of research-tech-tree.md) still
isn't wired up, since it needs a hook into per-kill tech comparison that didn't exist before this pass
either.

**Still open / deferred elsewhere** (roughly in the order they're likely worth tackling):
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

## Live playtesting findings (2026-09-04)

Built `Nova.exe` (legacy MSBuild, see "Building" above), launched it for real, and drove it with a
new message-based Win32 automation harness (`C:\StarsGame\nova_automation.ps1`, local machine only,
not committed — mirrors `tools/game-automation/automation.ps1` but targets `Nova.exe` and clicks
child controls directly via `SendMessage`/`BM_CLICK` rather than cursor coordinates).

**Why message-based clicking instead of cursor clicks:** partway through the session the RDP
session's display detached (a multi-hour idle period was involved) and `CopyFromScreen` /
cursor-based `mouse_event` clicks stopped working entirely (screenshots came back blank/errored,
clicks landed nowhere). Reconnecting the RDP session fixed real cursor input and `CopyFromScreen`
again, but the message-based approach (`SendMessage`/`PostMessage` straight to a control's HWND,
`PrintWindow` for screenshots) turned out to be more reliable regardless and is worth keeping:
- `BM_CLICK` on a native BUTTON HWND reliably fires its Click handler — use this for buttons,
  checkboxes, and radio buttons. Safe even mid-session.
- **Do not** send synthetic `WM_LBUTTONDOWN`/`WM_LBUTTONUP` to a `SysListView32` via `SendMessage`
  (synchronous). ListView's internal mouse-tracking logic waits for a real button-up before the
  down-message's `SendMessage` call returns, so two sequential synchronous calls from the same
  script deadlock (the down call never returns, so the script never gets to send the up). Fix:
  either use `PostMessage` (async) for both, or if already stuck, `PostMessage` a `WM_LBUTTONUP`
  from a *separate* process/call to break the deadlock. Once unstuck, item selection itself worked
  fine and is safe to repeat.
- A synthetic `WM_LBUTTONDBLCLK` sent directly to a `SysListView32` (without real mouse-move/hover
  priming first) crashed `Nova.exe` outright — see bug below. Avoid double-clicking list views this
  way; single-click plus a separate action (Enter key, or a dedicated button) is safer where the UI
  offers one.
- Menu bars (`MenuStrip`) and their dropdowns are ordinary child/popup HWNDs — `PostMessage` a
  click on the menu bar at the item's pixel position to open the dropdown, find the resulting
  popup's HWND via `EnumWindows` (class `WindowsForms10.Window.20808...` = `ToolStripDropDown`),
  `PrintWindow` it to read the item positions, then `PostMessage` a click on the item. Works
  reliably; just get the pixel math right (zoom into a `PrintWindow` capture first) or you'll open
  the wrong menu item (Battle Plans instead of Research, in one instance here — harmless, just
  close it with a `WM_CLOSE` and retry).
- A native `NumericUpDown`'s edit portion doesn't reliably pick up `WM_SETTEXT` +
  `WM_KILLFOCUS` (reverted to the old value in testing). Driving it via real cursor click + focus +
  `SendKeys` (Ctrl+A, type digits, Tab) worked correctly instead. Clicking its spinner buttons via
  synthetic mouse messages did *not* register at all (control ignores them, presumably because it
  checks real button state, not just the message content) — don't rely on that path.
- `ShowDialog()`-opened modal dialogs and open menus block the calling `SendMessage`/`BM_CLICK`
  call until they close — this is normal, not a hang. Run the click in the background, then poll
  `EnumWindows` for the new top-level window from a *separate* command while it's open.
- **A `MessageBox.Show(..., MessageBoxOptions.DefaultDesktopOnly)` fatal-error dialog is easy to
  miss** if you're only checking `EnumWindows`/process CPU rather than actually looking — one
  showed up on screen the whole time during what looked like a hung process (near-zero CPU,
  `Wait`/`LpcReply` thread state) with nothing found by `EnumWindows` in one investigation pass.
  Turned out to be an ordinary path-quoting bug (see below), not an environment/display problem at
  all. **Lesson: take an actual screenshot before concluding a WinForms process is "hung"** — a
  blocked modal message pump looks identical to a real hang from the outside.

**Bug found — path-quoting truncation, not Nova's fault:** `Start-Process -ArgumentList @(...)` in
Windows PowerShell 5.1 does **not** quote array elements containing spaces when it joins them into
the child process's command line. `C:\Users\Robert\Documents\Stars! Nova\Feel the Nova\Rabbitoid.intel`
got silently truncated at the space after `Stars!`, which `Nova.exe --gui` correctly rejected with a
fatal-error dialog ("Could not locate .intel file"). Fix: wrap any argument containing spaces in
its own literal escaped quotes before adding it to the `-ArgumentList` array, e.g.
`` "`"$intelPath`"" ``.

**Follow-up 2026-09-04 (later session): not a real bug — confirmed testing artifact.**
Double-clicking a row in the Nova Console's player list (`PlayerList_DoubleClick`,
[NovaConsole.cs:237](../Nova/WinForms/NovaConsole.cs#L237)) had crashed the whole process with an
`AccessViolationException` inside `ListView.WndProc` → native `comctl32.dll` when triggered by a
*synthetic* `WM_LBUTTONDBLCLK` message sent directly via `SendMessage` (no preceding real
mouse-down/hover state). Retested with **genuine** mouse input once the RDP session's display was
healthy again (real `SetCursorPos` + `mouse_event` double-clicks): it did not crash once, including
8 repeated real double-clicks on the same row spaced across multiple 2.5s `consoleTimer` ticks (to
rule out a `SetPlayerList()`/timer race, which was the other suspected cause — also ruled out). Each
real double-click correctly opened a `NovaGUI` window for that player, exactly as designed. No code
change made — there is nothing to fix. **Lesson for future automation sessions:** never synthesize
`WM_LBUTTONDBLCLK` directly into a native `SysListView32` via `SendMessage`/`PostMessage`; it can
crash comctl32 even though the exact same user action via real input is completely safe. See the
warning already in `tools/game-automation/nova_automation.ps1`. Launching the GUI directly via
`Nova.exe --gui -r <race> -t <turn> -i <intel file>` remains a convenient way to open a specific
player's turn for testing without going through the Console UI at all, but is no longer needed as a
crash workaround.

## Follow-up fix (2026-09-04, later session): Scrap Fleet was genuinely unreachable + payout bug

User asked where the "scrap fleet" command was — couldn't find it in the UI while actually playing.
Turned out **it wasn't a "where do I look" problem — the control was completely unreachable**, a
real, longstanding upstream layout bug (not introduced this session). Scrapping is a Waypoint Task,
same category as Colonise/Invade/Lay Mines, exposed via a "Waypoint Task" dropdown at the bottom of
[FleetDetail](../Nova/WinForms/Gui/Controls/FleetDetail.cs)'s left column. Two independent bugs
combined to hide it entirely, in every build, regardless of window size:

1. **Clipped by an undersized container.** `FleetDetail`'s own designed content is 453px tall (its
   last group, "Waypoint Task", sits at y=392-452), but its host container `selectionDetail`
   ([NovaGUI.cs:86](../Nova/WinForms/Gui/NovaGUI.cs#L86)) was only sized 406px tall. Since
   `FleetDetail` is docked `Fill` inside it, everything past y=406 - the entire Waypoint Task group
   - was silently cut off. No scrollbar, no error, nothing suggesting content was missing.
2. **Painted over anyway, even where it did fit.** The `messages` panel ("Year XXXX - Message",
   `Location = Point(8, 412)` in
   [NovaGui.Designer.cs](../Nova/WinForms/Gui/NovaGui.Designer.cs#L225) - note the filename casing
   difference from `NovaGUI.cs`, easy to miss when searching) directly overlapped where the Waypoint
   Task group would sit, and was added to the form's `Controls` collection *before* `selectionDetail`
   - lower index = higher z-order in WinForms = painted in front - so even a hypothetically-tall-enough
   `selectionDetail` would have had its Waypoint Task group painted over by the message panel.

Fixed by growing `selectionDetail` to its full needed 453px and shifting `messages` and
`selectionSummary` down by the same +47px (and growing the form's `ClientSize`/`MinimumSize` and the
Star Map panel to match), preserving the original tight spacing between sections rather than
introducing new overlaps. Verified live: the "Waypoint Task" dropdown is now visible, selectable,
and correctly drives `Waypoint.LoadTask()`/`WaypointCommand` exactly as the (previously unreachable)
code already supported. **To scrap a fleet**: select it, select its *first* waypoint (its current
position - only waypoint index 0 is checked at turn-processing time, see `ScrapFleetStep.cs`) in the
Waypoints list, then set "Waypoint Task" to **Scrap**. Takes effect on turn generation, not
immediately.

While verifying that path, found a real bug in the payout math:
[ScrapTask.cs](../Common/Waypoints/ScrapTask.cs) `Perform()` computed the correctly-scaled
`returned` `Resources` (33%/45%/80%/90% of the fleet's mineral cost depending on starbase presence
and the Ultimate Recycling trait, per the doc comment already on the method) into a local variable
— and then never used it. Instead it re-read `fleet.TotalCost` (a *computed property* that
rebuilds a brand-new `Resources` object from `Composition` on every single access — see
[Fleet.cs:376](../Common/GameObjects/Fleet.cs#L376)) two more times: once to set `.Energy` on a
throwaway temporary (no effect at all), and once more to add to `star.ResourcesOnHand` — meaning
the star was credited with the fleet's **full, unscaled** mineral cost every time, regardless of
location or race trait. Net effect: scrapping was far more generous than intended (100% mineral
recovery everywhere, including scrapping at a bare planet with no starbase, which should only give
33%/45%). Fixed by reading `fleet.TotalCost` into a local once and actually using the scaled
`returned` value. Verified: `Tests/UnitTests/TurnGeneratorTest.cs`'s two scrap-related tests still
pass (they only assert the fleet disappears, not the exact payout — worth adding a payout-amount
assertion there at some point). Full test suite still at the same 65/67 baseline (the 2 failures are
pre-existing and unrelated — `BattleEngineTest.Test4SelectTargets` and
`RaceAdvantagePointCalculatorTest.calculateAdvantagePointsForStandardJoat`).

## Follow-up fix (2026-09-04, same day, third pass): Waypoint Task selection never actually worked

User reported that after finally being able to see and select "Scrap" (previous fix), the fleet
was untouched after the next turn, and re-selecting the waypoint afterward showed the task had
reverted to "None". Root cause was much more fundamental than either prior fix:

**`WaypointTasks.SelectedIndexChanged` was never wired to anything.**
[FleetDetail.cs](../Nova/WinForms/Gui/Controls/FleetDetail.cs) has a complete, correct
`WaypointTaskChanged` handler that builds a `WaypointCommand` and applies it - but nothing in
[FleetDetail.Designer.cs](../Nova/WinForms/Gui/Controls/FleetDetail.Designer.cs) ever subscribed it
to the combo box's event. Selecting a value visually changed the dropdown (that's just the native
control's own display state) but had **zero effect on game state** - not "forgot to submit", not
"got reset somehow" - the click literally never reached any code that mattered. This means Scrap,
Colonise, Invade, Unload Cargo, and Lay Mines have likely never worked via this dropdown in any
build of Nova, ever, independent of the visibility bug fixed earlier today. Fixed by adding
`this.WaypointTasks.SelectedIndexChanged += new System.EventHandler(this.WaypointTaskChanged);`.
Verified live: selected Scrap, navigated to a different selection and back, waypoint task correctly
still showed "Scrap" (previously always reverted to "None", since the underlying fleet data was
genuinely never touched). Also verified `WaypointCommand` correctly serializes to `<ScrapTask />`
XML in the `.orders` file on submit - the whole pipeline (dropdown -> command -> apply -> serialize)
now works end to end.

Also added, per user request: a confirmation prompt on closing the GUI
([NovaGUI.cs](../Nova/WinForms/Gui/NovaGUI.cs), `NovaGUI_FormClosing`) when
`clientState.Commands.Count` has grown since the last successful "Save & Submit Turn" - previously
closing the window (including via the X button) silently discarded any pending orders with zero
warning, which is exactly how the user's first scrap attempt was lost. The check is a simple
before/after count comparison, not a true diff, so it can theoretically miss a same-count
edit-that-replaced-an-edit (the existing "minimizing clutter" dedup logic in `WaypointTaskChanged`
pops and replaces the last command for the same waypoint) - a rare, low-severity edge case, not
worth the complexity of real dirty-tracking to close.

**Caution for future sessions testing this GUI**: verifying this fix (selecting Scrap, then testing
the close-confirmation dialog) accidentally left a stray `Robsters.orders` file in the user's real
"Feel the Nova" save, containing a live `<ScrapTask />` command, when the new confirmation dialog
got dismissed by some stray queued input from earlier test clicks rather than an intentional
choice - a reminder that automated input against a *live* save can have live side effects, not just
against disposable test saves. User was notified and asked to review/remove the stray order before
generating another turn.

## New feature (2026-09-04, same day, fourth pass): insert a waypoint mid-route

User wanted to add a waypoint ahead of or in the middle of an existing route (e.g. a fleet already
en route to several stars), rather than only being able to append to the end - previously the only
workaround was appending past the target, then adding another waypoint back near the desired
insertion point, then deleting the now out-of-order original, since there was also no way to
reorder waypoints once added.

Root cause of the limitation: `WaypointCommand.ApplyToState`'s `CommandMode.Add` case always calls
`Waypoints.Add(...)` (append), completely ignoring its own `Index` field - this is the *only* way
new waypoints were ever created (`StarMap.cs`'s `LeftShiftMouse`, the Shift+Click handler). Several
existing call sites (`DefaultFleetAI.cs`, `CargoDialog.cs`, `FleetDetail.cs`'s split/merge path)
already pass an `Index` value that `Add` has always silently ignored, so simply making `Add` respect
`Index` risked silently changing behavior for all of them in ways not possible to fully audit with
confidence. Instead, added a new `CommandMode.Insert` ([ICommand.cs](../Common/Commands/ICommand.cs))
handled only by `WaypointCommand.ApplyToState`
([WaypointCommand.cs](../Common/Commands/WaypointCommand.cs)) - `Waypoints.Insert(Index, Waypoint)` -
leaving every existing `Add` call site completely untouched. Also added bounds-checking to
`IsValid()` for the new mode (an out-of-range `Insert` throws and would otherwise crash *all* turn
processing, not just this one order - `ApplyToState` runs identically on both the client, for
immediate GUI feedback, and the server, via `TurnGenerator.ParseCommands()`, since it's shared code
in `Common/`).

For the actual UX: [FleetDetail.cs](../Nova/WinForms/Gui/Controls/FleetDetail.cs) gained a
`SelectedWaypointIndex` getter (whichever row is selected in the Waypoints list, or -1).
[StarMap.cs](../Nova/WinForms/Gui/Controls/StarMap.cs) gained a `GetWaypointInsertIndex` delegate
that `LeftShiftMouse` consults: if a waypoint *other than the last one* is selected, Shift+Click now
inserts the new waypoint immediately after it instead of appending to the end; selecting nothing or
the last waypoint preserves the original append-at-end behavior exactly. Wired up once in
[NovaGUI.cs](../Nova/WinForms/Gui/NovaGUI.cs)'s constructor
(`MapControl.GetWaypointInsertIndex = () => SelectionDetail.FleetDetail.SelectedWaypointIndex;`).
Also had to extend the existing "minimizing clutter" dedup logic in `FleetDetail.cs` (two near-
identical blocks, `WaypointSpeedChanged` and `WaypointTaskChanged`) to also protect `Insert`
commands from being popped/discarded, the same way it already protected `Add` - otherwise
immediately adjusting a freshly-inserted waypoint's speed or task would silently erase the insert.

**To insert a waypoint**: select the fleet, select the waypoint it should follow in the Waypoints
list (not the last one), then Shift+Click the new destination on the map - same gesture as adding a
waypoint normally, the destination just depends on what's selected first.

**Verification**: added
[Tests/UnitTests/WaypointCommandTest.cs](../Tests/UnitTests/WaypointCommandTest.cs) - 6 new tests
directly covering `CommandMode.Insert` (mid-list insert, insert-at-count behaves like append,
insert-at-zero, `IsValid` rejecting negative/past-end indices, confirming `Add` is completely
unaffected). All pass; full suite now 71/73 (same 2 pre-existing, unrelated failures as always -
`BattleEngineTest.Test4SelectTargets` and
`RaceAdvantagePointCalculatorTest.calculateAdvantagePointsForStandardJoat`).

**Not verified live end-to-end this session.** Attempted to verify in a fresh, fully isolated
throwaway game (`C:\StarsGame\WaypointTestGame` - deliberately *not* the user's real save, precisely
to avoid a repeat of the earlier stray-orders incident) but hit an environment-level regression
partway through: `GetForegroundWindow()` started returning null and synthetic `keybd_event`
modifier-key state (needed to simulate Shift+Click) stopped being observable by the target process's
`Control.ModifierKeys`, even via synchronous `SendMessage` timed to hold Shift throughout. This
matches the same intermittent display/input-desktop degradation documented earlier in "Live
playtesting findings" (that time affecting `CopyFromScreen`/real cursor clicks; this time
`GetForegroundWindow` and synthetic keyboard state) - `query session` showed the RDP session number
had incremented (`rdp-tcp#1`, previously `#0`), consistent with a reconnect leaving old processes'
input-desktop attachment in a bad state. Was able to work around it for plain clicks (found and
clicked the actual inner `mapControl` UserControl rather than its outer decorative GroupBox, which
looks identical in a raw window dump - class `WindowsForms10.BUTTON...`, text "Star Map" - a mistake
worth flagging for future map-clicking automation), but not for the Shift modifier specifically.
Given the core logic is solidly covered by the new unit tests and the wiring is a small, direct,
carefully-traced change, confidence is high, but this specific feature has not been clicked through
by a human or a working automated session yet - worth an actual playtest before considering it fully
closed out.

## New feature (2026-09-05): fleets no longer fly through multiple waypoints in one turn

User reported a scout sent to a planet "left the planet unexplored," and separately recalled that
in the original game, arriving at a planet uses up all of a fleet's movement for that turn. Both
point at the same bug: `TurnGenerator.cs`'s `UpdateFleet` loops `while (fleet.Waypoints.Count > 0)`,
calling `Fleet.Move()` once per iteration and immediately continuing to the *next* waypoint in the
same turn if any of that turn's movement budget (`availableTime`, starting at 1.0 per year) was
left over after arriving at the first one. A fast enough fleet with closely-spaced waypoints could
therefore fly through several stars - including ones it was only passing by, never meant to
linger at - in a single turn, before anything (a scan report, an "explored" flag) ever registered
having been there. `docs/behavior-specs/fleet-movement-scanning-cargo.md` §5 already said as much
("... executes that waypoint's task once it actually arrives, then proceeds to the following
waypoint the next turn"), just without enough emphasis to have been caught as a discrepancy until
directly observed in play.

Fixed with one targeted `break` in `TurnGenerator.cs`'s `UpdateFleet`: after a real arrival (the
fleet's position actually changed to get there), stop processing further waypoints this turn.
Snapshot `fleet.Position` before calling `Move()`, compare after - if unchanged, don't stop. That
last part matters: this loop already has a legitimate zero-distance case it must keep allowing to
continue in the same turn - when a fleet is left `InTransit`, the loop re-inserts a "resume from
here" placeholder waypoint (`Position` set to wherever the fleet actually stopped) ahead of the
real remaining route; next turn, arriving at that placeholder costs no real distance and must be
consumed for free before continuing on with that turn's actual movement, or an in-transit fleet
would need two turns to make any further progress at all. Position-before/after comparison cleanly
tells the two cases apart without needing to touch that mechanism.

**Verification**: added `TurnGeneratorTest.Generate_StopsAtFirstWaypoint_EvenWithLeftoverMovement`
- a fleet at warp 9 (81 ly/year) given two waypoints 1 ly apart each; confirmed the test fails
without the fix (fleet ends up at the second star) and passes with it (stops at the first, second
waypoint still pending). Full suite now 72/74 (same 2 pre-existing, unrelated failures as always).

## New feature (2026-09-05): Packet Physics / Interstellar Traveler second starting planet

User noticed Interstellar Traveler didn't get its documented second starting planet, and asked for
a broader pass over racial-trait implementation. Traced to a genuinely large, previously-unnoticed
gap: `ServerState/NewGame/StarMapInitialiser.cs` has a `switch` over every Primary Racial Trait
listing exactly what ships/planets each one should start with — but the **entire switch statement
is inside a `/* ... */` comment block** and has never executed, for any PRT. Every race currently
starts with exactly one scout, one colony ship, one starbase (three colony ships for Hyper
Expansion specifically, the one piece of this system that *is* live, via a separate `if` outside
the dead block). See `docs/behavior-specs/race-traits.md`'s Open Questions for the full writeup.

Given the size of correctly implementing distinct starting *fleets* for all 10 PRTs (most are only
sketched as one-line comments, not sourced with the same confidence as the tech-level numbers
below), this pass scoped down to the one concrete, well-documented, user-verified gap: **Packet
Physics and Interstellar Traveler both start with a second homeworld-tier planet**
(`race-traits.md` §2 — PP: "Starts with a second homeworld-tier planet"; IT: "Starts with two
Stargate-equipped planets"). Implemented in `StarMapInitialiser.cs`'s `InitializeHomeStar`: after
the normal home star is set up, a race with either trait is granted the *nearest currently-unowned*
regular star (from the ones `GenerateStars()` already placed), given the same
habitability/population/resources treatment as a real homeworld via the existing
`AllocateHomeStarResources`. Deliberately does **not** draw from `map.Homeworlds` — that list is
sized to exactly the player count by `StarMapGenerator.PlaceHomeworlds()`, so taking a second entry
for one player would leave a later-processed player with no home star at all and hit the existing
`Report.FatalError("Could not allocate home star")`. IT's specific "Stargate-equipped" detail isn't
reflected yet (stargates aren't implemented in this codebase at all — a separately-tracked gap);
PP's "(non-tiny universes)" qualifier isn't checked either. Both noted as follow-ups in the spec.

While in there, also found and fixed a second, concrete, **data-verifiable** discrepancy: PP's
starting bonus is documented as "Mass Driver tech up to level 13," which `GameInitialiser.cs` had
implemented as Energy tech level **4**. "Level 13" turns out to refer to the mass-driver
component's own tier name — this project's `components.xml` names its mass-driver-family
components `Mass Driver 5/6`, `Super Driver 7/8/9`, `Ultra Driver 10/11/12/13`, and lists `Ultra
Driver 13` itself as requiring **Energy tech 24**, not 4 (which only reaches the far weaker `Mass
Driver 5`). Fixed to 24. This is a cross-reference against the project's own data, not a live-game
re-verification like the War Monger/Claim Adjuster tech-level fixes from the previous session —
worth confirming against the real game if it becomes reachable again.

**Verification**: added
`NewGameTest.GeneratePlayerAssets_PacketPhysicsAndInterstellarTraveler_GetSecondPlanet` — runs full
map + player-asset generation for one IT race and one plain race, asserts the IT race ends up
owning exactly 2 stars and the plain race exactly 1. No existing test touched PP's Energy level, so
nothing else needed updating for that fix. Full suite now 73/75 (same 2 pre-existing, unrelated
failures as always).

**Confirmed working end-to-end in the live app** (all from this session's fixes):
- New Game dialog: tab switching, Add/Delete player, Race Name / Human-AI combo selection, Create
  Game — all produced the expected state changes and started a real game (`Feel the Nova`, year
  2100, Rabbitoid vs. Default AI Antetheral).
- Production dialog ([ProductionDialog.cs](../Nova/WinForms/Gui/Dialogs/ProductionDialog.cs)):
  **Mineral Alchemy** and **Terraform** now appear in Available Designs (previously
  `NotImplementedException` stubs, fixed this session) and can be added to the queue; Mineral
  Alchemy priced correctly at 100 resources / 3 years, matching the spec. Queue state round-tripped
  correctly back to the main window's Production Queue panel.
- Research dialog: for a start-of-game empire with Propulsion 6 / Construction 5 (all others 0),
  the displayed "Resources needed to research next level" for Biotechnology (80) and "years to
  completion" (27, at 3 resources/year) both match hand-calculation against the rewritten
  `Research.Cost()` formula (`BaseCost[1]=50` + tech-investment surcharge `(6+5)*10=110` = 160,
  × this race's 50% Biotechnology cost factor = 80) — confirms the real `BaseCost` table
  ([Research.cs](../Common/Research.cs)) replaced the old Fibonacci placeholder correctly.
- Race Designer, Environment tab: the `maxGrowth` NumericUpDown's new `Minimum = 1`
  ([RaceDesigner.cs:901](../Nova/WinForms/RaceDesigner/RaceDesigner.cs#L901)) correctly clamps —
  typing `0` and tabbing away snapped the field back to `1` rather than accepting 0% growth.

Not yet exercised live: combat (no battle has actually occurred in a live session yet — would need
two armed fleets in the same system, more turns than tested here), tech trading, warp-10 engine
destruction, fuel-shortfall mechanics, cloaking/scan-range interactions, stargates. These are all
still only verified by unit tests / source reading per the sections above.

## Testing setup (original machine — unreachable this session, kept for reference)
- The original game supposedly runs via **otvdm** (Win16-on-Win64 shim) at
  `C:\Downloads\Games\Stars\`, launched via `Play Stars.bat`, on some other machine the user has
  physical/VNC access to. This session could not locate or reach that machine (checked the
  `\\10.1.1.60` share visible from `Z:\` — it's an unrelated home-server box, not this one).
  Serial number: see the user, do not commit it anywhere.

## Next steps
1. ~~Get `Tests\Tests.csproj` building~~ **DONE 2026-09-04** — see "Running the tests" above (65/67
   passing).
   ~~Playtest the changes in an actual running Nova.exe~~ **DONE 2026-09-04** — see "Live playtesting
   findings (2026-09-04)" below. Confirmed working end-to-end: New Game creation, Production dialog
   with Mineral Alchemy/Terraform now buildable, Research cost table math, Race Designer growth-rate
   clamp. A suspected crash bug (NovaConsole player-list double-click) turned out on retest with
   real mouse input to be a synthetic-input testing artifact, not a real bug — see "Live playtesting
   findings" for the full story. Combat itself still not exercised in a live battle — that remains
   the highest-priority follow-up. Worth adding real unit-test coverage for combat/production/research
   specifically, since the existing suite barely
   touches what this session changed.
2. Work through the "Still open in combat" and "Still open / deferred elsewhere" lists above —
   auto-build wiring, Slow Tech Advance, BET, remaining PRT/LRT mechanics, stargates/wormholes,
   conditional cargo transfers, per-missile independent resolution.
3. Continue the empirical punch list below (items 1, 4, 6-7) using the automation harness at
   `C:\StarsGame\automation.ps1` on ROBSAMD — items 2, 3, 5 are resolved. Item 7 (combat accuracy)
   is especially worth prioritizing now, to check the approximation added this session against the
   real game's actual behavior.
4. Keep building out `docs/ui-reference/` screenshots as a UI/UX reference for Nova's own (currently
   WinForms) front-end, or a future rewrite of it.
5. Set up a real GitHub fork of ekolis/stars-nova (this repo currently just has it as a `nova` git
   remote with its history merged in locally) so work here can actually be contributed back, per the
   2026-09-04 decision to evolve Nova rather than replace it. Needs the user's GitHub auth — not set
   up this session (no `gh` CLI available on ROBSAMD).
