# Behavior-Specs-5 Audit — coverage vs. current implementation

Audit date: 2026-09-22. Compares `docs/behavior-specs-5/` (18 files) against the current C#
implementation across `Common/`, `ServerState/`, `Nova.Ai/`, `Nova.Avalonia/`, and the WinForms
reference (`Nova/WinForms/`).

Legend: **CONTRADICTION** = code currently does something that conflicts with the spec (a bug).
**GAP** = spec describes something not implemented at all, or only partially. **CONFIRMED** = code
already matches. **AMBIGUOUS** = spec itself leaves a real implementation choice open, or is
simply silent on a question the code has to answer.

---

## Scope-defining fact: behavior-specs-5 is 13/18 files identical to behavior-specs-4

Before auditing anything, a byte-for-byte diff against `docs/behavior-specs-4/` (which already has
a thorough audit, `docs/behavior-specs-4-audit.md`, dated 2026-09-11) found **13 of the 18 files are
byte-identical** between the two spec directories. Only 5 differ, and only by small, additive
patches — no file was rewritten wholesale:

| File | Diff size | What changed |
|---|---|---|
| `client-interface.md` | ~30 new lines | **Large new section**: the map's planet "ring/bar" display modes (habitability, population, mineral bars), the fleet-in-orbit ring, a fleet ship-count badge, a tracked-object diamond marker, a bitmap-resource inventory, and the exact view-option bit assignments (0x10/0x20/0x40/0x80) tying it all together |
| `production-queue.md` | ~10 new lines | The production queue's exact per-item text-color scheme (green/blue/red/gray) and its RGB values |
| `client-ui-dialog-catalog.md` | 1 line | Minor cross-reference addendum to an already-unresolved "Mode 4" mystery — no new actionable content |
| `fleet-movement-scanning-cargo.md` | 2 new lines | Confirms (doesn't change) that no dynamic fleet-interception/pursuit mechanic exists in the original game |
| `race-traits.md` | ~30 lines, but **backwards** | See "Documentation hygiene" below — v5 actually contains *staler* text here than v4 |

This means the bulk of this audit is **re-verifying the existing v4 audit's findings against
today's code** (11 days of further development since 2026-09-11), plus a focused deep-dive on the
5 changed files, plus auditing session work done since 2026-09-11 that the v4 audit couldn't have
covered (a Stargate mechanic rewrite, a production auto-build throttle, a ship-mass display).

---

## Coverage summary

### Contradictions re-verified (v4 audit's 17-item list, checked against current code)

**13 of 17 NOW-FIXED, 1 PARTIALLY-FIXED, 3 STILL-BROKEN (all three deliberately deferred — the spec
itself doesn't resolve the correct replacement value), 0 REGRESSED.** No drift in either direction
beyond what the v4 audit already recorded. Full item-by-item list below.

*(Bookkeeping note: the v4 audit's own "Fix status" prose says "18 contradictions" and cites
items up to "18," but the numbered list itself only runs 1-17 — a pre-existing off-by-one in that
document, not something new. This audit uses the actual 1-17 list.)*

### Gaps re-verified + newly found

- **Real additional progress since 2026-09-11**, beyond what the v4 audit could have known about:
  combat's target-type classifier is now wired up, tech-gain-from-battle is implemented, Disengage
  retreat works, and `SplitMergeTask` now has probabilistic fuel-stranding — all genuine narrowing
  of previously-documented gaps.
- **One brand-new, substantial gap**, from v5's new client-interface.md content: the map's
  "Planets:" display-mode system (habitability ring, population ring, two mineral-bar variants),
  the tracked-object diamond marker, the Packet-Physics Mass-Driver overlay, route-overlap dashing,
  and the entire shared view-option/keyboard-shortcut infrastructure are **all absent**. Some
  adjacent pieces (fleet-in-orbit ring, ship-count badge, scan-range wash, minefield visibility) are
  already implemented in a simplified, always-on form and partially cover the same visual area —
  see the dedicated section below.
- **One item fully closed**, from v5's new production-queue.md content: the queue-item color
  scheme is fully implemented (verified this session) — no longer a gap at all.
- All previously-documented gaps not touched by recent work (AI §10, mining depletion curve, race
  archetypes, galaxy-gen algorithm divergence, missing boolean game-option flags, research
  discounts, tutorial system, save-format caps, `dynamic-string-table.md`'s N/A status) are
  **unchanged** — confirmed still present, not re-litigated in full below since nothing changed.

### New session work not sourced from any spec (flagged, not "fixed" or "broken")

- **Stargate "use Stargate" warp-speed selection** (this session): the specs document Stargate
  jump *eligibility* (both ends gated, fuel-only cargo, mass/range limits) in detail, but are
  **entirely silent** on how a player actually orders one — no client-side waypoint/speed-selector
  dialog was ever traced in any pass. This was built from your own direct account of the original
  game rather than from decompiled code. It doesn't contradict anything the specs document; it
  fills a gap the reverse-engineering pass never reached. See "Ambiguous" below.
- **Production auto-build population throttle** (this session): the spec's own worked examples in
  `production-queue.md` §3/§9 never exercise a case where an auto-build target exceeds the
  population-operable cap in the same turn it's ordered, so there's no worked example to confirm
  or contradict against. The "grow into your infrastructure" mechanic itself (factories beyond the
  cap sit idle, not wasted) is unchanged and still correctly implemented; the new throttle only
  paces *how fast* auto-build races toward a target that exceeds today's cap, which the spec
  doesn't quantify either way.
- **Ship mass display** (this session, UI only): pure usability addition, directly consistent with
  (not contradicting) the per-ship mass-check formulas `fleet-movement-scanning-cargo.md` already
  documents for Stargate overgating. No compliance question here.

---

## CONTRADICTIONS (re-verified against current code, 2026-09-22)

1. **Turn phase ordering** — **NOW-FIXED**. `ServerState/TurnGenerator.cs:161-168`: `battleEngine.Run()`
   runs before the fleet-movement (`ProcessFleet`) loop, matching the spec's phase order, with a
   comment citing it.
2. **`LayMinesTask.Perform()` no-op** — **NOW-FIXED**. `ServerState/LayMines.cs` +
   `TurnGenerator.cs:555-561` now do the real work, dispatched right after `Perform()` succeeds.
3. **Minefields never regrow** — **STILL-BROKEN, deliberately**. `ServerState/CheckForMinefields.cs:67`
   only decays; the spec gives no regrowth formula to implement against.
4. **Alternate Reality research-cost penalty inverted** — **NOW-FIXED**.
   `Common/RaceDefinition/RaceAdvantagePointCalculator.cs:388` now penalizes Energy==Expensive, not
   Cheap.
5. **Victory condition defaults wrong** — **NOW-FIXED**. `Common/Files/GameSettings.cs:58-65` matches
   the spec's confirmed defaults (TechLevels/NumberOfFields enabled at 22/4, TotalScore 11000,
   SecondPlaceScore enabled at 100, ProductionCapacity 100).
6. **`TargetsToMeet` not derived from enabled-condition count** — **NOW-FIXED**.
   `Nova.Avalonia/ViewModels/NewGameViewModel.cs:286-306` clamps to the actual enabled-condition
   count.
7. **Invented overfull-population decline** — **NOW-FIXED**. `Common/GameObjects/Star.cs:379-390`
   plateaus at capacity instead of declining.
8. **Habitability malus cap doubled by Total Terraforming** — **NOW-FIXED**.
   `Common/RaceDefinition/Race.cs:283-286` (`GetMaxMalus()`) unconditionally returns 15.
9. **Battle Plan limit (10 vs. 15)** — **NOW-FIXED**. `Common/GlobalDefinitions.cs:158`
   (`MaxBattlePlans = 16`, i.e. 15 additional plus the un-removable first plan).
10. **Defense unit cost/mineral charge** — **PARTIALLY-FIXED**. `GlobalDefinitions.cs:135-138` no
    longer charges minerals (the clear bug is fixed), but the total resource cost (15) still doesn't
    match any of the spec's three race-derived branches (25/44/48) — the code's own comment
    discloses this remains open.
11. **Terraform cost doesn't match any decompiled branch** — **STILL-BROKEN, deliberately**.
    `Common/Production/TerraformProductionUnit.cs:63` unchanged (100/70); the spec's 70/110/120
    branches aren't confirmed enough to pick the right replacement.
12. **Scrap-fleet recovery rates** — **STILL-BROKEN, deliberately**. `Common/Waypoints/ScrapTask.cs`
    still uses 80%/90%, not the spec's 3/4/5/10/20 divisors — moderate-confidence source, left
    alone per the same reasoning.
13. **Bombing damage rounds down instead of stochastically** — **NOW-FIXED**.
    `ServerState/Bombing.cs:87,108,113,118` all call `Global.StochasticRound`.
14. **Race Designer tolerance band can go to 0 width** — **NOW-FIXED**.
    `EnvironmentToleranceViewModel.cs:34,54-59` enforces a 20-width minimum.
15. **Race Designer default band wrong (15-85 vs. 20-80)** — **NOW-FIXED**.
    `Common/DataStructures/EnvironmentTolerance.cs:43-44`.
16. **Race Designer Immune-off doesn't restore an interval** — **NOW-FIXED**.
    `EnvironmentToleranceViewModel.cs:80-94` resets to 20-80.
17. **Research forecast dimensional bug** — **NOW-FIXED**.
    `Nova.Avalonia/ViewModels/Panels/ResearchViewModel.cs:222-224` — the erroneous
    `- currentLevel[targetField]` term is gone.

## GAPS (re-verified, grouped; only items with a status change from the v4 audit are detailed —
## everything else is confirmed unchanged and not re-litigated)

- **Combat — substantially narrowed since 2026-09-11.** Target-type classifier now reads
  `BattlePlan.PrimaryTarget`/`SecondaryTarget` (`BattleEngine.cs:623-624`); tech-gain-from-battle is
  implemented (`GrantBattleTechGains`, calling `TechTrading.AttemptTechGain`); Disengage retreat
  works. Still missing, unchanged: initiative formula is still a simple sum (no PRT bonus, no 0-8
  clamp, no per-token tie-breaking randomization); no diminishing-returns cap on cloak/jam
  stacking.
- **`SplitMergeTask` — partially improved.** Probabilistic fuel-stranding with graduated messaging
  is now implemented (`Common/Waypoints/SplitMergeTask.cs` ~264-371) — new since the v4 audit.
  Incompatible-design-abort and scanner-cap logic are still not found.
- **Stargates/Wormholes — intact, not regressed** by this session's Stargate rewrite.
  `TurnGenerator.TryStargateJump` still implements the full range/mass/overgating/vanish-chance/
  cargo-restriction formula set the spec documents; `Wormhole.cs`'s previously-disclosed omissions
  (no cargo-transit side effects) are unchanged.
- **AI §9/§10, mining depletion curve (27/462/1000/2000 vs. spec's 25/10), race archetypes, galaxy
  generation's structural divergence, 6 of 7 missing boolean game-option flags, research discounts
  (Slow Tech/Miniaturization/Bleeding Edge Technology/starbase discount), tutorial/coach system,
  save-format name cap and score history, `dynamic-string-table.md`'s N/A status** — all confirmed
  **unchanged**, exactly as the v4 audit described.
- **Production queue's OTHER documented gaps (200-entry cap, 4-slot template manager, Alternate
  Reality auto-Mineral-Alchemy) remain untouched** — the new auto-build-throttle feature added this
  session is a distinct mechanic (see "New session work" above), not an implementation of any of
  these three.

## NEW GAP: map "Planets:" display-mode system (`client-interface.md`, new in v5)

This is the single largest new finding in this audit — a substantial spec section with no v4
counterpart, describing four selectable planet-icon overlay modes plus several related overlays,
all gated by one shared "view options" word (bits 0x10/0x20/0x40/0x80) with digit-key shortcuts
(1-9, 0, Shift+0). Verified against `Nova.Avalonia`'s current map rendering
(`StarMapDocumentViewModel.cs`, `StarMapStarViewModel.cs`, `StarMapDocumentView.axaml`, and
sibling `StarMap*ViewModel.cs` files):

| Spec feature | Status |
|---|---|
| Habitability "bullseye" ring (dark/bright red-green-yellow pairs, sized by value) | **Not implemented** on the map. Habitability is shown as a plain text row in the Inspector (`InspectorViewModel.cs:972`), not drawn |
| Population ring (green/yellow/red by relationship, sized by population brackets) | **Not implemented** on the map — same Inspector-text-only fallback |
| 3-segment mineral concentration/stockpile bar (blue/dark-green/yellow) | **Not implemented** anywhere found |
| Fleet-in-orbit ring (white/relationship-color/third-color) | **Implemented, simplified.** `StarMapStarViewModel.cs:29-49` draws a fixed 12px ring with the right 3-way color scheme, but as one fixed size rather than the spec's two size classes, and via a vector `Ellipse` rather than sprite compositing (visually immaterial) |
| Ship-count badge (clamped to 999, colored by owner) | **Implemented**, close match. `StarMapFleetViewModel.cs:27-36` — shows "999+" rather than clamping the number itself to 999, a cosmetic difference |
| Tracked-object diamond marker (blue/red, replacing a deep-space fleet's own icon) | **Not implemented** — there's no "tracked object" concept distinct from selection; deep-space fleets always render as a directional triangle |
| Scan-range circle (+ secondary half-radius penetrating-scan circle) | **Implemented, partially.** `StarMapScanCircleViewModel.cs` draws both, but the penetrating circle uses its own real `PenScanRange` rather than exactly half the primary radius, it's always-on (no toggle bit), and there's no overlap-culling |
| Packet-Physics Mass-Driver range overlay | **Not implemented** — no PRT check or overlay found anywhere in map code |
| Minefield visibility overlay (3 fill patterns, per-relationship 4-bit mask) | **Implemented, simplified.** Own/enemy visibility logic exists and renders filled circles, but with no per-minefield-type fill pattern and no per-relationship checklist popup |
| Route-overlap dashing | **Not implemented** — route legs always render as plain solid lines |
| Shared view-option word + digit-key shortcuts (1-9/0/Shift+0) | **Not implemented at all.** Every overlay that does exist is independent and always-on; there is no packed options word, no bit-gating, and no keyboard-shortcut wiring anywhere in `Nova.Avalonia` |

**Bottom line**: roughly half of the individual visual pieces already exist in a simplified,
always-on form (fleet-in-orbit ring, ship-count badge, scan-range wash, minefield visibility), but
the four planet-icon "Planets:" modes themselves, the tracked-object diamond, the Packet Physics
overlay, route-overlap dashing, and — most consequentially — the entire shared toggle/keyboard
infrastructure that ties them together as one coherent, player-switchable system are absent. This
reads as genuinely new scope (a real feature, not a bug), not a quick fix.

## CONFIRMED: production queue color-coding (`production-queue.md`, new in v5)

Fully implemented, including the underlying 100-year completion-time simulator the color scheme
depends on. `Common/Production/ProductionCompletionEstimator.cs`'s `EstimateAll`/`SimulateAll`
(lines 93-322) simulates start/finish turns per queue item exactly as documented (forced to 100 if
never reached), and applies the doc's exact five-bucket rule (green for (1,1), blue for
start=0-or-(1,>1), red for start≥100/unmatched, gray for the auto-build-already-satisfied case,
default otherwise) — wired into the UI via `ProductionItemViewModel.cs`'s `TextColor` and bound in
`ProductionView.axaml`. The one deliberate deviation: the exact literal RGB values (dark green
0,127,0 / dark blue 0,0,127) are swapped for `LimeGreen`/`DodgerBlue`, with an explicit code
comment explaining the literal values would be nearly invisible against this app's dark theme. Not
a gap — a disclosed, reasonable adaptation.

## CONFIRMED: no dynamic fleet interception (`fleet-movement-scanning-cargo.md`, new in v5)

The new text confirms the original game has no automatic pursuit mechanic — a waypoint targeting a
fleet captures its position once, at order time, rather than steering toward it while it moves.
Grepping `ServerState/TurnGenerator.cs` for any re-resolution of a target fleet's live position
during movement processing found none: `Waypoint.Position` is a fixed point set once when the order
is created, and movement code only ever reads that stored value. **CONFIRMED matching**, no action
needed.

## AMBIGUOUS (spec leaves a real choice open, or is simply silent)

- **Stargate "use Stargate" speed-selection mechanic.** The specs document jump *eligibility* in
  detail (both ends gated, fuel-only cargo except Interstellar Traveler, mass/range limits with
  5x overgating) but never trace the client-side order UI at all — no waypoint/speed-selector
  dialog appears in `client-ui-dialog-catalog.md` or `client-interface.md`, and the fleet-movement
  doc's own "no dynamic interception" analysis is the closest thing to UI-adjacent coverage this
  pass has. This session's redesign (an explicit warp-11 "use Stargate" order, defaulting when both
  ends are gated, falling back to ordinary warp otherwise) was built from your own direct
  description of the original game rather than from any decompiled source, and doesn't contradict
  anything the specs actually document — it answers a question they never asked. Worth folding back
  into `fleet-movement-scanning-cargo.md` as a documented (if differently-sourced) mechanic in a
  future spec revision, rather than leaving it as an undocumented implementation-only detail.
- **Auto-build population-throttle pacing rate.** As above — the "grow into your infrastructure"
  mechanic itself is spec-confirmed and unchanged; exactly how fast an auto-build order should race
  toward a target beyond today's operable cap isn't quantified anywhere in the source material.
  This session's choice (pace to the current cap each turn, never rushing ahead of population
  growth) is a reasonable reading consistent with the mechanic's own name, not a spec-derived
  number.
- Everything the v4 audit already listed as ambiguous (population-growth crowding-factor formula
  vs. an alternate ceiling-clamp reading; the no-trait Defense/Terraform cost branch; whether the
  63%-cap stacking formula applies to cloak or jamming; fuel-per-LY's two candidate formulas; AI's
  stateless-per-turn architecture vs. §9/§10's cross-turn memory needs) remains open and unchanged.

## Documentation hygiene (not a code issue — flagging for the docs themselves)

- **`race-traits.md`: v5 is stale relative to v4 in one section.** v4 received a later edit (dated
  2026-09-17, after the v4 audit) resolving the "PRT-specific starting fleet" gap as **RESOLVED** —
  confirming `ServerState/NewGame/StarMapInitialiser.cs` correctly grants each Primary Racial
  Trait's documented starting-ship bonus (verified directly: War Monger/Hyper Expansion's armed
  scout, Packet Physics' two shielded scouts, Space Demolition's two mine layers, Interstellar
  Traveler's destroyer+privateer, Jack Of All Trades' four extra ships, etc. — all present in
  current code, matching v4's *later* text). v5's copy of this same section still carries the
  *older*, pre-fix "effectively unimplemented" wording. **The code is correct; v5's spec text is
  simply out of date** and should be synced from v4's later revision.
- **v4 audit's own numbering:** its "Fix status" section refers to "18 contradictions" and cites an
  item "18," but the actual numbered CONTRADICTIONS list only runs 1-17. Pre-existing, harmless,
  but worth fixing next time that document is touched so citations stay unambiguous.

## Recommended next step

Nothing here rises to "drop everything" — no regressions were found anywhere, and the single
biggest surprise (the map-overlay system) is new scope rather than a broken promise. In priority
order if you want to act on this:

1. Sync `race-traits.md`'s stale section from `behavior-specs-4`'s later revision (a two-minute
   documentation fix, zero code risk).
2. Decide whether the map-overlay system (habitability/population rings, mineral bars, the shared
   view-option toggle system) is worth building — it's the largest real gap found, but also the
   largest single chunk of new work; the existing simplified always-on overlays (fleet-in-orbit
   ring, ship-count badge, scan circles, minefields) already cover a meaningful fraction of the
   same visual real estate.
3. Fold the Stargate speed-selection mechanic back into `fleet-movement-scanning-cargo.md` as
   documented, differently-sourced behavior, so future audits don't re-flag it as unsourced.
4. Everything else is a rerun of the v4 audit's own unchanged backlog (defense/terraform costs,
   scrap recovery rates, mining depletion curve, AI §10, tutorial system, etc.) — no new urgency
   beyond what that document already recommended.
