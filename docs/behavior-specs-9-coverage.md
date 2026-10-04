# Behavior-Specs-9 Coverage — full game functionality audit

Audit date: 2026-10-01. Covers `docs/behavior-specs-9/` (the 18 spec files plus `component-stats.tsv`). `behavior-specs-8-coverage.md`
and the earlier reports are kept alongside for history.

**What spec-9 is.** A whitespace-insensitive diff against `behavior-specs-8/` shows content changes in every file but
three (and large *line* counts partly caused by rewording: the clean-room framing was changed, hex globals and pseudo-code
became prose, quoted game strings were paraphrased). Underneath that, it is the most *corrective* revision so far — several mechanics
the earlier specs described (and this project built) are now **retracted or reversed** after a further trace of the original:

| Spec file | Changed lines (diff -w) | Substance |
|---|---|---|
| race-traits.md | 551 | Lesser-trait bit numbering fixed (**no lesser trait is Inner Strength**; bit 9 is OBRM); AR capacity table x100; Ultimate Recycling rewritten; exact IS breeding / AR transit loss; BET retraction; AR auto-alchemy retracted |
| research-tech-tree.md | 487 | Full 239-record prerequisite table verified from the exe; availability is a plain per-field minimum; gift/trait gates run first; data corrections (Gravity ±11 missing) |
| population-growth.md | 463 | **+10% capacity bonus is OBRM, not Inner Strength**; AR table x100 and **0 with no starbase**; exact integer habitability and growth carry; dead band 1,000 colonists |
| ai-opponent-behavior.md | 443 | **Colonisation score/roll/"too eager" and the freighter router retracted and replaced**; new §12 (per-personality driver detail, hubs, bomber advisor, end-of-pass top-up) |
| production-queue.md | 408 | AR auto-alchemy retracted; UR blend exact; Genesis re-roll settled; messages 62/63/123/140/185/205-207/297/298/303; terraform headroom/step-chooser; mineral-packet arrival |
| turn-generation-engine.md | 398 | 40-step order unchanged; **all four yearly random events fully specified (§5a)** incl. the Mystery Trader; repair/IS-growth/AR-loss exact; `FUN_10f0_6ea2` is orbital bombardment |
| ship-design-and-components.md | 364 | Gate table by tsv idx; **cloak points per named component**; RS armor halving limited to Armor parts; design-reveal mask |
| fleet-movement-scanning-cargo.md | 274 | **Scrap divisors retracted** (exact rates); Transport task traced; overgating code-confirmed; minefield type table; colonize dismantle; **Mystery Trader re-attribution** |
| combat-resolution.md | 192 | Exact beam/missile/gatling/overflow rules; capacitors, sappers, regenerating shields; cloak-point name table |
| dynamic-string-table.md / client-ui-dialog-catalog.md / client-interface.md | 159 / 135 / 66 | No substance / per-type message filter + exact **score formula** / tracked-object key and chevron |
| new-game-setup.md | 126 | Consolidated per-PRT starting fleet, template loadouts and the starting tech-upgrade pass |
| save-turn-file-format.md / race-designer / victory-conditions / tutorial / diplomacy / .tsv | 40 / 24 / 21 / 20 / 14 / 6 | Binary-format detail / bit numbering / score and elimination / out of scope / meeting mechanic retracted / ordinal note |

**Method.** Eight analysis agents each read the full diff for their files, the surrounding spec text and the *current code*, and returned
(a) substantive changes, (b) row-level coverage impact verified against the code, (c) what is implementable now and (d) spec gaps.
Those reports were merged into the v8 report row by row; rows the code now contradicts are marked, with the file and symbol.
Nothing was implemented in this pass — you asked to see the spec gaps first.

**Scope decisions per your direction:**
- `tutorial-system.md` is **excluded** — a learn-to-play coach/tutor system, out of scope for this
  project. (For the record: it audited at 1/17 implemented, 0 tested — just a dead WinForms stub
  button — but no further action is expected here.)
- `client-interface.md` and `client-ui-dialog-catalog.md` (the two UI specs) are included in full
  below, but see the dedicated **"UI differences for your review"** section — a lot of what those
  two files flag as "gaps" reflects this port's deliberate, already-agreed move to a touch/mobile-
  adapted dockable-panel UI rather than a literal recreation of 1990s modal dialogs. That section
  separates what's a real informational/behavioral gap (like the map overlay rings you called out)
  from what's just a different, equally-valid UI shape, for you to triage.

---

## Headline numbers

| Spec file | Implemented (full+partial) / applicable | Tested (full+partial) / applicable |
|---|---|---|
| ai-opponent-behavior.md | 11/50 | 11/50 |
| client-interface.md | 34/80 | 0/80 |
| client-ui-dialog-catalog.md | 26/46 | 4/46 |
| combat-resolution.md | 36/39 | 24/39 |
| diplomacy-relations.md | 9/11 | 4/11 |
| dynamic-string-table.md | 6/24 | 3/24 |
| fleet-movement-scanning-cargo.md | 31/58 | 22/61 |
| new-game-setup.md | 15/43 | 13/43 |
| population-growth.md | 26/29 | 23/29 |
| production-queue.md | 27/38 | 20/35 |
| race-designer-ui-and-availability.md | 22/31 | 4/31 |
| race-traits.md | 33/42 | 33/42 |
| research-tech-tree.md | 23/35 | 8/35 |
| save-turn-file-format.md | 3/16 | 3/16 |
| ship-design-and-components.md (+ .tsv) | 31/41 | 10/41 |
| turn-generation-engine.md | 34/51 | 30/51 |
| victory-conditions.md | 17/19 | 6/19 |
| ~~tutorial-system.md~~ | *excluded* | *excluded* |
| **Total** | **384/653 (about 59%)** | **218/653 (about 33%)** |

Straight re-sum of the per-file summary lines above. Immediately after the spec-9 audit these stood at 343/653 implemented and
169/651 tested (down from v8's 346/597 and 175/594, because spec-9 retracted mechanics the code had implemented and about 56 rows of new
spec content were added). The implementation pass that followed (see "Where spec-9 overturns earlier work") lifted them to the figures
above. Row granularity still varies by file, so treat the percentage as directional.

## Where spec-9 overturns earlier work

These are places where a prior pass of this project (usually "FIXED" in an earlier report) is now **contradicted by the spec**, or a
pre-existing bug the audit surfaced. They are the natural first implementation targets, roughly ordered by blast radius.

| # | Item | What the code does now | What spec-9 says | Rows |
|---|---|---|---|---|
| 1 | **Beam deflectors do nothing in combat** (pre-existing bug) | `components.xml` property type `"Beam Deflector"` is dropped by `SumProperty`; `BeamDeflectors` reads `"Deflector"` -> always 0 | 0.9 per deflector, truncated on a 1000 scale (90 / 81 / 72) | combat 16 |
| 2 | **AR homeworld shrinks every turn** | `AlternateRealityCapacity` is 100x too low (10,000 on a Space Station) and 2,500 with no starbase | 250,000 ... 3,000,000 colonists; 0 with no starbase; a founded colony gets the "Starter Colony" starbase | pop 12, race 18 |
| 3 | **+10% population bonus on the wrong trait** | gated on Inner Strength (the spec-8 "fix"), test asserts it | **OBRM**; Inner Strength has no population bonus | pop 11, race 31 |
| 4 | **Ultimate Recycling resources** | tested on the fleet owner, 70% (0% without a starbase), credited next turn, persisted | planet owner's trait, 100%, blended into the *same* turn r + d·r/(d+r), remainder lost | fleet 44/45, race 26, prod 30 |
| 5 | **Trait gates skipped on tech-up** (pre-existing bug) | `StarUpdateStep.TechLevelUp` adds crossed components without checking restrictions | gates run first; never replaced by the tech check | research 32 |
| 6 | **`<NRSE>` tags silently dropped** (pre-existing bug) | trait key is `"NRS"`; all seven restrictions ignored | NRSE gates ram scoops / Interspace-10 | research 33, race 29 |
| 7 | **Overgating destroys ships almost always** | A=68 mass curve + two rolls, Interstellar Traveler x0.5 | one roll floor(damage/3)%, IT never vanishes | fleet 37, race 17 |
| 8 | **Scores are wrong** (pre-existing) | counts every token as an Escort, tech scores 0 (`AllTechLevels` never populated), leftover not output resources | exact nine-row formula | victory 16, client-ui 28 |
| 9 | **Colonization / freighter AI** | scored selector with "too eager" roll; 180-ly freighter router | nearest unowned untargeted planet; the 180 ly rule belongs to the warship hunter | ai 4-7, 15-17 |
| 10 | **Regenerating Shields armor** | halves all armor incl. hull; regenerates zero-shield tokens; test locks it in | Armor-category parts only; zero-shield tokens never regenerate | combat 35-39, race 35, ship 39 |
| 11 | **Over-capacity dead band** | 10 colonists | 1,000 colonists (10 units) | pop 16 |
| 12 | **Mining above concentration 100** | raw value in depletion | depletion clamps >= 101 to 100 | pop 23 |
| 13 | **AR planets mine nothing** | 0 operable mines | innate mining, sqrt(pop/100) effective mines | race 19, turn 54 |
| 14 | Battle can grant Mini Morph / Genesis Device | uniform draw over all 12 | only the (unbuilt) Mystery Trader reaches bits 8/10/12 | ship 8, turn 29 |
| 15 | Fuel/repair/bombardment/invasion step order | repair inside movement loop; bombing after colonise; invasion on arrival | steps 22/25, 23b before 23f | turn 1, 21 |
| 16 | Retracted-spec tests | `AiTargetSelectionTest`, `PopulationCapacityTest`, `AlternateRealityCapacityTest`, `UltimateRecyclingDeferredResourcesTest`, `RegeneratingShieldsTest`, `ColonizationTieBreakTest` assert superseded behaviour | - | - |

**Implementation status (this pass).** Done and tested (all 659 tests green; each new test verified to fail against the old behaviour): items 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12 and 13 above, plus the colonisation rewrite, the Starter Colony, Inner Strength breeding, Alternate Reality transit loss, the random events (comet, environment shift, mineral deposit) and the "No Random Events" setting. **Still open from this list:** #9 (the AI colonisation/freighter rewrite - large, and its tests encode the retracted behaviour), #14 (Mini Morph / Genesis Device grants - waiting on the Mystery Trader decision), #15 (repair/bombardment/invasion step order - repair still runs inside the movement loop), and the retracted-spec tests for the AI selector.

**Confirmed unchanged by spec-9** (worth stating because they were recent): Defenses 15 + 5/5/5 and Terraform 100/70/CA-halved, per-turn "up to N",
the operable/build/defense caps, Genesis Device (now including the shared environment draw), auto Alchemy 1,000, the battle-after-movement order,
the 1/2/3/5/8/20% repair table, the passive fuel pass, minefield decay, Accelerated BBS population.

---

## Top standout findings (cross-cutting, ranked by how concrete/actionable they are)

1. ~~**Stargates and Mass Drivers have a systematic 2x cost error.**~~ **FIXED.** Confirmed
   exhaustively across all 16 Stargate/Mass-Driver entries *and* all 5 Starbase Chassis entries
   (a second category beyond what was originally sampled) — all had costs exactly half the raw
   binary-extracted values, apparently built from an already-halved "community figure" source
   rather than the real data. All 21 entries' costs have been doubled in `components.xml`, and
   Mass Driver 7 (misnamed "Super Drvier 7" with a wrong tech level) was corrected too.
   *(ship-design-and-components.md)*
2. ~~**Population capacity for growth/crowding isn't scaled by habitability.**~~ **FIXED** —
   `Star.Capacity` now multiplies `race.MaxPopulation` by `habValue` before dividing, so a
   less-habitable world correctly supports fewer colonists. While in the same area: Inner
   Strength's undocumented +10% population-capacity bonus was also found to be wired to the wrong
   trait (OBRM) and has been moved to Inner Strength — see finding below and row 11.
   *(population-growth.md)*
3. ~~**The "population plateaus at capacity" behavior is now directly contradicted by the spec**~~
   **FIXED.** `CalculateGrowth` now implements the spec's traced decline formula past capacity+10
   (ramping smoothly down to a hard-clamped -12%/turn at ~4x capacity); the two tests that asserted
   the old "plateau" behavior were updated to the new decline values, verified by hand-deriving
   the expected numbers from the formula and confirming the tests pass. *(population-growth.md)*
4. **[SPEC-9: REVERSED - see "Where spec-9 overturns earlier work" #4; the same-turn blend is the real rule]** ~~**Ultimate Recycling's scrap-fleet resource bonus is credited immediately, not deferred to next
   turn's production as documented**~~ **FIXED.** The bug was actually worse than "immediate": since
   `ScrapFleetStep` runs before `StarUpdateStep` every turn, and `Star.UpdateResources()`
   unconditionally overwrites `ResourcesOnHand.Energy` from `GetResourceRate()` every time it runs
   (twice per star per turn), crediting the resources share directly into `ResourcesOnHand.Energy`
   got it silently destroyed before it was ever spent, not merely made available a turn early. Added
   `Star.DeferredScrapResources`, credited into `ResourcesOnHand.Energy` only at the very end of
   `StarUpdateStep`'s per-star pass — after this turn's own production spend — so it correctly lands
   in the balance carried into next turn's budget instead. Minerals are unaffected (already credited
   immediately via `+=`, as documented). `Tests/UnitTests/UltimateRecyclingDeferredResourcesTest.cs`.
   *(race-traits.md / fleet-movement-scanning-cargo.md)*
5. ~~**Defense and Terraform base costs are stale.**~~ **FIXED, then found wrong by spec-8, then
   fixed again.** v7's "Defense 44/25/48, Terraform 110/70/120" were **Mineral Packet kilotonnages** read
   off the wrong case group of the cost calculator. Spec-8 settles the real items: Defenses cost the SDI
   record (**15 resources + 5/5/5 kT**, Inner Strength ×3/5 by integer arithmetic), Terraforming **100
   (70 with Total Terraforming, halved for Claim Adjuster)**. The code is back to (effectively) the original
   community figures, now with the minerals Defenses really need. *(production-queue.md rows 8/10)*
6. ~~**Non-ramscoop engines generate zero fuel at any warp**~~ **FIXED.** Every engine now generates
   ≥1mg at warp 1, matching the spec — a stranded fleet with an ordinary engine can self-rescue
   again. *(fleet-movement-scanning-cargo.md)*
7. ~~**Victory check runs before the turn/year counter increment, not after**~~ **FIXED** (evaluation
   now runs after `TurnYear++`) — ~~and the TotalScore victory-condition slider's Maximum (10000) is
   below its own shipped default (11000)~~ **FIXED** (Maximum raised to 20000). Victory conditions
   now have their first test coverage (`VictoryCheckTest.cs`), though most of the file's other rows
   remain untested. *(victory-conditions.md)*
8. ~~**Order-file parsing discards *all* commands for an empire on one bad record**~~ **FIXED** — a
   malformed order now only costs that one order, per the spec's per-record fault isolation.
   *(save-turn-file-format.md)*
9. **[SPEC-9: MOOT - the 180-ly rule belongs to the warship hunter, not the freighter router]** ~~**AI freighter routing's reachability cutoff is off by a factor of 180**~~ **FIXED** — now
   compares against 180²=32,400, matching the spec's intended ~180 ly range. *(ai-opponent-behavior.md)*
10. ~~**A combat targeting formula (`GetAttractiveness`'s beam range-falloff term) is now positively
    confirmed by the spec to be *wrong***~~ **FIXED.** The term has been removed — a beam weapon's
    attractiveness score now depends only on cost/armor/shields/deflectors, matching the spec's
    §4 conclusion (reached via four independent static-analysis techniques) that no range term
    exists in the real targeting formula. *(combat-resolution.md)*
11. ~~**Cloaking is a clean, fully-specified gap**~~ **FIXED.** Replaced the old flat hardcoded 20%
    with `CloakCalculator`'s decompiled piecewise raw-units-to-percent curve, Super Stealth's flat
    +300 raw-unit baseline (300 raw lands exactly on the curve's 75% breakpoint, matching the
    community-known "SS races carry an inherent 75% cloak"), a back-solved +40 baseline for Improved
    Starbases (disclosed as not decompiled the way SS's figure is), `Fleet.RecalculateCloak`'s
    mass-weighted fleet-level combination, and the 18-entry Tachyon Detector counter-cloak table
    applied in `ScanStep`. *(combat-resolution.md)*
12. **Research has almost no dedicated test coverage** (now 5/31, up from 2 — still mostly gaps)
    despite 19/31 behaviors being implemented — tech trading and the Generalized Research split
    still have real code with zero direct tests, though the cost curve now does. ~~The "turns to
    next level" forecast doesn't account for Generalized Research halving the rate~~ **FIXED** —
    see row 29, `Research.TargetFieldContributionFraction`. ~~Bleeding Edge Technology and
    Miniaturization remain literal empty `// TODO` stubs~~ **FIXED** — see rows 11/12,
    `ShipDesign.ApplyMiniaturizationAndBleedingEdge`. *(research-tech-tree.md)*
13. ~~**`race-traits.md` (the largest file) shows most PRT/LRT *secondary* effects still
    unimplemented**~~ **Mostly fixed.** War Monger's combat bonuses, Claim Adjuster's free
    terraforming, and Regenerating Shields are now all implemented (see rows 6/7/35 and finding
    11's cloak fix for Super Stealth/Improved Starbases). Still open: Super Stealth's separate
    minefield-safety/passive-research-sharing pair (its own cloak effect is fixed).
14. ~~**The auto-build population-throttle … still not addressed by any spec revision.**~~ **RESOLVED
    by spec-8, and it exposed a real bug.** The clamp is `min(N, operable(next year's projected population)
    − built)` (production-queue §10a) — the throttle built earlier was essentially right. But the spec's
    live test (§10h) shows **"up to N" is a per-turn maximum, not a standing total**: "Up to 12" with 10
    already built keeps buying every year. `ProductionOrder.Process` treated N as a target and went idle
    once N were built; that is fixed and tested. Auto Mineral Alchemy is also now a persistent "buy as many
    as possible (1,000)" order. *(production-queue.md rows 5/12/22)*
15. **Two new mechanics from spec-7 — resolved differently by spec-8.** (a) ~~"Planet Rebirth"
    disaster~~ **IMPLEMENTED — it is not a disaster at all**: it is the build-completion effect of the
    **Genesis Device**, an ordinary 5,000-resource manual production item; `GenesisDeviceProductionUnit`
    + `Star.ApplyGenesisDevice` + a broadcast message, fully tested (production-queue row 18). (b) **[SPEC-9: RETRACTED - AR has no automatic Mineral Alchemy]** ~~AR
    automatic Mineral Alchemy: still not implementable.~~ The spec did *not* resolve it — turn-generation
    §6 now records a whole-routine search that found no such function and suggests it may be a client-side
    display estimate. See the ambiguity list (item 1). *(production-queue.md rows 13/18)*
16. **The turn order was wrong in the code, because the old spec list was wrong.** Spec-8 rebuilds the
    master routine as 40 steps and states that combat is step 23 — **after** fleet movement and the
    production hub — and that the old "combat at phase 9" list was a coarse misreading. This project had
    moved combat *before* movement on the strength of that list (and earlier audits reported it "confirmed
    fixed, well-tested"). It now runs after movement and production, colonisation resolves after the
    battle, and the year counter / victory check / scan step come last. `TurnOrderTest` (verified to fail
    against the old order). *(turn-generation-engine.md rows 1-3)*
17. **Operable/build limits were mis-specified and Alternate Reality was unrestricted.** The operable
    factory/mine count multiplies before flooring (25,000 colonists operate 25 factories, not 20), is
    floored at 1, and is clamped by a separate **build cap** set by *maximum* population; Defenses have
    their own caps (4 × habitability %, 10..100; one operable per 2,500 colonists); and Alternate Reality
    can build/operate none of them. All implemented (`BuildingCapsTest`). *(production-queue.md rows 3/4/9)*
18. **Alternate Reality population capacity** now follows the spec-8 starbase-chassis table (2,500 /
    5,000 / 10,000 / 20,000 / 30,000) — with one disclosed deviation for planets with no starbase (spec
    says 0; see ambiguity list item 2). AR landings on owned planets now destroy the colonists.
    *(population-growth.md row 12, turn-generation row 31)*
19. **Smaller spec-8 corrections implemented:** minefield decay is now a yearly `2 + 4×S`% pass instead of
    a 1% decay applied once per moving fleet while mutating the collection it iterated; the fuel pass
    (+50 mg per Anti-matter Generator, +200 mg per Fuel Transport ship, *not* ramscoops); the repair-rate
    selection (moved-this-year rule, Inner Strength ×2, fuel-transport bonuses, flat starbase rate);
    Accelerated BBS starting population scaled by growth rate; the 25,000-raw-unit no-cloak guard.

---

## UI differences for your review

You asked to separate genuine relevance from grey-area cosmetic/architectural differences. Pulling
the two UI-heavy files' rows into one triage table, sorted with clearest-relevance-first:

| Behavior | Source | My read | Why | Notes |
|---|---|---|---|---|
| Habitability "bullseye" ring overlay on planets | client-interface.md | **Relevant** | Conveys real game state (habitability) visually — same category as the map dots/rings you already prioritized once |  |
| Population ring overlay (green/yellow/red by relationship) | client-interface.md | **Relevant** | Same — real information, currently only in text form in the Inspector, not on the map |  |
| 3-segment mineral concentration/stockpile bar overlay | client-interface.md | **Relevant** | Same — real information not surfaced on the map at all |  |
| Fleet-in-orbit ring (already ✅ implemented, simplified) | client-interface.md | **Relevant, mostly done** | Already implemented in simplified single-size form — the remaining gap (two size classes tied to "tracked object") is minor polish |  |
| Ship-count badge (already ✅ implemented) | client-interface.md | **Relevant, done** | Already implemented; "999+" vs. hard-clamped "999" is cosmetic |  |
| Scan-range circle + secondary penetrating-scan circle (already ✅/⚠️ implemented) | client-interface.md | **Relevant, mostly done** | Real information, already on the map; the secondary circle's exact radius (should be half primary) is a minor formula fix |  |
| Minefield visibility overlay (already ⚠️ implemented, simplified) | client-interface.md | **Relevant, mostly done** | Real information, already on the map in simplified form; missing per-type fill patterns and a per-relationship checklist popup |  |
| Packet-Physics Mass-Driver range overlay | client-interface.md | **Relevant if PP is used** | Real information, but only matters for one PRT; low traffic | Let's do it |
| Route-overlap dashing (overlapping/reversed waypoint legs) | client-interface.md | Judgment call | A visual clarity nicety for a route editor, not new information — the route itself is already fully visible | Let's do it |
| Tracked-object diamond marker replacing a deep-space fleet icon | client-interface.md | Judgment call | A "which fleet is selected" affordance — this port already has a selection-highlight mechanism, just shaped differently | Let's do it |
| Shared view-option word + digit-key (1-9/0/Shift+0) shortcuts | client-interface.md | **Grey area / architectural** | This is the original's *mechanism* for toggling the above overlays, not a mechanic in its own right — a touch UI would reasonably use checkboxes/menu toggles instead of keyboard shortcuts | Skip Android, include for Windowed UI |
| Object-information panel 6-way content mode (digit keys 1-6) | client-ui-dialog-catalog.md | **Grey area / architectural** | Same keyboard-shortcut-driven mode-switching pattern; this port already shows per-object-type content via tabs instead | Skip Android, include for Windowed UI |
| Toolbar icon actions, 9-slot Recent Files, map printing, autosave, sound toggles, 3-way window-size preset | client-interface.md | **Grey area / architectural** | All 1990s-desktop-app conventions (toolbar, MRU list, printing, autosave, sound) — none of these carry game-state information; a mobile/dockable-panel app reasonably omits or replaces all of them | Good for Android, but let's include for Windowed UI |
| Startup title screen as one shared window vs. separate top-level screens | client-interface.md | Grey area / architectural | Same 4 actions (New/Open/Continue/Exit), different screen structure | Good for Android, but let's include for Windowed UI |
| Right-click object-disambiguation popup vs. repeated-tap cycling | client-interface.md | Grey area / architectural | Already has a different, deliberate, documented mechanism (tap-cycling) for the same problem — a reasonable touch-first substitute for a desktop right-click menu | Good for Android, but let's include for Windowed UI |
| Course-plotting via Shift/middle-drag rubber-band + live tooltip vs. tap-based waypoint arming | client-interface.md | Grey area / architectural | Already has a working, deliberate touch-first alternative | Good for Android, but let's include for Windowed UI |
| Rich map hover-tooltips (13 content variants) vs. plain static tooltips | client-ui-dialog-catalog.md | Grey area / architectural | Real underlying data mostly already exists and is shown elsewhere (Inspector panel); this is a "how much detail on hover" polish question | Let's review all the data and ensure it's all visible elsewhere |
| Component-category Prev/Next paged browser vs. one scrollable list | client-ui-dialog-catalog.md | Grey area / architectural | Explicitly a deliberate redesign already (scrollable list instead of paging) — reasonable for touch | Good for Android, but let's include for Windowed UI |
| Ship Designer: Copy command, 6-category filters, delete-in-use confirmation | client-ui-dialog-catalog.md | **Mixed** | Filters/Copy are architectural (nice-to-have workflow, not data); but the **missing delete-in-use confirmation is a real regression** vs. the WinForms original, which does warn before destroying in-use designs — worth fixing regardless of UI shape |  |
| Saved production-template manager (4 named slots) | client-ui-dialog-catalog.md | **Mixed** | The underlying capability (save/restore a queue configuration) has real workflow value independent of UI shape; how it's exposed (dialog vs. panel) is architectural |  |
| Messages viewer: categories, Next/Prev navigation, read/unread filter | client-ui-dialog-catalog.md | Judgment call | Real usability question at scale (many messages), but not information currently missing — everything's still in the flat list | Ok as is currently |
| Reports grid: toggleable/sortable columns, Fleets idle/ETA column | client-ui-dialog-catalog.md | **Mixed** | Idle-status/ETA is real information not currently surfaced anywhere; column sort/toggle is pure UI polish | Need something suitable for Android which appropriately exposes all the data. Go for similar for Windowed UI |
| Report TSV/plain-text export | client-ui-dialog-catalog.md | Grey area / architectural | A power-user workflow feature, not game information | Agreed, skip |
| Score display: 3 cyclable modes (medal grid / leader table / history graph) vs. one table | client-ui-dialog-catalog.md | **Mixed** | A score *history* view is real information not currently available (only current-turn snapshot exists, and that's also flagged separately in save-turn-file-format.md); the medal-grid/leader-table presentation styles are cosmetic | Let's implement something appropriate for Android, and similar to original game for Windowed UI |
| Battle VCR: 10×10 board + animation vs. plain stepped text log | client-ui-dialog-catalog.md | Grey area / architectural | Already a deliberate, disclosed simplification; the WinForms version has the richer board if ever wanted as reference | Good, keep |
| Race Wizard: 8 preset "style" archetypes + Random-race generator | client-ui-dialog-catalog.md / race-designer-ui-and-availability.md | **Mixed** | The presets/random-generator are a real missing *feature* (faster race creation), not just a layout difference — but not urgent since building a race from scratch already fully works | Agreed |
| Race Designer: working-copy/cancel semantics (edits apply immediately, Cancel doesn't revert) | race-designer-ui-and-availability.md | **Relevant** | This is a real behavioral gap, not cosmetic — Cancel silently not working is a correctness issue regardless of dialog shape | Fine |
| Battle Plans: no opponent-selection UI for the 5th "specific target" Attack category | client-ui-dialog-catalog.md / race-designer-ui-and-availability.md | **Relevant** | The targeting backend fully supports it and is well-tested; there's just no control to pick a specific opponent — a real missing capability, not a shape difference | Let's implement something appropriate for Android, and similar to original game for Windowed UI |
| Global modal accept/cancel/commit contract, shutdown confirmation, title/status-bar race-name builder | client-interface.md | Grey area / architectural | Desktop-app conventions with no gameplay information content | Fine as is. Let's implement similar to original game for Windowed UI |
| Technology Browser dialog, Host Mode dialog (Generate Now/Auto/countdown), progress indicator | client-ui-dialog-catalog.md | Grey area / architectural | All desktop-app/batch-processing conventions; turn generation already works, just without a dedicated control surface | Let's implement something appropriate for Android, and similar to original game for Windowed UI |
| Find Planet/Fleet search dialog | client-ui-dialog-catalog.md | Judgment call | Real usability value at scale (many planets/fleets), architectural in shape | Let's implement something appropriate for Android, and similar to original game for Windowed UI |
| Change-password / local access-secret dialog | client-ui-dialog-catalog.md | Grey area / architectural | Single-player-focused port; original's local-security model may not even apply | Agreed, skip for now |
| Map printing | client-interface.md / client-ui-dialog-catalog.md | Grey area / architectural | A physical-paper-era feature with no digital equivalent need | Agreed, skip |

---

## Full per-file tables

### ai-opponent-behavior.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Personality dispatch: 0-7 code selects driver, shared decision skeleton | ⚠️ | ✅ | `Nova.Ai/DefaultAi.cs:51-68,116-181`. Only one shared code path exists (not 6 distinct driver bodies). `Tests/UnitTests/DefaultAiPersonalityTest.cs`. **Spec-9:** the record word at offset 2 is computer flag (bit 9), **skill** 0-3 (bits 10-12) and **category** 0-7 (bits 13-15); category **6 is the no-op, 7 is economy-only** (research + end-of-pass top-up + bomber defence, no fleet orders). **CONTRADICTS the code**: `DefaultAi.cs:51,58` treats 0 as disabled and 1 as passive; one shared code path; the `-n` flag is never passed (`NewGameViewModel.cs:28-30`). |  |
| 2 | Named AI personality/difficulty exposed in setup UI vs abstract difficulty | ❌ | ❌ | UI/dialog-string forensics belongs to new-game-setup.md; no independent AI-behavior claim. **Spec-9:** now applicable: setup offers 6 archetypes (Robotoids, Turindrones, Automitons, Rototills, Cybertrons, Macinti) x 4 skill tiers (Easy/Standard/Tough/Expert) or Random (uniform 0-5 / 0-3); each pick copies one of 24 built-in AI race templates (contents not given). No such picker exists. |  |
| 3 | "Nearest match"/"best match" generic search primitives | ✅ | ✅ | `Nova.Ai/ColonizationTargetSelector.cs:103-120,230-266`. `Tests/UnitTests/AiTargetSelectionTest.cs`. **Spec-9:** no longer what colonization uses (spec-9 §2 retracts the scored search); the primitive itself is unaffected. |  |
| 4 | Colonization-target scoring (habitability + distance bands) | ❌ | ❌ | `ColonizationTargetSelector.cs:97-161` (self-documented reconstruction, not bit-verified). **Spec-9:** **RETRACTED.** There is no habitability+distance score: the target is simply the *nearest unowned planet not already targeted by another own colony fleet*, with a non-negative terraformed-habitability filter for every category except 0 and 5. `ColonizationTargetSelector.cs:97-161` implements the retracted scoring and `AiTargetSelectionTest.cs` encodes it. |  |
| 5 | Probabilistic acceptance (roll under score) | ❌ | ❌ | `ColonizationTargetSelector.cs:163-170`. **Spec-9:** **RETRACTED** - no roll-under-score acceptance exists (`ColonizationTargetSelector.cs:163-170`). |  |
| 6 | "Too eager" flag — extra flat 25% rejection | ❌ | ❌ | `ColonizationTargetSelector.cs:172-178` (`TooEagerScoreThreshold=80`). **Spec-9:** **RETRACTED** - no "too eager" 25% rejection exists (`TooEagerScoreThreshold`). |  |
| 7 | Late-game distance-conservative colonization (turn 59+) | ❌ | ❌ | `ColonizationTargetSelector.cs:197-224`. No test exercises `currentTurn > 59`. **Spec-9:** **CONTRADICTS**: the isolation check gates colony-ship *production* (not target choice), runs once the turn counter exceeds 59, and measures from the producing planet to the nearest planet the AI does *not* own (refuse beyond 350 ly; independent 50% rolls beyond 300 and 250). `PassesLateGameDistanceCheck` filters targets by distance to the nearest *owned* colony. Merges with row 25. |  |
| 8 | Random exploratory colonization (reservoir sampling, expanding radius) | ⚠️ | ✅ | `ColonizationTargetSelector.cs:230-266`. **Spec-9:** unclear: §12 personality 0 *scraps* a targetless colony ship; `SelectExploratoryTarget` has no traced basis. |  |
| 9 | Starbase mineral transfer (700 threshold, 3-way distance-weighted split) | ⚠️ | ⚠️ | `Nova.Ai/FreighterRoutingSelector.cs:63-64` — not starbase-specific, picks one mineral only, no 3-way split. |  |
| 10 | Starbase build decisions: Stargate/orbital build, colonist redirect, scrap/consolidate | ❌ | ❌ | Not found anywhere in `Nova.Ai/`. |  |
| 11 | Combat-readiness gate (distance>14, cargo>499, weapon-slots>15) | ⚠️ | ✅ | `Nova.Ai/CombatReadinessAdvisor.cs:58-81` — implemented but **not wired to any real fleet action** (no invasion/attack targeting uses it). `Tests/UnitTests/CombatReadinessAdvisorTest.cs`. |  |
| 12 | Base threat rating (4-6 + jitter) | ✅ | ✅ | `Nova.Ai/ThreatAssessment.cs:63-66`. `Tests/UnitTests/ThreatAssessmentTest.cs`. |  |
| 13 | Aggregate threat from nearby enemy fleets | ✅ | ✅ | `ThreatAssessment.cs:73-99`. |  |
| 14 | Defense-need evaluator (threshold formula, minefield cap, coin-flip, passive/turn-30 skip) | ✅ | ✅ | `ThreatAssessment.cs:106-120`. **Fixed**: `DefaultAi.DoMove` (`DefaultAi.cs:178-191`) now also skips `HandleDefense()` once `gameTime > 30`, alongside the existing personality-based skip - no separate "aggressiveness flag" concept exists elsewhere to check independently, so this treats "past turn 30" as the gate itself, matching the spec's own imprecise ("roughly") framing. Not independently unit-tested: `DefaultAi.DoMove`'s branching isn't unit-tested anywhere in this codebase (per `DefaultAiPersonalityTest.cs`'s own comment) since it needs a fully-initialized game save; verified by inspection instead. |  |
| 15 | Freighter routing (nearest reachable shortfall, double-assign avoidance, fallback) | ❌ | ❌ | `Nova.Ai/FreighterRoutingSelector.cs:95-176`. **Spec-9:** **RETRACTED/RE-HOMED**: the 180-ly cutoff, cargo sum, double-assign skip and random fallback belong to the warship enemy-fleet hunter `FUN_1090_1438`; the real freighter router is `FUN_1090_19c8` (score = value / ceil(trunc(d)/25), hub scarcity levels, salvage pickup, orders at warp 4, hubs from turn 20). `FreighterRoutingSelector` (700 threshold, claimed-target skip) matches neither. |  |
| 16 | **180 ly² reachability cutoff — BUG** | ❌ | ❌ | **Fixed**: `FreighterRoutingSelector.cs:75` now uses `ReachableCutoffSquared = 180.0 * 180.0` (32,400), matching the spec. `Tests/UnitTests/AiTargetSelectionTest.cs`. **Spec-9:** the "fixed" 180-ly-squared cutoff is real but sits in the wrong routine (headline finding #9 is moot as a *freighter* rule; it describes the warship hunter). |  |
| 17 | Value-per-time-of-arrival scoring (normalized by warp-5²=25) | ❌ | ❌ | `FreighterRoutingSelector.cs:183-189`, self-documented as a reconstruction, not bit-verified. **Spec-9:** **CONTRADICTS**: T = ceil(trunc(d)/25) with minimum 1 (integer), values 500x mining value / 6,500 colony seeding / flat 25,000 urgent / 100 x min(100M/cap, 100 - fullness); `ValuePerTimeOfArrival` uses float d/25 with a 0.01 floor. |  |
| 18 | Production-advisor fixed decision chain (overall shape) | ⚠️ | ⚠️ | `Nova.Ai/DefaultPlanetAI.cs:77-162` implements factories/mines/ships/defense-% only. **Spec-9:** **CONTRADICTS in detail**: the real end-of-pass top-up (`FUN_10a8_1e8a`) queues Factories at the *bottom*, Mines at the *top*, Mineral Alchemy and (category 5 only) Terraform, from surplus = stock + mining - full queue cost; `DefaultPlanetAI` puts factories first, sizes them from stock, has no Alchemy/Terraform, and deletes unstarted queue items every turn. |  |
| 19 | Defense-percentage advisor (decaying act-chance, 5% floor) | ✅ | ✅ | `Nova.Ai/DefensePercentageAdvisor.cs:46-55`. `Tests/UnitTests/DefensePercentageAdvisorTest.cs`. |  |
| 20 | Mass-driver/mineral-packet advisor | ❌ | ❌ | Not found. |  |
| 21 | Two further component-category advisors (cap 4) | ❌ | ❌ | Not found. |  |
| 22 | Resource-output percentage discount (gated by global flag) | ❌ | ❌ | Not found. |  |
| 23 | ~~Auto-load colony cargo (surplus > 49)~~ **retracted: this is the bomber-threat Defenses advisor** | ❌ | ❌ | Not found (different from the pre-existing new-colonization cargo load). **Spec-9:** `FUN_1090_420c`: from turn (galaxy-size index + 2) x 10, when a foreign fleet with a bomber-hull design orbits the planet and nothing is queued, queue Defenses (and Alchemy at the top). Not implemented. |  |
| 24 | Production-queue insertion cap (200 items) | ✅ | ❌ | `DefaultPlanetAI.cs:50,72-75`. No dedicated test. |  |
| 25 | Isolation check gating expansion investment (distance bands, >59 stars) | ❌ | ❌ | Not found. **Spec-9:** constants corrected (350 ly, turn > 59, nearest *non-owned* planet, only personalities 4 and 5 call it) - see row 7. |  |
| 26 | Ship auto-design pipeline (assemble from hull + best components) | ⚠️ | ✅ | Only transport design auto-assembled (`DefaultAIPlanner.cs:174-262`, `ShipDesignRefresher.cs:101-118`). |  |
| 27 | Age-based design refresh scheduling (35/50-turn thresholds, grace period, category rotation) | ⚠️ | ✅ | `ShipDesignRefresher.cs:56-79` implements only the collapsed 35-turn threshold; urgency tiers/grace/rotation not implemented. |  |
| 28 | Obsolete-flag immediate replacement | ❌ | ❌ | Deliberately excluded (`ShipDesignRefresher.cs:36-44`). |  |
| 29 | Fleet turn-processing order randomization | ⚠️ | ❌ | Implemented (`DefaultAi.cs:141-144`), but spec's own §8 now **retracts** this as an unconfirmed legacy misattribution — Nova still does it regardless (harmless). **Spec-9:** §8 now says plainly "walk fleets in plain array order": only the planet-order and player-order shuffles are real. **`DefaultAi.cs:159-162` should drop the fleet shuffle.** |  |
| 30 | Planet per-turn processing-order shuffle (newly-confirmed real mechanism) | ✅ | ⚠️ | **Fixed**: `DefaultAi.cs` now Fisher-Yates shuffles `shuffledPlanetAIs` once per turn (mirroring the existing fleet shuffle), and `HandleProduction` iterates that instead of `planetAIs.Values` directly. Not independently unit-tested: `DoMove`'s branching isn't unit-tested anywhere in this codebase (needs a fully-initialized game save), consistent with the existing fleet-shuffle's own untested status. |  |
| 31 | Player (empire) turn-generation order shuffle (newly-confirmed real mechanism) | ⚠️ | ✅ | **Partially fixed**: `ServerData.ShuffledEmpireOrder` is now computed once per turn (`TurnGenerator.Generate()`) and used by the main fleet-processing loop and `RemoteMiningStep`'s per-star contested-mining order. This codebase's step-pipeline architecture (batch-processes all empires per step) has no equivalent to the original's true per-player-sequential dispatch, so the shuffle is applied at the specific places within that architecture where empire order provably affects a shared outcome, not universally rethreaded through every fleet-touching pass (`BattleEngine`, `ScrapFleetStep`, `BombingStep`, `Scores` still use the old fixed `IterateAllFleets()` order - not audited this pass for whether shuffling them is safe). `Tests/UnitTests/EmpireOrderShuffleTest.cs`, `Tests/UnitTests/RemoteMiningOrderFairnessTest.cs`. |  |
| 32 | Colonizer commitment: cargo-capacity threshold (5,000) | ✅ | ✅ | `Nova.Ai/ColonizerCommitmentAdvisor.cs:57,64-67`. `Tests/UnitTests/ColonizerCommitmentAdvisorTest.cs`. |  |
| 33 | Funding-source distance gate (squared-distance 10,000) | ✅ | ✅ | `ColonizerCommitmentAdvisor.cs:61,75-78`. |  |
| 34 | Design tech-upgrade ambition ladder + research-cost budget query | ❌ | ❌ | Deliberately not ported — spec gives no modulation formula. |  |
| 35 | Persistent per-decision audit-trail bitmask | N/A | N/A | Architecturally inapplicable — stateless per-turn AI. |  |
| 36 | Stale-slot sweep utility | N/A | N/A | Spec itself can't determine real-world meaning. |  |
| 37 | 16-slot pool redistribution | N/A | N/A | Same. |  |
| 38 | Production-catalog item identities | N/A | N/A | Belongs to production-queue.md (now fully resolved there, §10). **Spec-9:** personality 5 queues 1 Genesis Device + 75 Terraform on up to min(10, planets/20) planets after year 120 (gate tests the Neutron Shield). `GenesisDeviceProductionUnit` exists; the AI never uses it. | Spec-8 also records that the original AI queues exactly one Genesis Device on a planet above 10,000 colonists when its staged plan has none, behind two random-chance rolls (`FUN_1090_2736(0xd, 1, 1, 1)`); this project's AI never builds one. |
| 39 | **Fleet-role predicates** (spec-8 §11): combat fleet = hull ids 6-10 (or armed Frigate/Nubian/Meta Morph under 500 kT hold); scout-or-warship line = hulls 4-10; hauler = hulls 0-3, 11-13 (or armed Meta Morph ≥ 500 kT) — they classify the AI's *fleets*, not production | ❌ | ❌ | `Nova.Ai` classifies fleets by its own logic (`CombatReadinessAdvisor`, `FreighterRoutingSelector`), not by these hull-id rules. **Spec-9:** fleet role is mainly decided by **per-personality design-slot ranges**; `FUN_1090_2aa8` role codes have no effect. | Spec-8 retracts the old "auto-build eligibility" reading; nothing in production depends on them. |
| 40 | AI weapon-value cache refreshed once per AI player per turn (ship table `+0x87`) | N/A | N/A | Architecture: `ShipDesign.Summary` is recomputed live. | |
| 41 | Hubs table (turn 20+: starbase planets or pop > 79 + 20 mines + 20 factories + mineral score; up to 64 hubs x 8 freighters) | ❌ | ❌ | No hub concept; the AI is stateless per turn (architecture blocker). |  |
| 42 | Freighter router scoring, salvage pickup, order shape (`FUN_1090_19c8`) | ❌ | ❌ | See row 15. |  |
| 43 | Bomber-threat Defenses advisor (`FUN_1090_420c`) | ❌ | ❌ | Replaces old row 23. |  |
| 44 | End-of-pass production top-up (`FUN_10a8_1e8a`) - Factories bottom / Mines top / Alchemy / Terraform | ❌ | ❌ | See row 18. |  |
| 45 | Category 7 economy-only and category 6 no-op semantics; skill-tier effects | ❌ | ❌ | Row 1. |  |
| 46 | Enemy-fleet hunter (`FUN_1090_1438`: 180 ly, skip 1/3 and 1/15, refuel below half fuel) | ❌ | ❌ | `CombatReadinessAdvisor` exists but is not this routine and is unwired. |  |
| 47 | Planet-attack handler: strength K (4, rising after year 130, cap 50), bombers Q (6, cap 12), q = min(3, Q/2 - 1), target bands +7/5/4/3/2/1 at 50/100/150/200/300/500 ly, invasion estimate | ❌ | ❌ | Not found. | Needs a role-tagging stand-in for design slots. |
| 48 | Rendezvous (`FUN_1090_5eae`), minelayer branch (Lay Mines from year 41, 1-in-5 relocation within 105 ly), colony-ship flow (load 10 colonists from year 5, scrap if no target) | ⚠️ | ❌ | `DefaultFleetAI.LayMines` partially covers the minelayer branch. |  |
| 49 | End-of-pass speed setter (`FUN_1058_56e2`, minefield-type warps) and haulers on battle plan 4 | ❌ | ❌ | Code uses `FreeWarpSpeed` and a fixed warp 6 for scouts. |  |
| 50 | Personality 4/5 mineral packets and Genesis logic; "Computer Players Form Alliances" target bias | ❌ | ❌ | No packet production unit and no alliances option exist. |  |

**Summary**: 11/50 implemented, 11/50 tested. (Row 39 is new in spec-8, row 40 is N/A; AI rows are otherwise unchanged by the spec refresh apart from the §8 preamble noting the AI runs once per game load and its orders reach turn generation as ordinary files.)

---

### client-interface.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Persistent shell regions (title/menu/toolbar/map/status) | ⚠️ | ❌ | `Nova.Avalonia/Views/MainView.axaml:15-37` — 2-item Menu + status bar, no toolbar/title area. | Only implement for Windowed UI |
| 2 | Draggable divider with live preview | ⚠️ | ❌ | Handled by AvaloniaDock's own splitters, no bespoke clamping. | Only implement for Windowed UI (Avalonia Windows) |
| 3 | Toolbar/menu command parity | N/A | N/A | No toolbar exists to compare against. | Only implement for Windowed UI |
| 4 | 9-slot Recent Files list | ❌ | ❌ | Not found; plain file picker only. | Only implement for Windowed UI |
| 5 | Map printing | ❌ | ❌ | Not found. | Skip completely |
| 6 | Resize responsiveness | ✅ | ❌ | Generic Avalonia layout behavior. |  |
| 7 | Startup display-tier detection + GDI resource pass | N/A | N/A | Win16 implementation detail. | Skip |
| 8 | On-demand GDI resource helpers | N/A | N/A | Pure binary-format detail. | Skip |
| 9 | Persisted settings (.ini: window placement, PBEM, tracked-object) | ❌ | ❌ | Not found. | Need something for all UIs |
| 10 | "Tracked object" persistence encoding | N/A | N/A | No such feature exists to encode. **Spec-9:** the key format is now settled: kind letter + player-slot letter (`B` + slot, up to `Q`) + object number; the saved object is dropped on restore if the player differs. Implementable if tracked-object persistence is built (low priority). |  |
| 11 | Command availability gated on context/permission | ✅ | ❌ | Standard `RelayCommand(..., canExecute)` gating throughout. |  |
| 12 | Modal editor suspends conflicting commands | N/A | N/A | Non-modal dock panels, concept doesn't transfer. | Only implement for Windowed UI |
| 13 | Trait-gated control validator (16 categories) | ❌ | ❌ | No centralized validator found. | Implement |
| 14 | Trait change strips now-illegal components from existing designs | ❌ | ❌ | Not found. |  |
| 15 | Working-copy edit pattern (accept commits, cancel discards) | ⚠️ | ❌ | Individual panels hold own state, no shared mechanism. | Only implement for Windowed UI |
| 16 | Default planet-name generator (compressed table + numeric suffix) | ❌ | ❌ | Not found (this port uses `NameGenerator.cs`'s own fixed list instead). | Existing sufficient |
| 17 | [Map] Selecting object makes it active subject for commands/panels | ✅ | ❌ | `SelectionService.cs`; `StarMapDocumentViewModel.cs:407-421`. |  |
| 18 | [Map] Selected fleet's pending route drawn as ordered legs | ✅ | ❌ | `StarMapDocumentViewModel.cs:534-555`. |  |
| 19 | [Map] Current position/waypoints/direction visually distinct | ✅ | ❌ | `StarMapRouteLegViewModel.cs:52-86`. |  |
| 20 | [Map] Selecting different fleet replaces/removes route overlay | ✅ | ❌ | Recomputed on every `SyncSelection`. |  |
| 21 | [Map] Right-click object-disambiguation picker | ❌ | ❌ | Repeated-tap cycling exists instead (deliberate, different mechanism). | Only implement for Windowed UI |
| 22 | [Map] Left-click selects; repeat click cycles co-located objects | ✅ | ❌ | `StarMapDocumentViewModel.cs:490-524`. |  |
| 23 | [Map] Search dialog to locate/filter an object | ❌ | ❌ | Not found. | Only implement for Windowed UI. Bugger menu in Android port sufficient |
| 24 | [Map] Course-plotting via drag rubber-band + tooltip | ⚠️ | ❌ | Tap-based waypoint arming instead — different UX for the same goal. | Android fine. Should be same for Windowed UI |
| 25 | [Map] Ctrl+click inserts waypoint mid-route | ❌ | ❌ | Only Move-Up/Down reordering exists. | Fine for Android. Implement for Windowed UI |
| 26 | [Map] Object-identification tooltip builder | ❌ | ❌ | Not found. | Only implement for Windowed UI |
| 27 | [Map] Starbase/Stargate/Mass-Driver capability dots | ✅ | ❌ | `StarMapStarViewModel.cs:16-27` (heuristic adaptation, disclosed). |  |
| 28 | [Map] Habitability "bullseye" ring overlay | ❌ | ❌ | Still text-only in Inspector. |  |
| 29 | [Map] Population ring overlay | ❌ | ❌ | Still text-only in Inspector. |  |
| 30 | [Map] 3-segment mineral bar overlay | ❌ | ❌ | Not found. | Exists in Android UI when planet selected. Ensure Windowed UI matches original |
| 31 | [Map] Shared "Planets:" 6-way view-mode selector | ❌ | ❌ | Not found. | Ensure Windowed UI matches original |
| 32 | [Map] Fleet-in-orbit ring | ✅ | ❌ | `StarMapStarViewModel.cs:29-49` — fixed single ring size, not two size classes. | Improve for Android. Ensure Windowed UI matches original |
| 33 | [Map] Fleet ship-count badge | ✅ | ❌ | `StarMapFleetViewModel.cs:27-36` — shows "999+" not hard-clamped 999. | Keep - better than original. |
| 34 | [Map] Tracked-object **chevron** marker (spec-9: not a diamond) | ❌ | ❌ | No "tracked object" concept distinct from selection. **Spec-9:** exact pixel shapes given: the 11x11 is a right-pointing arrow, the 5x5 a right triangle with its right angle bottom-left. | Implement |
| 35 | [Map] Bitmap-resource inventory/sprite compositing | N/A | N/A | Avalonia renders vector shapes instead. |  |
| 36 | [Map] Scan-range circle (primary) | ✅ | ❌ | `StarMapScanCircleViewModel.cs`. |  |
| 37 | [Map] Secondary penetrating-scan circle at half primary radius | ⚠️ | ❌ | Drawn from real `PenScanRange`, not derived as half primary. | Fix |
| 38 | [Map] Scanner-percentage display-size correction | ❌ | ❌ | Not found. | Fix |
| 39 | [Map] Overlap-culling for nested scan circles | ❌ | ❌ | Not found; every circle drawn unconditionally. | Fix |
| 40 | [Map] Packet-Physics Mass-Driver range overlay | ❌ | ❌ | Not found. | Fix |
| 41 | [Map] Minefield visibility overlay (3 patterns + 4-bit mask + popup) | ⚠️ | ❌ | Own/visible-by-scan logic + two flat colors; no per-type pattern, no popup. | fix |
| 42 | [Map] Route-overlap dashing | ❌ | ❌ | Always solid lines. | Fix |
| 43 | [Map] Cloak state never adjusts own scan-circle radius | ✅ | ❌ | Consistent by construction. |  |
| 44 | [Map] Exact 9-level zoom scale factors | ❌ | ❌ | Continuous 1.15x multiplicative step instead. | As is sufficient |
| 45 | [Map] Zoom recenters on same world point | ❌ | ❌ | Not found. | Fix |
| 46 | [Map] Shared view-option word + digit-key shortcuts | ❌ | ❌ | Not found. | Ensure Windowed UI matches original |
| 47 | Toolbar as configurable/persisted vertical strip | ❌ | ❌ | No toolbar exists. | Only for Windowed UI |
| 48 | 3-way window-layout preset | ❌ | ❌ | Not found. | Skip |
| 49 | Technology Browser toggle command | ❌ | ❌ | Not found. | Skip |
| 50 | Autosave interval preference | ❌ | ❌ | No autosave mechanism. | Skip for now |
| 51 | Toolbar visibility toggle | N/A | N/A | No toolbar to toggle. | Only for Windowed UI |
| 52 | Object-information panel 6-way content mode (digit keys) | ❌ | ❌ | Fixed per-object-type content instead. | Only for Windowed UI |
| 53 | Message view: 4 category-selection controls | ❌ | ❌ | Flat unfiltered list. **Spec-9:** pair with the per-message-type filter (client-ui row 25). | Only for Windowed UI |
| 54 | Message activation jumps to 1-of-11 destination views | ⚠️ | ❌ | Only battle-report replay wired. | Fix |
| 55 | Two sound-preference menu toggles | ❌ | ❌ | No audio subsystem at all. | Skip |
| 56 | "Force immediate redraw" command | N/A | N/A | Superseded by Avalonia's own model. | Fine |
| 57 | Object-count summary popup | ❌ | ❌ | Not found. | Fix |
| 58 | Startup title/main-menu screen | ⚠️ | ❌ | Same 4 actions, separate screens not one shared window. | Skip. Only for Windowed UI |
| 59 | Battle Plans: 15+default limit | ✅ | ❌ | `GlobalDefinitions.cs:158`. |  |
| 60 | Battle Plans: first record cannot be removed | ✅ | ❌ | `BattlePlansViewModel.cs:85-88`. |  |
| 61 | Battle Plans: delete-while-assigned confirmation + renumbering | ❌ | ❌ | No confirmation, no fleet-reference fixup (real bug — see standout findings). | Fix |
| 62 | Battle Plans: 36-byte record shape | N/A | N/A | Internal binary layout detail. | Skip - not relevant to gameplay |
| 63 | Relations dialog, separate from targeting | ✅ | ❌ | `PlayerRelationsViewModel.cs`. |  |
| 64 | Race Designer: six-stage wizard | ✅ | ❌ | `RaceDesignerViewModel.cs:42-60`. |  |
| 65 | New-session flow retains values across stages | ✅ | ❌ | Long-lived view model. |  |
| 66 | Random-seed dialog/config | ✅ | ❌ | `NewGameViewModel.cs:75,90-93`. |  |
| 67 | Score view (comparative standings) | ✅ | ❌ | `ScoreReportViewModel.cs`. |  |
| 68 | Tutorial view | ❌ | ❌ | Excluded from this audit per your direction. | Agreed |
| 69 | Replay controller with transport-like controls | ⚠️ | ❌ | Plain text step-log instead (disclosed simplification). |  |
| 70 | Progress dialog for long-running actions | ❌ | ❌ | Not found; legacy WinForms `ProgressDialog` still used in one path. | Probably unnecessary - modern computers so fast delays negligible |
| 71 | Secret/password entry dialog | ✅ | ❌ | `Views/RaceDesignerView.axaml:46`. |  |
| 72 | Global hotkey relay | ❌ | ❌ | Not found. | Only for Windowed UI |
| 73 | Move-Up/Move-Down reordering | ✅ | ❌ | `InspectorViewModel.cs:1113-1165`. |  |
| 74 | Delete/Backspace removes selected waypoint | ✅ | ❌ | Wired through `FleetWaypointRowViewModel.cs`. |  |
| 75 | Minefield inspector's compact display-option selector | ❌ | ❌ | Fixed non-configurable rows. | Fix |
| 76 | Cargo-transfer dialog, validates both sides | ✅ | ❌ | `InspectorViewModel.cs` (from line 397). |  |
| 77 | Fleet-merge dialog for compatible fleets | ✅ | ❌ | `InspectorViewModel.cs:227-235`. |  |
| 78 | Rename dialogs | ✅ | ❌ | `BattlePlansViewModel.cs:99-106` and similar. |  |
| 79 | Help view, working copy unchanged | ✅ | ❌ | `HelpViewModel.cs:12-24`. |  |
| 80 | Distinction between warning/validation-failure/cancelled | ⚠️ | ❌ | Ad-hoc per-panel `StatusMessage` fields. | Fix |

**Summary**: 34/80 implemented, 0/80 tested (no test project references `Nova.Avalonia` at all).

---

### client-ui-dialog-catalog.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Global modal accept/cancel/commit contract | ⚠️ | ❌ | No shared abstraction; ad-hoc `RelayCommand`s per ViewModel. | Only for Windowed UI |
| 2 | Main workspace frame: accelerators, shutdown confirm | ❌ | ❌ | No accelerator routing or Closing handler in Avalonia; WinForms-only equivalent exists (`Nova/WinForms/Gui/NovaGUI.cs:233-257`). | Fix |
| 3 | Title/status bar with race-name summary builder | ❌ | ❌ | `MainWindow.axaml:11` hardcodes the title, never rebound. | Only for Windowed UI |
| 4 | Toolbar icon actions with enabled/hover/tooltip state | ❌ | ❌ | Just a 2-item text Menu + End Turn button. | Only for Windowed UI |
| 5 | Contextual right-click popup menu on map objects | ❌ | ❌ | Not found. | Only for Windowed UI |
| 6 | Rich map hover-tooltip content modes (13 variants) | ❌ | ❌ | Only plain static tooltip strings. | Only for Windowed UI |
| 7 | Component-category Prev/Next browser | ❌ | ❌ | Deliberately one scrollable list instead (disclosed). | Only for Windowed UI |
| 8 | Planet inspector 6 modes via 1-6 number keys | ⚠️ | ❌ | Tabbed panel via `SelectedTabIndex`, no numeric-key switching. | Only for Windowed UI |
| 9 | Scanner-display % control with live tooltip | ❌ | ❌ | Not found. | For Android show scanner info in planet details. Windowed UI as per original game |
| 10 | Fleet order list + context-sensitive detail control | ⚠️ | ❌ | Waypoint list/cargo/split-merge all implemented; "give fleet to another race" task entirely absent. | Fixup for Android. Windowed UI as per original game |
| 11 | Cargo transfer editor (per-category, capacity-clamped) | ✅ | ❌ | `CargoResourceRowViewModel.cs:1-141` — arguably improved (sibling-aware clamp). |  |
| 12 | Fleet merge (eligibility, pre-select, fuel-stranding) | ⚠️ | ✅ | Fuel-stranding fully implemented & tested; auto-pre-select UI not found. |  |
| 13 | Rename surfaces: live-filter vs accept-time variants | ❌ | ❌ | Only accept-time validation exists. | Fix |
| 14 | Slot editor graded compatibility | ⚠️ | ❌ | Binary compatible/incompatible only, no tech-distance grading. **Spec-9:** the four caption variants and the graded result are now traced; the near-miss grades apply only when the single short field is the one currently being researched. |  |
| 15 | Ship & Starbase Designer (filters, Copy, delete-in-use confirm) | ⚠️ | ❌ | **Missing delete-in-use confirmation is a real regression** vs. the WinForms version, which does warn. | Fix for all |
| 16 | Minefield overlay (3 fill patterns) + visibility popup | ⚠️ | ⚠️ | One plain circle, no per-type pattern, no popup. | Fix for all |
| 17 | New-session simplified + detailed wizard | ⚠️ | ❌ | Single 3-tab wizard instead of two distinct paths. | Fix |
| 18 | Randomization seed entry | ✅ | ❌ | `NewGameViewModel.cs:75,90-95`. |  |
| 19 | Local access-secret entry/replacement | ❌ | ❌ | No change-password dialog. | Fix for all |
| 20 | Production queue editor (add/remove/reorder/clear, auto-build) | ⚠️ | ✅ | Fully present except the "leftover-only" checkbox (which **does** exist in the WinForms `ProductionDialog` but was never carried over) and Prev/Next planet paging (which, per a deeper sub-pass, **never existed in WinForms either** — not an Avalonia regression, a pre-existing gap). | Fix |
| 21 | Saved production-template manager (4 named slots) | ❌ | ❌ | No such concept anywhere. | Implement for all UIs. Make option for "favourite to apply to all newly colonised planets" |
| 22 | Research allocation editor (6 fields + lowest-field combo) | ⚠️ | ❌ | Per-field budget + target combo present; no "lowest field" auto-allocate. | Fix |
| 23 | Battle plans editor (Primary/Secondary/Tactic/DumpCargo/Rename) | ⚠️ | ❌ | All present except a separately-confirmed "Attack Who" setting. | Fix |
| 24 | Inter-owner relations (3 states, queued order) | ✅ | ❌ | `EmpireRelationRowViewModel.cs:22`. |  |
| 25 | Messages viewer (per-type filter with hide/show toggle, Next/Prev skipping filtered types) | ⚠️ | ❌ | Flat list with battle-replay linking only. **Spec-9:** the 392-bit bitmap is a **per-message-type filter**, not a read/unread flag (blue tick / red cross, magnifier toggles hide-filtered vs show-filtered; persisted in the player file). `MessagesViewModel` has no filtering. | Fix |
| 26 | Reports grid (4 types, toggleable columns, idle/ETA) | ⚠️ | ❌ | Separate simple tables per type; no shared grid, no idle/ETA column. | Fix |
| 27 | Report TSV/plain-text export | ❌ | ❌ | Not found. | Skip |
| 28 | Score display (3 cyclable modes, 9 categories) | ⚠️ | ❌ | Single-mode, current-turn-only. **Spec-9:** **CONTRADICTS** (see victory-conditions row 16): the score record has nine rows (Planets, Starbases, Unarmed/Escort/Capital ships, Tech Levels, Resources, Score, Rank) and an exact formula that `ServerState/Scores.cs` does not follow. **Implemented (spec-9 pass):** the score record and formula are now exact (victory-conditions row 16); the three cyclable display modes and history graph are not. | Fix |
| 29 | Tutorial surface | ❌ | ❌ | Excluded per your direction. | Agreed |
| 30 | Event replay/Battle VCR (10×10 board, animation) | ⚠️ | ⚠️ | Plain stepped text log; richer board exists WinForms-only. | Implement |
| 31 | Map printing | ❌ | ❌ | Not found. | Skip for all |
| 32 | Progress indicator lifecycle | ❌ | ❌ | **Confirmed a total, deliberate gap** — Avalonia has NO progress UI for any lengthy operation, not just one path: `GameSession.cs:25-32`'s own comment explains the legacy WinForms `ProgressDialog` deadlocks when hosted outside a classic WinForms `Application`, so component loading now runs headless/synchronous with nothing shown at all. | Skip, unlikely to need this as modern computers so fast there's no discernable delay. |
| 33 | Hidden diagnostic/anti-cheat log-writing path | N/A | N/A | Original-binary leftover, not wanted. **Spec-9:** retracted: the hidden FPU diagnostic does not exist, so the N/A justification is moot. | Skip |
| 34 | Styled text entry | ✅ | ❌ | Native Avalonia `TextBox`. |  |
| 35 | Styled single-choice selector | ✅ | ❌ | Native Avalonia `ComboBox`/`RadioButton`. |  |
| 36 | Styled list | ✅ | ❌ | Native Avalonia `ListBox`. |  |
| 37 | Segmented bar/gauge + repeating-button controls | ⚠️ | ❌ | **Correction from a deeper sub-pass**: auto-repeat IS implemented (Avalonia's built-in `RepeatButton` drives the Production +/- quantity steppers, `ProductionView.axaml:50-51,85-87`). The segmented-bar gap is real but more specific than first stated: WinForms' `CargoMeter.cs` genuinely draws ONE bar with sequential colored segments (Ironium/Boranium/Germanium/Colonists) plus a 3D bevel border; Avalonia's `RangeBarViewModel` replacement draws each mineral as its own **separate single-color bar** instead of one multi-segment control, and has no near-limit tri-color state (neither does the segmented WinForms version, for what it's worth). | Find way to implment in Avalonia. Windowed UI to implement as per original |
| 38 | Word-wrap text layout engine | ✅ | ❌ | Native Avalonia `TextWrapping`. |  |
| 39 | Font loader + autofit/shrink-to-fit | ❌ | ❌ | Not found. | No relevent, skip |
| 40 | Escape-coded report-text formatting engine | ⚠️ | ❌ | Plain C# string interpolation achieves the same end effect. | Agreed, skip |
| 41 | Merge Fleets dialog (listbox + Select All/Unselect All) | ⚠️ | ❌ | Single combo per fleet instead of multi-select listbox. | Implement |
| 42 | Simple dialogs: Find Planet/Fleet, Change Password, Print Map | ❌ | ❌ | None found. | Implement (probably burger-menu for Avalonia Android). Windowed UI as per original |
| 43 | Stars! Serial Number (copy-protection) | N/A | N/A | Not wanted — abandonware clean-room clone. | Agreed, skip |
| 44 | Stars! Host Mode dialog | ❌ | ❌ | Not found. | Skip |
| 45 | Technology Browser dialog | ❌ | ❌ | Not found. | Implement (burger menu for Avalonia Android) |
| 46 | Victory Conditions in-session summary dialog | ❌ | ❌ | Only exists in the setup wizard. | Implement |
| 47 | Race Wizard exact-label pages + 8 preset styles | ⚠️ | ❌ | Core traits/costs implemented, consolidated into 2 pages; **preset styles/Random-race generator entirely absent**. | Implement |

| 48 | **Message-click routing**: clicking a message of type 62, 63 or 175-180 opens the production queue of the named planet (spec-8; 175-180 are never produced by the original build) | ❌ | ❌ | Not found — a message selects/goes to its object only. | Low value: only 62/63 (queue-empty notices) are ever produced, and this project does not post them. |

**Summary**: 26/46 implemented, 4/46 tested.

---

### combat-resolution.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Battle triggers only when co-located races have hostile orders | ✅ | ⚠️ | `ServerState/BattleEngine.cs:338-357,605-633`. |  |
| 2 | "Legitimate enemies" single Attack category, shared by combat+bombing | ✅ | ✅ | `BattleEngine.cs:828-860`, reused by `Bombing.cs:66`. `Tests/UnitTests/BattlePlanAttackPolicyTest.cs` (5 tests). |  |
| 3 | 10x10 grid/token positioning | ⚠️ | ⚠️ | `BattleEngine.cs:501-566` places stacks at one shared point per race, not per-token squares. |  |
| 4 | 256-token cap, fair per-race allocation | ✅ | ❌ | `BattleEngine.cs:418-495`. |  |
| 5 | 16-round cap | ✅ | ⚠️ | `BattleEngine.cs:41,573-593`. |  |
| 6 | Round-tiered movement (0-3 squares, bonus schedule) | ✅ | ❌ | `BattleEngine.cs:49-78,867-1003`. |  |
| 7 | Weight-based movement order with near-parity randomization | ❌ | ❌ | `BattleEngine.cs:890` — explicit TODO, list-order iteration only. |  |
| 8 | Six movement tactics | ⚠️ | ⚠️ | Three "Maximise" tactics collapsed into one; `BattlePlan.cs:69-76` self-documents "only two tactics switched on". |  |
| 9 | Starbase +1 range bonus + fleeing-ships-misjudge-range quirk | ✅ | ✅ | Not found. **Spec-9:** now fully specified: +1 reach for a starbase firer, but the falloff divisor uses the stored range only (a starbase range-1 beam at distance 2 loses 20%). **Implemented (spec-9 pass):** `BattleEngine.WeaponReach` gives a starbase firer +1 reach; `BeamFalloffPercent` divides by the stored range only. `BattleCombatRulesTest`. |  |
| 10 | 7-way target-type classifier | ⚠️ | ✅ | `BattleEngine.cs:683-715`; Freighters/Fuel Transports are a disclosed heuristic substitute. |  |
| 11 | Attractiveness formula | ⚠️ | ✅ | `BattleEngine.cs:734-798` — **Fixed**: removed the beam range-falloff term the spec's §4 exhaustively confirms doesn't exist in the real targeting formula (it belongs only to the separate beam range-dissipation formula applied to actual damage, §6). `Tests/UnitTests/BattleAttractivenessRangeTest.cs`. **Spec-9:** standard beam score = cost x deflector% / (armor + shields + 1); sapper = ceil(cost x deflector% / shields), 0 (never picked) with no shields; ties go to the earlier token. **Code**: a shieldless target gives the sapper `double.MaxValue` (most attractive), targets are chosen once per stack per round regardless of reach. **Implemented (spec-9 pass):** `GetAttractiveness` weapon-specific overload: standard beam cost x deflector% / (armor + shields + 1), sapper ceil(cost x deflector% / shields), shieldless sapper target scores 0, ties to the earlier token. Still one target per stack per round (shot-time retargeting not done). |  |
| 12 | Initiative computation (PRT bonus, 0-8 clamp, mass penalty) | ⚠️ | ❌ | `Common/Components/ShipDesign.cs:463-478` — hull+computer only, no PRT bonus/clamp/mass term. |  |
| 13 | Firing-order tie-break (range then persistent coin-flip) | ⚠️ | ❌ | `ServerState/WeaponDetails.cs:58-69` — unstable sort, not a persisted coin-flip. |  |
| 14 | Per-weapon-slot firing, ShipsInToken scaling | ✅ | ❌ | `BattleEngine.cs:1445-1471`. |  |
| 15 | Beam range dissipation | ✅ | ✅ | `BattleEngine.cs:1458-1463`. **Spec-9:** exact order of operations: slots x ships x damage, x capacitor%, x deflector%, x (100 - floor(10 d / range)) / 100, every step truncating, Chebyshev distance. `CalculateWeaponPower` uses a continuous 0.1 x min(1, d/range) with Euclidean distance. **Implemented (spec-9 pass):** exact order (slots x ships x damage, x capacitor%, x deflector%, x (100 - floor(10d/range))/100, truncating each step) with Chebyshev distance (`GridDistance`). |  |
| 16 | Beam deflector stacking (0.9^n) | ✅ | ✅ | `Common/Components/ProbabilityProperty.cs:118-137`. **Spec-9:** **BUG (pre-dates spec-9, found by this audit): beam deflectors never take effect.** `components.xml` gives them a `"Beam Deflector"` property that `ShipDesign.SumProperty` has no case for; `ShipDesign.BeamDeflectors` reads the key `"Deflector"` and so always returns 0. Even once wired, `ProbabilityProperty` gives 72.9% at three deflectors where the spec truncates to 72 (90 / 81 / 72 on a 1000 scale). **Implemented (spec-9 pass):** **deflector bug fixed**: `ShipDesign.SumProperty` handles `"Beam Deflector"`, `BeamDeflectorPercent` is 90/81/72/65 on a 1000 scale, `BeamDeflectors` reads it. Includes a test over the real `components.xml` record. |  |
| 17 | Shields absorb before armor | ✅ | ✅ | `BattleEngine.cs:1273-1290,1354-1387`. **Spec-9:** sappers never touch armor and do nothing to a shieldless token; `FireBeam` (line 1315) sends sapper overflow into armor. **Implemented (spec-9 pass):** sappers never touch armor and do nothing to a shieldless token; remaining shield pool re-split per ship (floored). |  |
| 18 | Torpedo/missile independent per-missile hit/miss | ✅ | ❌ | `BattleEngine.cs:1307-1345` — matches spec's worked example exactly. |  |
| 19 | Capital missile double damage post-shield-depletion | ⚠️ | ❌ | Approximated via all-or-nothing check, not precise prorating (disclosed). **Spec-9:** capital-missile doubling is decided **once per salvo, before any missile resolves**, only if the target pool is already 0; `FireMissile` doubles mid-salvo. Worked Example 3 was redone. **Implemented (spec-9 pass):** doubling decided once per salvo before any missile resolves, only when the pool is already 0; salvos over 200 use the expected-value shortcut; two aggregated damage applications (miss chip, then hits). |  |
| 20 | Accuracy formula (jam/computer subtractive cancellation) | ✅ | ✅ | `BattleEngine.cs:1489-1501` uses a simpler approximation, self-flagged as unconfirmed — spec now fully confirms the real formula. **Spec-9:** the formula is now exact: residual = jam - computer; if >= 0, base x (100 - residual)/100, else 100 - (100 - leftover) x (100 - base)/100, clamped 1-100 (base 20 + Battle Super Computer 30 = 44). The hit test `accuracy >= random.Next(0,100)` is likely off by one. **Implemented (spec-9 pass):** `CalculateWeaponAccuracy` is the exact two-branch integer formula, clamped 1..100 (44 / 16 / 28 on the published cases). Hit test changed to `roll < accuracy` (all three spec statements imply it). |  |
| 21 | Whole-ship-kill division + 1/500 armor-quantization exploit | ⚠️ | ❌ | Correct division, but per-token not per-1/500-quantized (disclosed). **Spec-9:** kill accounting is exact: already-damaged ships die first needing only their remaining armor, the remainder is pooled over the survivors and rounded up to the next 1/500 of armor, every survivor then counts as damaged. **Implemented (spec-9 pass):** one-kill limit via `DamageArmor(killLimit)` with surplus discarded. **Not done**: damaged-ships-first ordering and 1/500 quantisation (single armor total per token). |  |
| 22 | Energy/flux capacitor beam-damage bonus | ✅ | ✅ | `BattleEngine.cs:1466-1469` — explicit "not modeled" comment. **Spec-9:** capacitors compound (Energy 10, Flux 20 per capacitor), capped at 255%. `CapacitorProperty.Maximum = 250` is a *bonus* (350% total) and nothing reads it in combat. **Implemented (spec-9 pass):** `ShipDesign.CapacitorPercent` compounds (100 + rate)/100 per capacitor, capped at 255; `Capacitor.Maximum` corrected 250 -> 155 (a bonus); used in beam damage. |  |
| 23 | Gatling-type "hits-all" fire mode | ✅ | ✅ | `WeaponType.gatlingGun` exists in data but no multi-target logic. **Spec-9:** Gatling "hits-all" is fully specified: one shot hits every enemy token in reach matching the primary or secondary type, full damage x capacitor% x that token's deflector%, no range falloff. `WeaponType.gatlingGun` is treated as a plain beam. **Implemented (spec-9 pass):** `FireGatling` hits every primary/secondary-type enemy in reach at full damage x capacitor% x that token's deflector%, no falloff. |  |
| 24 | Battle-end conditions | ✅ | ⚠️ | `BattleEngine.cs:576-593`. |  |
| 25 | Disengage retreat (7 squares) | ✅ | ✅ | `BattleEngine.cs:47,972-991`. `Tests/UnitTests/BattleTargetTypeAndDisengageTest.cs`. |  |
| 26 | Salvage = 1/3 mineral cost | ✅ | ❌ | `BattleEngine.cs:238-259`. |  |
| 27 | Deep-space salvage decay | ✅ | ❌ | `Common/GameObjects/DeepSpaceMinerals.cs:67-87`. |  |
| 28 | Tech-gain-from-battle | ⚠️ | ❌ | `BattleEngine.cs:1544-1594` — uses Nova's existing 6-field system, not the original's undeciphered weighted tables. |  |
| 29 | Planetary bombing | ✅ | ⚠️ | `ServerState/Bombing.cs:48-161`. |  |
| 30 | Component diminishing-returns stacking, 63%-cap distinction | ✅ | ✅ | Generic formula used uniformly; no 63%-cap variant. **Spec-9:** **new bug**: `Computer.operator+` (`Computer.cs:110-118`) builds the sum then `return op1;`, so computers in different slots never stack for accuracy or initiative. The 0-63 capped output is an initiative-family value added to the weapon initiative. **Implemented (spec-9 pass):** `Computer.operator+` returns the sum, so computers in different slots stack. |  |
| 31 | Canonical component stat tables | ✅ | N/A | Spot-checked against spec §10a — exact match. |  |
| 32 | Cloaking: piecewise curve, PRT baseline, Tachyon Detector table | ✅ | ✅ | **Fixed**: `Common/Components/CloakCalculator.cs` implements the decompiled piecewise raw-units-to-percent curve and the 18-entry Tachyon Detector counter-cloak table; `ShipDesign.SumProperty`/`Update` now sum raw cloak units per design (Super Stealth's flat +300 baseline, and a back-solved +40 for Improved Starbases — disclosed as not decompiled like SS's figure, see the code comment) instead of the old flat hardcoded 20% (`ServerState/Manufacture.cs`'s ISB special-case, now removed as redundant); `Fleet.RecalculateCloak` combines a fleet's designs as a mass-weighted average; `ScanStep.cs` applies the Tachyon Detector multiplier to the target's cloak before the effective-scan-range check. `Tests/UnitTests/CloakingTest.cs` (curve breakpoints, detector table, raw-unit summation vs. probability-combination, SS/ISB baselines, fleet mass-weighting, and a full `ScanStep` detector-extends-range integration test). **Spec-8 confirms the +40 Improved-Starbases baseline** (`FUN_1048_57b6` adds a flat 40 for a starbase chassis of an ISB race — it is no longer merely back-solved) **and adds the edge case**: a raw total that is zero, negative or above 25,000 returns 0 (no cloak), now implemented in `CloakCalculator.PercentFromRawUnits` with test cases. |  |
| 33 | Per-hull weapon-slot firing order | N/A | N/A | Nova aggregates weapons per design, no discrete slot-order concept. |  |
| 34 | **Per-component cloak points beyond the electrical cloaks** (spec-8 addendum: 20 pts for engine subtype 8 / shield subtype 6 / general-purpose subtype 4 / category-0x10 subtype 18, 40 for scanner subtype 6 and armor subtype 9, 70 for shield subtype 4, 50 for armor subtype 7, 60/50 for bomb subtypes 6/7; "Multi Cargo Pod provides a 10% cloak" = 20) | ✅ | ✅ | `components.xml` carries raw cloak units for the dedicated cloak devices and the Multi Function Pod (this session); the other subtypes listed were **not mapped to named components**, so they contribute no cloak. **Spec-9:** **the subtype-to-name table is now given** (§11, 17 rows): Enigma Pulsar 20, Chameleon Scanner 40, Shadow Shield 70, Langston Shell 20, Depleted Neutronium 50, Mega Poly Shell 40, Multi Contained Munition 20, Alien Miner 60, Orbital Adjuster 50, Multi Cargo Pod 20. See ship-design row 40 for the (wrong) values in `components.xml`. **Implemented (spec-9 pass):** the data side is done: the ten named components now carry the spec's raw cloak points in `components.xml` (`ComponentTraitGateTest.RawCloakPoints_MatchTheSpecTable`). | Needs the subtype→component mapping (the spec gives subtype numbers, not names) — see ambiguity list. |
| 35 | Chebyshev grid distance for reach and falloff (spec-9 §6) | ✅ | ✅ | `ProcessAttack` (line 1211) uses Euclidean distance, so a range-1 weapon cannot hit a diagonally adjacent token. **Implemented (spec-9 pass):** Chebyshev distance for reach, falloff and the movement threat check. |  |
| 36 | Missiles: "one missile, one kill" limit with re-allotment of surplus missiles; salvos over 200 use the expected-value shortcut | ⚠️ | ✅ | Spec-8's negative finding is withdrawn. Not implemented. **Implemented (spec-9 pass):** one-kill limit done; **re-allotment of surplus missiles to a further target is not** (allotment rounding unspecified). |  |
| 37 | Beam overflow to the next most attractive target in reach (never at starbases) | ✅ | ✅ | TODO at `FireBeam` line 1331. **Implemented (spec-9 pass):** `FireBeam` overflow re-aims the undeflected, unattenuated remainder at the next target in reach (primary then secondary), never after a starbase. |  |
| 38 | Regenerating Shields per-round rule: from round 2, tokens with shields above 0 regain floor(full/10) per ship; a token knocked to 0 never regenerates; each battle starts at full shields | ✅ | ✅ | `ApplyRegeneratingShields` (line 608) also regenerates zero-shield tokens and does not floor per ship; "full shields at battle start" happens via the turn repair step, not at battle start. **Implemented (spec-9 pass):** `ApplyRegeneratingShields`: rounds 2-16, only tokens with shields > 0, floor(full/10) per ship, capped; tokens start each battle at full shields. |  |
| 39 | Regenerating Shields armor penalty halves only Armor-category parts (per slot, floored) - not hull armor, Croby Sharmor, Langston Shell, Multi Cargo Pod | ✅ | ✅ | **CONTRADICTS**: `ShipDesign.Update` (~line 895) halves the whole summed Armor including `Hull.ArmorStrength`; `RegeneratingShieldsTest.cs:54-61` asserts hull armor 200 -> 100, locking the wrong behaviour in. **Implemented (spec-9 pass):** `ShipDesign.Update` halves only Armor-category parts per slot (floored), shields = base + floor(2/5 base) capped at 65,535; `RegeneratingShieldsTest` rewritten. |  |

**Summary**: 36/39 implemented, 24/39 tested (row 32 gained the spec-8 25,000-unit guard; row 34 is new).

---

### diplomacy-relations.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | One relation value per ordered race pair (asymmetric) | ✅ | ⚠️ | `Common/DataStructures/EmpireIntel.cs:39`; per-empire dictionaries. |  |
| 2 | Exactly three states: Enemy/Neutral/Friend | ✅ | ✅ | `Common/DataStructures/EmpireData.cs:34-39`. |  |
| 3 | Dialog offering three mutually-exclusive choices | ✅ | ❌ | `PlayerRelationsView.axaml:11-16`. |  |
| 4 | New race's relation initializes to Neutral | ✅ | ✅ | `ServerState/NewGame/GameInitialiser.cs:149-161`. **Fixed the latent trap**: `EmpireIntel(EmpireData)`'s constructor now explicitly sets `Relation = PlayerRelation.Neutral` itself, instead of silently defaulting to `Enemy` (enum value 0) whenever a caller forgot to set it explicitly afterward (as `GameInitialiser.cs` happened to already do, masking the bug there). `Tests/UnitTests/EmpireIntelDefaultRelationTest.cs`. |  |
| 5 | Relationship change is a queued turn order, not immediate | ✅ | ❌ | `Common/Commands/RelationCommand.cs`. |  |
| 6 | No automatic per-turn relationship decay | N/A | N/A | Confirmed non-mechanic — correctly not modeled. |  |
| 7 | ~~Relationship gates cooperative colonist/mineral/fuel "meetings"~~ **retracted by spec-9** | N/A | N/A | Not found — only combat/bombing/invasion consume relations. Friend-rated owners' fleets also do not yet get the "friendly planet" refuel/terraform treatment. **Spec-9:** no cooperative-meeting mechanic exists: `FUN_10f0_6ea2` is *orbital bombardment* (gated by a relationship check whose rule was not re-derived). The Friend-gated *fleet terraforming* pass (`FUN_10b8_2e04`) is a separate, unimplemented behaviour (see row 12). |  |
| 8 | Invasion requires Enemy relation | ✅ | ❌ | `Common/Waypoints/InvadeTask.cs:110-129`. |  |
| 9 | Battle Plan Attack resolves 5 categories from relation table | ✅ | ✅ | `BattlePlan.cs:88-91`; `BattleEngine.cs:828-860`. `Tests/UnitTests/BattlePlanAttackPolicyTest.cs`. |  |
| 10 | Same resolution reused for bombing | ✅ | ⚠️ | `Bombing.cs:66`. |  |
| 11 | No built-in AI ever submits a relation-change order | ✅ | ❌ | Confirmed via grep across `Nova.Ai/`. **Spec-9:** also: AI code never reads the relation table; the "Computer Players Form Alliances" option only biases AI targets toward humans (no such option exists here). |  |
| 12 | Friend-rated planet owner lets a fleet's terraforming improve their planet; otherwise a no-starbase planet is degraded (messages 300/301/346/347) | ❌ | ❌ | No fleet-terraforming step exists in `ServerState` (turn-generation row 23). |  |

**Summary**: 9/11 implemented, 4/11 tested.

---

### dynamic-string-table.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1-6, 8-10, 14-16, 19-21, 23-24 | Binary compression-format mechanics | N/A | N/A | Not applicable — proprietary string-compression format, clean-room .NET string handling used instead. | Agreed, skip |
| 7 | Waypoint task name ground truth (Transport/Colonize/etc.) | ⚠️ | ⚠️ | This codebase's labels differ in wording ("Colonise"/"Scrap"/"Lay Mines" vs. "Colonize"/"Scrap Fleet"/"Lay Mine Field"); Remote Mining/Patrol/Route/Transfer Fleet don't exist as separate task classes. | Implement |
| 11 | Identifier-space content map (file-I/O errors, component text, etc.) | ⚠️ | ❌ | Component labels partly match; file-I/O error wording not found. | skip |
| 12 | Backslash-letter template substitution convention | ❌ | N/A | Codebase builds text via plain string concatenation instead. | skip |
| 13 | Specific quoted examples ("Armor", "Beam Weapons", etc.) | ⚠️ | ❌ | "Armor"/"Beam Weapons" match exactly; others not found verbatim. |  |
| 17 | Message text validation vs. known codes | ⚠️ | ⚠️ | Analogous events exist with different wording throughout. |  |
| 18 | Full message-vocabulary inventory | ⚠️ | ⚠️ | Most categories exist with own wording; **Mystery Trader and comet events are entirely unimplemented**. | Fix |
| 22 | Recovered 999-entry default name roster incl. joke entries | ⚠️ | ❌ | `ServerState/NewGame/NameGenerator.cs:164-1375` is an almost exact match — **missing the 4 joke/numeric entries (007/911/90210/555-1212)**. |  |

**Summary**: 6/24 implemented, 3/24 tested.

---

### fleet-movement-scanning-cargo.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Warp speed distance = N² ly/year | ✅ | ⚠️ | `Common/GameObjects/Fleet.cs:554-555`. |  |
| 2 | Engine-rated max warp constrains orderable speed | ❌ | ❌ | Not enforced anywhere. |  |
| 3 | Warp 10: 10%/yr per-ship destruction unless safe | ✅ | ❌ | `ServerState/TurnGenerator.cs:475,621-670`. |  |
| 4 | Cheap Engines: >warp 6, 10%/yr fail-to-launch | ✅ | ❌ | `TurnGenerator.cs:481-489`. |  |
| 5 | Per-waypoint Warp Factor 0-10 + "Use Stargate" sentinel (11) | ✅ | ✅ | **CONFIRMED** (carried forward from v6 audit). `Global.StargateWarpFactor`; `StargateJumpTest.cs`. |  |
| 6 | Warp 0 = dedicated "hold position" order | ⚠️ | ⚠️ | Achieved as a side-effect of zero-speed math, not an explicit early-exit; still runs minefield check (AMBIGUOUS, low priority). |  |
| 7 | Fuel consumption formula, IFE ×0.85 | ✅ | ❌ | `Common/Components/ShipDesign.cs:878-901`. |  |
| 8 | Ramscoop fuel-generation step pattern | ⚠️ | ❌ | Simplified rule-based approximation, disclosed. |  |
| 9 | **Every engine generates ≥1mg fuel at warp 1 — BUG** | ✅ | ✅ | **Fixed**: `Fleet.cs:680-712` now gives every engine a `perEngineFactor` of 1 at warp 1 (ramscoops keep their existing step-table above that). `Tests/UnitTests/FleetFuelGenerationTest.cs`. | Definitely fix |
| 10 | Radiating Hydro-Ram Scoop irradiates colonists in cargo | ❌ | ❌ | Not found. |  |
| 11 | Fuel exhaustion → partial hop (not full jump then zero) | ✅ | ❌ | `Fleet.cs:567-577`. |  |
| 12 | Underfueled fleet auto-downgrades next leg's warp (Nova-only addition) | ⚠️ | ❌ | `Fleet.cs:608-613` — undocumented in spec either way. |  |
| 13 | Standard vs. penetrating scanner tiers | ⚠️ | ❌ | Planet-side penetrating scan is a TODO stub (`ScanStep.cs:106`). |  |
| 14 | No Advanced Scanners (NAS): doubles conventional range | ✅ | ❌ | `ShipDesign.cs:749-756`. |  |
| 15 | Multi-scanner combination (4th-root of sum of 4th powers) | ✅ | ❌ | `Common/Components/Scanner.cs:107-129` — exact formula match. |  |
| 16 | Cloak reduces effective detection range | ✅ | ✅ | **Fixed/tested**: `ScanStep.cs`'s effective-scan-range check now runs the target's `Fleet.Cloaked` (itself now computed via `CloakCalculator`/`Fleet.RecalculateCloak`, see combat-resolution.md row 32) through the Tachyon Detector counter-cloak multiplier before reducing range. `Tests/UnitTests/CloakingTest.cs`'s `ScanStepTachyonDetectorTest`. |  |
| 17 | Space Demolition minefield-based cloaked-fleet detection | ❌ | ❌ | Not found. |  |
| 18 | Minefield detection radius formula | ❌ | N/A | Not found. |  |
| 19 | Packet Physics mineral-packet built-in scanner | ❌ | N/A | No mass-packet mechanic exists at all. |  |
| 20 | Wormhole probabilistic detection (75% cloak) | ❌ | ❌ | Deliberately simplified to "visible once in range" (disclosed). |  |
| 21 | Wormhole placement (4 squared-distance tiers) | ⚠️ | ✅ | Simpler plain minimum-distance check instead (disclosed). `Tests/UnitTests/WormholeTest.cs`. |  |
| 22 | Wormhole annual drift + 7-tier stability | ✅ | ✅ | `ServerState/TurnSteps/WormholeDriftStep.cs`. |  |
| 23 | Wormhole heavy-mineral-cargo transit (**spec-8: this is the Mystery Trader's mineral-for-technology trade, not a wormhole check**) | ❌ | ❌ | Explicitly left unimplemented (disclosed); the Trader itself does not exist. **Spec-9:** superseded: this is the Mystery Trader encounter (turn-generation §5a) - the fleet is always absorbed, the "tier" is the number of tech advances granted, the "26 or 10" is a tech-level cap. Nothing implemented. |  |
| 24 | Base cargo capacity per hull, fuel as separate pool | ✅ | ❌ | `Fleet.cs:387-427`. |  |
| 25 | Cargo-pod bonuses (+50/+100 kT) | ✅ | ❌ | `components.xml` — matches exactly. |  |
| 26 | Multi Cargo Pod (+250 kT third tier) | ❌ | N/A | Component doesn't exist in `components.xml`. |  |
| 27 | Fuel-tank bonuses (+250/+500mg) | ✅ | ❌ | `components.xml` — matches exactly. |  |
| 28 | Anti-matter Generator +200mg fuel bonus | ✅ | ❌ | `components.xml` — matches exactly. | See row 52 for its separate +50 mg/year generation. |
| 29 | Fixed-amount Load/Unload cargo task | ⚠️ | ❌ | `Common/Waypoints/CargoTask.cs:180-228`. **Spec-9:** the Transport task is now traced in full (four passes a turn, five slots Fe/Bo/Ge/colonists/fuel, caps on free space and source holdings). **CONTRADICTS**: `CargoTask.cs:145-228`/`Cargo.cs:227-241` clamp nothing (loads can overfill, sources can go negative), any task at a foreign star runs `InvadeTask` (even a Load or mineral unload), minerals cannot be dropped at an unowned star, no fuel slot. |  |
| 30 | Conditional "Set Waypoint to `<amount>`" cargo automation | ❌ | N/A | Only fixed-amount supported. **Spec-9:** fully specified (Set Waypoint to N = source - N, Fill/Wait to N% = floor(N% x capacity) - carried). |  |
| 31 | Fill-to-percentage / "Load Optimal" cargo modes | ❌ | N/A | Not found. **Spec-9:** fully specified; but §4 still carries a stale "habitability-driven Load Optimal" paragraph that contradicts the fuel-slot rule (see ambiguity list). |  |
| 32 | Inner Strength: colonists reproduce in cargo hold (spec-8: `growth% × colonists / 200`, landed on the orbited planet if the hold is full) | ✅ | ✅ | Not found. **Spec-9:** now exact: growth = floor(R x C / 200) hundreds of colonists (R = growth slider 1-20, no habitability term); if 0, a 1/3 chance of +1; clamped to hold space (msg 251); overflow lands only on an orbited planet of the *same* race (msg 344), otherwise lost. Implementable now. **Implemented (spec-9 pass):** `ColonistBreedingStep` (key 9): floor(R x C / 200) units, 1/3 chance of +1 when zero, clamped to hold space, overflow only onto an own orbited planet. `ColonistBreedingStepTest`. | Concrete now, but unit/rounding wording ambiguous — see list. |
| 33 | ~~100-vs-25 colonist/fuel transfer-chunk divisor (MA)~~ **retracted by spec-9** | N/A | N/A | Transfers are instantaneous, no chunking. **Spec-9:** the divisor came from the production purchase routine (the Mineral Alchemy price): Mineral Alchemy has no effect on cargo. The code's lack of chunking is correct. |  |
| 34 | Cargo via Stargate: fuel-only except IT | ✅ | ✅ | `TurnGenerator.cs:789-799`. `StargateJumpTest.cs`. **Spec-9:** spec still says a forced cargo dump runs for non-IT races while §4 says cargo "must be off-loaded first"; the code refuses the jump (`TurnGenerator.cs:869-876`). Where dumped cargo goes is unspecified. |  |
| 35 | Theft: Pick Pocket/Robber Baron cargo siphon | ❌ | N/A | Not found. |  |
| 36 | Stargate overgating damage formulas | ✅ | ✅ | `TurnGenerator.cs:801-856`. `StargateJumpTest.cs`. **Spec-9:** code-confirmed: per design (empty mass), each exceeded limit L gives survival factor (5L - x)/(4L) in 1/10,000ths truncated and multiplied in; damage% = (10,000 - product)/100; refused if d > 5R or mass > 5x a gate's limit (msgs 227/228); survivors take damage% of armor (>= 1 point); msgs 231-234. Missing here: per-step truncation, the 1-point minimum, the messages. **Implemented (spec-9 pass):** integer factors and the 1-point survivor minimum are now in; stack-count quirk deliberately not replicated. |  |
| 37 | Overgating vanish-chance + IT reduced risk | ✅ | ✅ | `TurnGenerator.cs:883-892`. **Spec-9:** **CONTRADICTS**: vanish chance = floor(combined damage / 3)% per ship, a *single* roll; **Interstellar Traveler never vanishes**; the fitted A=68 mass curve is declared wrong (it gives ~100% vanishing at the safe limit). `TurnGenerator.cs:960-980` has two independent rolls and `INTERSTELLAR_TRAVELER_VANISH_SCALE = 0.5`. The old Test tick was false: `StargateJumpTest.cs:36` says no test exercises the vanish rolls. **Implemented (spec-9 pass):** `TryStargateJump`: one roll per ship at floor(combined damage / 3)%, Interstellar Traveler never vanishes (`INTERSTELLAR_TRAVELER_VANISH_SCALE` and the A=68 curve deleted), integer survival factors, refuse beyond 5x range/mass, "any" range = 8,000 ly, survivors take at least 1 armor point, messages 231-234 style. `StargateJumpTest` (verified failing without the change). |  |
| 38 | No dynamic interception (static waypoint x/y) | ✅ | ⚠️ | `Common/Waypoints/Waypoint.cs`. |  |
| 39 | Repeat Orders flag | ❌ | N/A | Not found. |  |
| 40 | 10-entry waypoint-task table; Patrol/Route/Transfer Fleet absent | ❌ (3/10) | N/A | Confirmed still missing. |  |
| 41 | Patrol: radius-gated automatic hostile-pursuit | ❌ | N/A | Not implemented. |  |
| 42 | Route: redirect remaining orders through another object | ❌ | N/A | Not implemented. |  |
| 43 | Transfer Fleet: cross-player gift (512-cap check) | ❌ | N/A | Not implemented; the 512-fleet cap constant is defined but never enforced anywhere. |  |
| 44 | **Scrap Fleet recovery rate — now confirmed stale** | ✅ | ✅ | `ScrapTask.cs:109-131` uses 33/80/90/45%; spec now resolves the real values as 33/25/20/10/5%. **Spec-9:** **the 3/4/5/10/20 divisor reading is retracted.** Exact rule: per mineral S = sum(ship count x design cost), recovery = floor(f x S) plus the fleet's whole cargo; deep space 1/3 left as a **wreckage object** (msg 91), bare planet 1/3 (9/20 if the *planet owner* has UR), planet with a starbase 4/5 (9/10 with UR); Colonize 3/4. The code's 33/45/80/90 figures now match; **CONTRADICTS**: UR is tested on the scrapper not the planet owner, deep space returns false before clearing the fleet (no scrap, no wreckage), cargo is not added, colonists are not landed, the tech-trade roll is for the sender (`ScrapTask.cs:100-172`). **Implemented (spec-9 pass):** `ScrapTask` rewritten: rates 1/3, 9/20, 4/5, 9/10 with starbase and Ultimate Recycling taken from the **planet** (UR from the planet owner), cargo added, colonists join an own planet, deep-space scrap leaves a wreckage object, salvage roll to the planet owner. `ScrapFleetRecoveryTest`. (Arrival-triggered scraps in `ProcessFleet` still lose the wreckage.) |  |
| 45 | Ultimate Recycling: resources deferred to next turn | ✅ | ✅ | **Fixed**: see standout finding #4 - `Star.DeferredScrapResources`, credited by `StarUpdateStep` only after this turn's own production spend. `Tests/UnitTests/UltimateRecyclingDeferredResourcesTest.cs`. **Spec-9:** **REVERSED.** The UR resource credit is *not* deferred a year: the planet owner's per-planet accumulator d (ship count x design resource cost at 100%, a quarter for flagged designs) is spent in the **same** generation, blended into production as r + floor(d x r / (d + r)); the remainder is discarded and nothing carries over. `Star.DeferredScrapResources`, the end-of-`StarUpdateStep` credit (line 117) and `UltimateRecyclingDeferredResourcesTest` implement and assert the superseded behaviour (standout finding #4 is withdrawn). **Implemented (spec-9 pass):** **reversed to the spec rule**: `Star.RecycledScrapResources` (transient, zeroed each generation, never saved) is blended into production as r + floor(d x r / (d + r)); `DeferredScrapResources` and its next-turn credit are gone. `UltimateRecyclingResourcesTest` replaces the old test file. |  |
| 46 | ~~Waypoint-task feasibility / partial fulfilment with stochastic loss~~ **superseded by spec-9** | N/A | N/A | Tasks either fully succeed or fully fail. **Spec-9:** it was the overgating routine all along; merged into rows 36/37. |  |
| 47 | Split Fleet/Merge Fleets | ✅ | ⚠️ | `Common/Waypoints/SplitMergeTask.cs`. |  |
| 48 | Merge Fleets probabilistic fuel-stranding | ⚠️ | ✅ | Whole-fleet-fraction approximation, disclosed. `FleetMergeFuelShortfallTest.cs`. |  |
| 49 | Post-merge ability-stat recombination (scanner max, 12-slot sum cap) | ❌ | N/A | Not found. |  |
| 50 | Fleet creation fills smallest unused ID (gap reuse) | ❌ | N/A | Monotonic counter instead, never reuses freed IDs. |  |
| 51 | Remote Mining (fleet-based, unowned-star extraction) | ✅ | ✅ | `ServerState/TurnSteps/RemoteMiningStep.cs`. |  |
| 52 | **Fuel pass (turn step 22): +50 mg/yr per Anti-matter Generator, +200 mg/yr per Fuel Transport / Super-Fuel Transport ship when not refuelling at a friendly starbase; ramscoops play no part** | ✅ | ✅ | **New.** `Fleet.PassiveFuelGeneration`, `ShipDesign.FuelGenerationPerYear/IsFuelTransportHull`, `TurnGenerator.RegenerateFleet`. `Tests/UnitTests/PassiveFuelGenerationTest.cs`. | Supersedes the old turn-generation claim of a ramscoop flat 200. |
| 53 | **Starbase cloak in planet sweeps: detail level 3 only within `range × (100−cloak%)/100`; no Tachyon counter-cloak for starbases** | ⚠️ | ⚠️ | Starbase cloak is computed (Improved Starbases +40 raw, Super Stealth +300 raw, components) via `CloakCalculator`; the planet-sweep "planet seen, starbase hidden" level-2 state and the no-Tachyon-for-starbases rule were not re-audited. `CloakingTest.cs`. **Spec-9:** **CONTRADICTS**: starbases are `Fleet`s in `OwnedFleets`, so `ScanStep.cs:202-204` applies the Tachyon counter-cloak to them, and there is no planet-seen/starbase-hidden state; the +40 / +300 cloak points are now confirmed by the executable (fix the "back-solved" code comment). |  |
| 54 | **AR warp-acceleration colonist loss** (msg 193): C > 10 hundreds, loss floor((C + 11) x 3 / 100), every turn the fleet sets out on an ordinary-warp leg (pass 0, at least 2 waypoints, first task not Transport/Lay Mines, warp non-zero and not a Stargate leg) | ✅ | ✅ | Nothing in `UpdateFleet`/`Fleet.Move`. Now fully specified. **Implemented (spec-9 pass):** `TurnGenerator.IsSettingOutUnderAlternateReality` / `ApplyAlternateRealityWarpLoss`: once per turn at the first ordinary move, loss floor((C + 11) x 3 / 100) units when C > 10. `AlternateRealityWarpLossTest`. |  |
| 55 | **Colonize: whole-fleet dismantle at the task pass**: surface minerals gain floor(3S/4) + cargo (S = mineral cost of all ships incl. escorts), fuel/resources lost, UR not applied (msg 89); contest strength 110% (165% War Monger, 0 AR), tie kills all, winner x (largest - runner-up)/largest, minimum 1 | ✅ | ✅ | `ColonizationResolver.ApplyColonization` **overwrites** the surface stockpile with the cargo, `fleet.TotalCost.Energy = 0` is a no-op on a computed property, only the winner is dismantled, no WM/AR strength, no runner-up scaling, cancel 82 tests `Colonists != 0` instead of ownership. `ColonizationTieBreakTest` encodes the non-spec tie rule. **Implemented (spec-9 pass):** `ColoniseTask.Perform` dismantles every colonizing fleet (floor(3S/4) + cargo ADDED to the stockpile, energy/fuel lost); `ColonizationResolver` rewritten: strength 110% / 165% War Monger / 0 AR in contests, exact tie destroys all, winner x (largest - runner-up)/largest minimum 1 with the lower-index runner-up quirk; cancel when the planet has any owner. (Colonize still runs during movement, before the battle.) |  |
| 56 | Minefield type table: Standard / Heavy / Speed Bump - damage per ship 100/500/0 (125/600 variants), fleet minimum for <= 4 ships 500/2000, safe warp 4/6/5, hit chance 0.3/1.0/3.5% per warp over | ⚠️ | ❌ | `CheckForMinefields.cs`: no field types, 50 armor per token, probability `0.03 x d x delta-warp x 100` (3% per ly per warp - ten times its own comment), the "lesser of" distance inverted at line 123. The unit of the hit chance is unclear (per ly vs per turn). |  |
| 57 | A completed Stargate jump sets "saw action": no repair that turn | ✅ | ✅ | `TurnGenerator.cs:360,445` gives the moved-fleet rate. **Implemented (spec-9 pass):** `fleetsThatSawAction` in `TurnGenerator`: a completed Stargate jump gets no repair that turn. |  |
| 58 | Fleet-carried Jump Gate: if every design carries one, the jump works with no gate at the origin and the cargo dump is skipped | ❌ | ❌ | `TryStargateJump` requires `origin.GetStargate()`. |  |
| 59 | Mine sweeping by fleets and starbases (turn step 24) | ❌ | ❌ | No sweep rate, no sweeper. |  |
| 60 | Fleet terraforming / hostile un-terraforming (Orbital Adjuster, turn step 27) | ❌ | ❌ | The "Terraforming" ship property sums into designs but no pass uses it. |  |

**Summary**: 31/58 implemented, 22/61 tested. (Row 9's fuel-generation bug fixed previously; row 52's fuel pass is new this pass; row 53 is a new spec-8 row.)

---

### new-game-setup.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Two setup paths: Simplified vs Detailed wizard | ❌ | ❌ | One unified tabbed screen instead. |  |
| 2 | Galaxy Size: 5 discrete options | ❌ | ❌ | Continuous MapWidth/Height sliders instead. | Agreed, skip |
| 3 | Star Density: 4 discrete options | ❌ | ❌ | Continuous 1-100 slider instead. | Agreed, skip |
| 4 | Starting Distance setting | ❌ | ❌ | Not found. |  |
| 5 | 7 boolean game-option flags (spec-8 attributes every bit: Max Minerals 0x01, Slower Tech 0x02, Alliances 0x10, Accelerated BBS 0x20, Public Scores 0x40, No Random Events 0x80, Clumping 0x100) | ⚠️ | ⚠️ | Only "Accelerated Start" (≈Accelerated BBS Play) exists; its population effect is now spec-exact (row 38). The other six options have no setting, no UI and no effect. | Spec-8 now documents each option's exact creation- and turn-time effect (`turn-generation-engine.md` §1b). |
| 6 | "Beginner: Maximum Minerals" — every ordinary planet's three concentrations are 100 (instead of 31-119) and the extra boost roll is skipped | ❌ | ❌ | Not found (no option). | Fully specified now; a small, isolated feature. |
| 7 | "Galaxy Clumping" anti-clumping toggle | ❌ | ❌ | No relaxation pass exists to gate. |  |
| 8 | Internal "deterministic galaxy" flag | N/A | N/A | Different, intentional public seed mechanism already exists. | Agreed, skip |
| 9 | Player-slot assignment grid | ⚠️ | ⚠️ | Flat row-list instead, no open-slot/relation-linked states. | Agreed, skip |
| 10 | Victory conditions wizard page | N/A | N/A | Covered by victory-conditions.md. |  |
| 11 | Simplified-setup auto player-count scaling | ❌ | ❌ | Not found. |  |
| 12 | Simplified-setup year-gate auto-seed | ❌ | ❌ | Fixed default (50) instead. |  |
| 13 | Galaxy diameter = (size+1)×400 | ❌ | ❌ | No such formula; arbitrary ints instead. | Agreed, skip |
| 14 | Star count formula + 999 cap | ❌ | ❌ | Confirmed structurally different algorithm (continuous density-function vs. discrete formula) — pre-existing, architectural. | Agreed, skip |
| 15 | Star placement: over-generate + rejection-sample | ⚠️ | ❌ | Rejection sampling exists, different shape. |  |
| 16 | Anti-clumping relaxation pass | ❌ | ❌ | Not found. |  |
| 17 | Star naming: fixed pool of 999 + dup-prevention | ⚠️ | ❌ | Pool exists but size/dup-count not verified exact. |  |
| 18 | Home-world mineral concentration 100-299 | ✅ | ❌ | `ServerState/NewGame/StarMapInitialiser.cs:769-771` — exact match. |  |
| 19 | Ordinary planet mineral concentration 31-119 | ✅ | ✅ | **Fixed**: `StarMapInitialiser.GenerateStars()` now uses two independent 0-44 rolls plus a flat 31, matching the confirmed 31-119 range (was a flat 1-99 roll). `Tests/IntegrationTests/NewGameTest.cs`. |  |
| 20 | Surface mineral tonnage + 25% bonus flag | ⚠️ | ✅ | **Partially fixed**: ordinary planets now get starting surface tonnage (`concentration * 0-9 + 10`), previously always 0. Not implemented: the "further top-up roll if under 200" refinement (not concretely quantified by the spec) and the +25% option-flag bonus (no such game-option exists in this port yet - row 5). `Tests/IntegrationTests/NewGameTest.cs`. |  |
| 21 | Per-player starting fleet keyed by PRT | ⚠️ | ✅ | `StarMapInitialiser.cs:423-950`. Extensively tested in `Tests/IntegrationTests/NewGameTest.cs`. **Spec-9:** §5a consolidates the per-PRT starting fleet (scout pass, colonizer pass, specialist pass, ARM step); **mismatches**: HE/WM get a Laser "Armed Scout" (spec: plain Peeping Tom / X-Ray probe), PP gets 2 Mole-skin "Shielded Scouts" (spec: 1 Long Range Scout, +1 at the second planet), SS lacks Shadow Transport/Sleuth, CA has a Scout hull with 1 adjuster (spec: Mini-Miner hull with 2), JOAT has 2 plain scouts, no ARM Potato Bugs, no Construction-gated variants. |  |
| 22 | Second home planet for PP/IT on non-tiny galaxies | ⚠️ | ✅ | Implemented but without the galaxy-size gate. **Spec-9:** the IT second planet gets a second **Peeping Tom**, PP a second Long Range Scout; still no galaxy-size gate (`StarMapInitialiser.cs:829`) and no slot-0 ship at the second planet. |  |
| 23 | Batch/scripted game-creation format | ❌ | ❌ | Only simple CLI switches exist, not the full format. |  |
| 24-28 | Various binary-record/decompilation-only mechanics | N/A | N/A | Different architecture, not comparable 1:1. | skip |
| 27 | Bulk default-relation flag (**spec-8: bit 0x04 is the single-human-player flag**; when exactly one human, every slot pair gets relation value 2) | ❌ | ✅ (different mechanism) | Unconditionally sets Neutral instead. | Spec-8 corrects the flag's meaning; no behavioural difference observed here. |
| 28 | 22-entry named starting-ship template table | N/A | N/A | Rebuild hardcodes named designs per PRT branch instead. | Agreed |
| 29 | Per-PRT starting-ship list for all 10 PRTs | ⚠️ | ⚠️ | Plausible ships granted but different names/hulls than spec's table. **Spec-9:** see row 21 for the concrete per-PRT mismatches against the consolidated table. | Fix |
| 30 | Per-PRT default diplomatic relation presets | ❌ | ❌ | Everyone starts Neutral regardless of PRT. | Fix |
| 31 | Unique display-name assignment (~20 retries + fallback) | ⚠️ | ❌ | Different retry/fallback trigger shape. |  |
| 32 | Home-star selection via eligibility + nearest-to-anchor search | ⚠️ | ✅ | Much simpler pre-reserved-position approach instead. | Fix |
| 33 | Isolation-distance check | ⚠️ | ⚠️ | Belongs to ai-opponent-behavior.md; not found there either. **Spec-9:** the isolation check is now corrected (350 ly, turn > 59, rolls beyond 300/250, nearest non-owned planet, gates colony-ship *production*); `ColonizationTargetSelector.PassesLateGameDistanceCheck` implements the distances but measures candidate target to nearest *owned* colony - the reverse. |  |
| 34 | Game-name/file validation with cursor/status handling | ⚠️ | ❌ | Validates via try/catch + StatusMessage; no busy-cursor state. | Skip |
| 35 | New-game commit reuses master turn-generation routine | ✅ (different means) | ⚠️ | Structurally different but functionally analogous. | Skip |
| 36 | Save-As common file dialog | ⚠️ | ❌ | Cross-platform `PlatformHooks` abstraction instead. | Skip |
| 37 | "Reset settings to defaults + regenerate" | ❌ | ❌ | Not found. |  |
| 38 | **Accelerated BBS Play: home population × `(growth% + 5) × 2 / 10`** (4× at 15%), surface minerals +25%, concentrations < 40 get +5 | ⚠️ | ✅ | **Population fixed**: `Race.GetStartingPopulation` (was a flat 100,000). `Tests/UnitTests/AcceleratedStartPopulationTest.cs`. The +25% minerals and +5 concentration boost are not applied. | |
| 39 | **Starting tech-upgrade pass (§5b)**: each starting design's listed parts swap to the first buildable candidate (Quick Jump 5 -> Radiating Hydro-Ram Scoop / Alpha Drive 8 / Daddy Long Legs 7 / Fuel Mizer / Long Hump 6; Bat -> Possum/Mole/Rhino; Mole-skin -> Wolverine/Cow-hide; Tritanium -> Carbonic/Crobmnium; Laser -> Yakimora/X-Ray; Alpha Torpedo -> Beta; Robo-Midget/Mini -> Robo-Miner) | ❌ | ❌ | Every starting design is fixed at QJ5/Bat/Tritanium/Mole-skin/Laser/Alpha Torpedo. |  |
| 40 | **AR starbase slots**: slot 0 "Starter Colony" (Orbital Fort hull), slot 1 "Starbase" (Space Station), homeworld on slot 1 | ❌ | ❌ | Links to population-growth AR rows. |  |
| 41 | Any race with ARM and without OBRM starts with 2 Potato Bugs (Midget Miner hull) | ❌ | ❌ | `GameInitialiser.cs:319` TODO. |  |
| 42 | Planet environment generation: Gravity and Temperature 1 + U(0..89) + U(0..9) (trapezoid), Radiation 1 + U(0..98) (uniform) | ✅ | ❌ | `StarMapInitialiser.cs:108-110` uses `random.Next(1, 99)` on all three axes. **Implemented (spec-9 pass):** `StarMapInitialiser`: Radiation 1 + U(0..98), Gravity and Temperature 1 + U(0..89) + U(0..9). No distribution test. | Present in spec-8 but never given a row. |

**Summary**: 15/43 implemented, 13/43 tested. (Row 38 is new; row 5's option table is now fully specified but mostly unimplemented.)

---

### population-growth.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Per-axis tolerance → normalized clicks-from-center | ✅ | ✅ | `Race.NormalizeHabitalityDistance`, `Common/RaceDefinition/Race.cs:310-324`. |  |
| 2 | Immunity pins an axis to its best value | ✅ | ✅ | `Race.cs:312-315`. |  |
| 3 | In-band habitability combination formula | ⚠️ | ⚠️ | Only the older boxed sqrt formula implemented; spec's alternate reconstruction not distinguished by tests. **Spec-9:** the community shape is declared structurally right and the integer formula is now exact (sum of (100 - floor(100d/h))^2, 10,000 per immune axis; ideality x (3h - 2d)/(2h) per axis with 2d > h; floor(sqrt(sum/3) + 0.9) x ideality / 10,000; h is the centre-to-edge distance *on the planet's side*). Remaining gaps: whole-percent truncation, the +0.9 floor, integer ideality; `NormalizeHabitalityDistance` uses a symmetric span/2 with integer division. | Fix |
| 4 | Out-of-band malus additive across axes, each capped at 15 | ✅ | ✅ | `Race.cs:168-186`. |  |
| 5 | Single-axis malus hard-capped at 15 (not doubled for TT) | ✅ | ✅ | `Race.GetMaxMalus()` `Race.cs:283-286`. |  |
| 6 | Growth crowding formula (16/9)(1-capPct)² above 25% | ✅ | ✅ | `Star.CalculateGrowth`, `Common/GameObjects/Star.cs:340-403`. **Spec-9:** minor: `Capacity()` is a ceiling-rounded percent; the spec uses floor(pop x 1000 / cap) and the test pop > cap/4. |  |
| 7 | **Population capacity should be habValue×maxPopulation — BUG** | ✅ | ✅ | **Fixed**: `Star.Capacity(Race)` now scales `race.MaxPopulation` by `race.HabValue(this)` before dividing (still 25,000 flat for non-positive habitability, unchanged). `Tests/UnitTests/PopulationCapacityTest.cs`. |  |
| 8 | Hyper Expansion doubles growth-rate | ✅ | ⚠️ | `Global.GrowthFactorHyperExpansion=2`. |  |
| 9 | Hyper Expansion halves max population | ✅ | ❌ | `Global.PopulationFactorHyperExpansion=0.5`. |  |
| 10 | Jack of All Trades +20% population capacity | ✅ | ❌ | `Global.PopulationFactorJackOfAllTrades=1.2`. |  |
| 11 | **Inner Strength +10% pop-capacity bonus — wired to wrong trait** | ✅ | ✅ | **Fixed**: `Race.MaxPopulation` now gates the +10% bonus on `HasTrait("IS")` instead of `HasTrait("OBRM")` (renamed `Global.PopulationFactorOnlyBasicRemoteMining` → `PopulationFactorInnerStrength` accordingly). Note: this contradicts `race-traits.md`'s own row 31 ("OBRM: ... +10% max population"), which appears to have just matched the pre-existing (buggy) code rather than independently re-deriving the bit; this row's decompiled bit-9 trace is cross-confirmed by `ship-design-and-components.md`'s independent bit-9=Inner-Strength finding (the same bit gates the Bomb-category IS exclusions), so it was treated as the more authoritative source — see race-traits.md row 31, updated to flag this. `Tests/UnitTests/PopulationCapacityTest.cs`. **Spec-9:** **REVERSED.** The +10% capacity bonus is **Only Basic Remote Mining (OBRM)** - spec-9 withdraws the "Inner Strength via bit 9" reading (bit 9 of the lesser-trait word at race offset 0x4e is OBRM, confirmed by the wizard string binding; Inner Strength is a primary trait with no population bonus). `Race.MaxPopulation` (Race.cs:380), `Star.AlternateRealityCapacity` and `Global.PopulationFactorInnerStrength` all gate on IS, and `PopulationCapacityTest.cs:74-90` asserts IS gets the bonus and OBRM does not - the earlier "fix" must be reverted and the test inverted. **Implemented (spec-9 pass):** the +10% bonus is back on Only Basic Remote Mining (`Race.MaxPopulation`, `Global.PopulationFactorOnlyBasicRemoteMining`, AR table); Inner Strength has none. `PopulationCapacityTest` inverted. |  |
| 12 | **Alternate Reality: population capacity from the orbiting starbase chassis** — Orbital Fort 2,500 / Space Dock 5,000 / Space Station 10,000 / Ultra Station 20,000 / Death Star 30,000 (spec-8 extracted the table) | ✅ | ✅ | **Implemented.** `Star.AlternateRealityCapacity` feeds `CapacityColonists`; `Capacity()` and the over-full decline branch now tolerate a zero capacity. `Tests/UnitTests/AlternateRealityCapacityTest.cs` (verified to fail with the AR branch disabled). **Spec-9:** **CONTRADICTS (units and fallback).** The chassis table is in units of 100 colonists: Orbital Fort / Space Dock / Space Station / Ultra Station / Death Star = **250,000 / 500,000 / 1,000,000 / 2,000,000 / 3,000,000** colonists (+10% OBRM), live-confirmed (AR homeworld 25,000 -> 57,700). `Star.AlternateRealityCapacity` is 100x too low (so the **AR homeworld, on a 10,000 Space Station, sits at 250% and shrinks from turn 1**) and falls back to 2,500 without a starbase, where the spec says 0; status bit 0x2 means "a starbase orbits this planet". `AlternateRealityCapacityTest.cs` asserts both wrong values. **Implemented (spec-9 pass):** `Star.AlternateRealityCapacity`: 250,000 / 500,000 / 1,000,000 / 2,000,000 / 3,000,000, 0 with no starbase, +10% OBRM; the AR homeworld (Space Station) now has room to grow. `AlternateRealityCapacityTest` rewritten. | **Deviation**: the spec says a planet with *no* qualifying starbase has capacity 0; that would make every newly founded AR colony (founded without a starbase here) decline 12%/yr at once, so it gets the Orbital Fort figure (2,500) instead — see ambiguity list. |
| 13 | Negative-habitability decline shape | ⚠️ | ✅ | `Star.CalculateGrowth` matches `0.1×Colonists×habitalValue` exactly. |  |
| 14 | Decline floor + persisted fractional-carry byte | ❌ | ❌ | No fractional-carry state; truncates to nearest 100 each turn. **Spec-9:** now fully quantified: population is stored in units of 100 with a 0-99 carry byte; growth = pop x rate% x hab% / 10,000 (the 3,750 -> 3,700 example is 37.5 units, 0.5 carried). |  |
| 15 | Deterministic fractional-carry-byte growth technique | ❌ | ❌ | Neither technique implemented. |  |
| 16 | **Population declines (not plateaus) past capacity+10 — now contradicted** | ✅ | ✅ | **Fixed**: `Star.CalculateGrowth`'s full-planet branch now implements the spec's traced decline formula (`n = max(99 - capPct1000/10, -300)`, `popChange = population*n/2500`), replacing the old "plateau at 0" branch; the two existing tests that asserted the superseded plateau behavior were updated to the new decline values, and a new test covers the "capacity to capacity+9 still zero growth" window. `Tests/UnitTests/StarTest.cs`. **Spec-9:** the dead band is 10 **units** (1,000 colonists): zero change up to 999 over capacity, decline from +1,000. `CalculateGrowth` uses `Colonists < rawCapacity + 10` (colonists); `StarTest.AtCapacityPlusNine_StillProducesZeroGrowth` passes trivially. **Implemented (spec-9 pass):** dead band is 1,000 colonists. `PopulationSpec9Test`. |  |
| 17 | Global-flag growth-rate-halving quirk | N/A | N/A | Correctly not modeled — spec itself says it's an internal marker, not a real trait. **Spec-9:** the rationale changes: bit 0x4 is the duplicate/invalid-registration penalty flag (turn-generation step 7), also cutting a queued planet's resources to 4/5 - undescribed on purpose, not appropriate for a clean-room port. |  |
| 18 | No enforced minimum colonist count to found a colony | ⚠️ | ⚠️ | `ColoniseTask.IsValid` only checks nonzero cargo. **Spec-9:** `ColoniseTask.IsValid` tests `star.Colonists != 0`; the spec checks owner != unowned (see fleet-movement colonize row). |  |
| 19 | Fresh colony starts at 0% capacity, grows at growthRate×habValue | ✅ | ⚠️ | Implicit via `CalculateGrowth`'s low-capacity branch. |  |
| 20 | Homeworld: 100% habitability, 1M cap, starting resources | ✅ | ⚠️ | `StarMapInitialiser.cs` ~1026-1047. |  |
| 21 | Per-turn mined amount formula | ✅ | ✅ | `Star.GetMiningRate`/`MineForFleet`. `RemoteMiningStepTest.cs`. |  |
| 22 | Fractional kT rounded stochastically | ✅ | ⚠️ | `Global.StochasticRound` — matches exe algorithm exactly, no test exercises the random branch itself. |  |
| 23 | **Concentration depletion curve breakpoints — now confirmed wrong** | ✅ | ✅ | **Fixed**: `Star.KtToDropOnePoint` now implements the spec's decompiled 2-tier curve (`12500 / effectiveConcentration`, clamped to 25 for concentration 5-24 and 10 below 5), replacing the old community-sourced 27/462/1000/2000 figures. `Tests/UnitTests/ConcentrationDepletionCurveTest.cs`. **Spec-9:** concentrations above 100 are real (comets, homeworlds 100-299): **mining uses the raw value, depletion treats >= 101 as 100**. `KtToDropOnePoint` uses the raw value everywhere (12,500/200 = 62 kT per point where the spec gives 125). **Implemented (spec-9 pass):** `KtToDropOnePoint` clamps the depletion concentration at 100 while mining keeps the raw value. `PopulationSpec9Test`. |  |
| 24 | Mine efficiency shouldn't accelerate concentration loss | ✅ | ✅ | **Fixed**: `Star.KtToDropOnePoint` now takes the mining race's `MineProductionRate` and scales the kT-per-point threshold proportionally (`1250 * mineProductionRate / effectiveConcentration` — reduces to the unscaled formula at the baseline rate of 10), threaded through `Star.Mine`/`ApplyMining`/`MineForFleet` (now takes an explicit `mineProductionRate` parameter) and `RemoteMiningStep.cs` (looks up the mining fleet's OWNING empire's race, not the star owner's, since remote mining applies at unowned/foreign stars too). A higher-efficiency race now extracts more minerals per point of concentration lost, instead of draining the planet faster. `Tests/UnitTests/MineEfficiencyThresholdScalingTest.cs`. |  |
| 25 | Remote-mining fleet cap of 4,000 mine-equivalents | ✅ | ❌ | `Global.MaxRemoteMiningEquivalents=4000`. |  |
| 26 | Multiple mining sources deplete concentration in sequence | ✅ | ⚠️ | `ApplyMining` shares state across `Mine`/`MineForFleet`. |  |
| 27 | Mining summary popup window chrome | N/A | N/A | Original client UI detail, not a gameplay mechanic. | Agreed, skip |
| 28 | **AR: no starbase = capacity 0; a won AR colonisation installs design slot 0 "Starter Colony" (Orbital Fort hull) and posts message 11** | ✅ | ✅ | No such installation; the code fakes it with a 2,500 fallback. **Implemented (spec-9 pass):** `ServerState/StarterColony.cs` (`EnsureDesign`, `Install`); `StarMapInitialiser.PrepareDesigns` creates the AR slot-0 design; `ColonizationResolver` installs it for an AR winner. `StarterColonyTest`, `ColonizationTieBreakTest`. | The original divides by zero for an owned AR planet with no starbase (not live-tested). |
| 29 | **AR: losing the starbase in battle depopulates the planet** (owner cleared, pop 0, message 142 under 1,001 units else 141, 324 to the destroyer) | ✅ | ✅ | `ServerData.cs:626-638` only nulls `Starbase`. **Implemented (spec-9 pass):** `ServerData.CleanupFleets` -> `DepopulateAlternateRealityPlanet`: owner cleared, population 0, owner told (message 141/142/324 wording is ours; the destroyer is not told). |  |
| 30 | AR design slot 0 cannot be deleted or edited for PRT 8; a design in use cannot be deleted | ❌ | ❌ | Not found. |  |
| 31 | Colonize dismantle: surface minerals += floor(3S/4) + cargo, energy/fuel lost, UR not applied | ✅ | ✅ | See fleet-movement colonize row. **Implemented (spec-9 pass):** see fleet-movement row 55. |  |

**Summary**: 26/29 implemented, 23/29 tested. (Rows 7, 11, 16, and 23's bugs fixed across earlier passes; row 24's mine-efficiency scaling fixed in the previous pass; row 12's Alternate Reality capacity table is new this pass.)

---

### production-queue.md

**Spec-8 changed this file more than any other (+159 lines):** a full item-type catalog (§10), the
corrected Defenses/Terraform costs, the operable-count formula and the build cap, the eight queue status
codes, a live-tested meaning for auto-build "up to N", and the resolution of "Planet Rebirth" (it is the
Genesis Device). **Two of spec-7's "fixes" turned out to be built on a misreading and were reverted**
(rows 8 and 10), and the auto-build semantics this project had built were wrong (row 22).

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Population resources = floor(pop/setting) | ✅ | ⚠️ | `Common/GameObjects/Star.cs` `GetResourceRate`. |  |
| 2 | Resources per factory + build cost (factory Germanium `4 − flag`) | ✅ | ⚠️ | `Common/RaceDefinition/Race.cs:330-334`. | Tutorial-mode all-minerals `2 − flag` variant not modelled (tutorial excluded). |
| 3 | **Operable factories/mines = `min(buildCap, max(1, floor(setting × pop / 10,000)))`**, pop in units of 100; Alternate Reality 0 | ✅ | ✅ | **Corrected.** `Star.OperableBuildings` / `GetOperable{Factories,Mines}` / `GetFutureOperable*` — product before flooring (25,000 colonists operate 25 factories, not 20), floor of 1 for a populated planet, clamp to the build cap, AR gets 0. `Tests/UnitTests/BuildingCapsTest.cs`. **Spec-9:** confirmed. Note the AR "0 operable mines" also zeroes AR **mining** here, where the spec's live test shows AR mining with effective mines = sqrt(population / 100) (24 mines at 57,700 colonists). |  |
| 4 | **Build cap: max(10, setting × MAX population / 10,000)** (1,000 at 100% hab); manual Factory/Mine/Defense orders cut to `cap − built`, dropped if no room | ✅ | ✅ | **New.** `Star.GetBuildCapFactories/Mines`, `IProductionUnit.BuildCap`, `ProductionOrder.Process` manual branch. `BuildingCapsTest.cs`, `ProductionQueueAutoBuildTest.ManualOrder_*BuildCap*`. | Messages 298/303/297/185 ("orders reduced" / "deleted") are **not posted** — the queue silently trims. |
| 5 | **Auto-build clamp: `min(N, operable(next year's projected pop) − built)`** (status codes 1/2) | ✅ | ✅ | **Now spec-confirmed** (was "not spec-sourced" in v7). `IProductionUnit.SupportableCount` → `Star.GetFutureOperable*` (was current pop); Defenses gain an operable count (1 per 2,500 colonists). `ProductionQueueAutoBuildTest.AutoBuild_IsClampedToTheOperableRoom_*`. | Resolves standout finding #14's open half. |
| 6 | Mines: independent per-mineral extraction, 30% home floor | ✅ | N/A | `Star.cs` (30% floor covered by population-growth.md). |  |
| 7 | Race-design economic settings table (7 settings) | ✅ (data model) | ❌ | Fields exist; wizard-bounds enforcement is a different spec's scope. |  |
| 8 | **Defenses cost = SDI record: 15 resources + 5/5/5 kT** (Inner Strength ×3/5, integer) — **reverts v7's "44/25/48"** | ✅ | ✅ | **Re-fixed.** `DefenseProductionUnit` ctor, `Global.Defense*Cost`. v7's PRT-keyed 25/44/48 were **Mineral Packet kilotonnages** (spec-8 §5); the old "~15" community figure was right. Defenses now also *need the minerals* (`IsSkipped` checks all three). `Tests/UnitTests/DefenseAndTerraformCostTest.cs` (rewritten). | A regression this audit loop introduced in v7 and has now unwound — the earlier "FIXED" entry for finding #5 is withdrawn. |
| 9 | **Defense cap = clamp(4 × habitability %, 10, 100)**; AR 0 (was flat 100) | ✅ | ✅ | **New.** `Star.GetMaxDefenses`, `DefenseProductionUnit.IsSkipped/BuildCap`. `BuildingCapsTest.MaxDefenses_*`. **Spec-9:** confirmed and the ambiguity closed: the cap uses the owner's habitability for the planet's *current* (terraformed) environment, recomputed on every call; built defenses are never removed when it falls. `(int)(HabValue x 100)` truncates a double where the spec uses an integer percent. | "Defense *types* / best-tech upgrade" is moot: spec says upgrades are free and applied elsewhere; the queue item only increments a count. |
| 10 | **Terraform cost = 100 / 70 (Total Terraforming) / halved for Claim Adjuster** — **reverts v7's "110/70/120"** | ✅ | ✅ | **Re-fixed.** `TerraformProductionUnit` ctor, `Global.TerraformResourceCost*`. v7's 70/110/120 were single-mineral packet kilotonnages. `DefenseAndTerraformCostTest.cs`. | |
| 11 | Terraforming: 1%/unit, worst-axis-first; **Min Terraform stops once habitable, Max Terraform continues** (types 4/5/12) | ⚠️ | ⚠️ | `TerraformProductionUnit.cs`; only one Terraform item exists — no Min/Max distinction, tech-gated cap is a flat 15/30%; message 123 per step and 303 ("beyond the maximum") not posted. **Spec-9:** **CONTRADICTS (new §10k)**: the step chooser is largest habitability gain per point over the axis's whole remaining move (ties gravity, temperature, radiation); the reach is the best terraform component the owner can build measured from the *original* value, clamped 1..99, immune axes excluded; Min Terraform gets no room when value > 0 and predicted growth >= 0. `TerraformProductionUnit.SelectAxisToImprove/CheckAxis` picks the axis furthest from ideal, uses a flat 15/30 limit and does not exclude immune axes; it is also reused by Claim Adjuster. |  |
| 12 | Mineral Alchemy: 100 resources (25 with the trait) → 1 kT of each mineral; **auto Alchemy buys as many as possible (quantity forced to 1,000) every turn** | ✅ | ✅ | `AlchemyProductionUnit` ctor/`Construct`, **`AutoBuildPerTurnLimit = 1000`**. `Tests/UnitTests/AlchemyAutoBuildTest.cs`. | Report 140 ("transmuted common materials") not posted. |
| 13 | ~~Alternate Reality automatic non-queued Alchemy conversion~~ **retracted by spec-9** | N/A | N/A | **Spec-9:** AR has no automatic Mineral Alchemy: the code and a live test both rule it out (`FUN_10b8_0000` has no PRT test; AR leftovers go to research, its only route to minerals is the queued Alchemy item, priced like everyone else's). Correctly absent here. (Standout finding #15b and ambiguity 1 are closed.) | **Needs your decision** — see ambiguity list. |
| 14 | Queue is strict top-to-bottom; 8 status codes (0-4 continue, 5-7 stop) | ✅ | ✅ | `ServerState/Manufacture.cs:50-88`. | |
| 15 | Insertion pauses (doesn't erase) progress; deletion forfeits | ✅ | ⚠️ | Implicit in top-down walk. | |
| 16 | Mineral shortfall blocks ordinary items but not auto-build | ✅ | ✅ | `ProductionOrder.IsBlocking`. `ProductionQueueAutoBuildTest.cs`. | Spec: an auto entry stopped by minerals (codes 3/4) continues; partial progress is peeled into a one-unit manual entry at the queue head — this project keeps the partial on the unit instead (equivalent outcome). |
| 17 | "Leftover only" per-planet checkbox semantics | ✅ | ❌ | `StarUpdateStep.cs`. | |
| 18 | **"Planet Rebirth" = Genesis Device completion effect** (item type 13, 5,000 resources, no minerals): zero mines/factories/defenses/scanner (non-AR), zero mineral stockpiles, re-roll concentrations (25 + 2×0-39) and environment (1 + 2×0-49); message 283 to **every** player | ✅ | ✅ | **New — resolves standout finding #15b.** `GenesisDeviceProductionUnit`, `Star.ApplyGenesisDevice`, `Manufacture.ApplyGenesisDevice`, catalog entry in `ProductionViewModel.BuildCatalog` gated on `AvailableComponents.Contains("Genesis Device")`. `Tests/UnitTests/GenesisDeviceTest.cs` (effect edges, AR exemption, partial payment, XML round-trip, Manufacture + broadcast). **Spec-9:** confirmed and the ambiguity closed: each axis gets one draw written to both original and current; the three axes and the three concentrations are separate draws; the disaster queue-cleanup does **not** run for Genesis (only the comet and environment shift call it). | Environment "current and original" set to the **same** roll (spec silent). Not a random disaster — no trigger chance exists. |
| 19 | Packed queue-record format: 1,023-unit / 200-entry caps | ❌ | ❌ | Not enforced; UI's own ad hoc 1,000 clamp is coincidentally close. | |
| 20 | Full queue-item colour scheme (green/blue/red/gray) | ✅ | ✅ | `ProductionCompletionEstimator.cs`. Already confirmed in the v5 audit. | The estimator prices an auto entry as N units and now shows it idle (Gray) only when there is no room under the operable cap, not merely because N are built — consistent with spec §10h (`ProductionCompletionEstimatorTest`). |
| 21 | Manual batch add via Shift/Ctrl (×1/10/100/max) | ⚠️ | N/A | Ramping +/- stepper instead, disclosed intentional touch redesign. | |
| 22 | **Auto-build "up to N" = per-TURN maximum, not a standing total** (live-tested in spec-8 §10h) | ✅ | ✅ | **Fixed.** `ProductionOrder.Process` no longer subtracts the built count from N: "Up to 12" with 10 built keeps buying every year. The entry is never edited/removed. Only Ships keep the legacy consume-to-zero behaviour (`AutoBuildIsStandingOrder`). `ProductionQueueAutoBuildTest.cs` (6 new tests, verified to fail against the old target semantics). | The previous implementation (and its doc comments) treated N as a total target; the spec's manual wording was wrong for this build. |
| 23 | 4-slot production template manager | ❌ | ❌ | Not found. | Implement something suitable not strictly equivalent |
| 24 | Default template auto-applies to new/captured colonies (AR skips types 0-2, Claim Adjuster skips 4-5) | ❌ | ❌ | New stars get an empty queue. | Implement "favourite or default" template which gets applied |
| 25 | "(Auto Build)"/"up to N" verbatim UI wording | ⚠️ | N/A | Functionally equivalent, different wording. | Agreed, skip |
| 26 | **Mineral packets** (types 6, 14-17): accelerator requirement, launch rating, 40/100 kT delivered for 44/110 priced, overspeed classes, merge cap 16,300 kT | ❌ | ❌ | No mass-packet class or queue item exists. **Spec-9:** arrival physics are now fully specified (§10k): catch share, deposit = caught + 1/9 of uncaught, damage (S^2 - R^2) x kT / 160 scaled by defence coverage, colonist and defense losses, messages 213-218/326/385; Interstellar Traveler catch rating halved. | A whole subsystem (also turn-generation rows 11/15). |
| 27 | **Planetary scanner items** (types 18-27): build a scanner; research upgrades installed ones automatically | ⚠️ | ⚠️ | `StarUpdateStep` assigns the best available scanner automatically; there is no queue item and no "one scanner per planet" order. | Different shape: automatic rather than purchased. |
| 28 | **Starbase/ship completion rules** (category 2): obsolete-design refusal, messages 205-207, 512 fleets/race cap with merge, resources spent before the check | ⚠️ | ⚠️ | Starbase replacement and ship creation exist (`Manufacture.CreateShips`, `ManufactureStarbaseReplacementTest`, `ManufactureFreshShipArmorTest`); the 512-fleet cap, dock-capacity messages and obsolete-design refusal are absent. | |
| 29 | **Queue cleanup after a comet/environment-shift disaster** keeps auto-build entries | ❌ | ❌ | Moot until disasters exist (turn-generation row 27). | |
| 30 | **Deferred Ultimate-Recycling resources blend into production** as `output + deferred × output / (deferred + output)` | ✅ | ✅ | Implemented as a full credit into next turn's balance (`Star.DeferredScrapResources`, finding #4), not the spec's damped blend. `UltimateRecyclingDeferredResourcesTest.cs`. **Spec-9:** **REVERSED** (see fleet-movement row 45): the accumulator lives for one turn generation only, keyed on the planet owner's UR, d = ship count x design resource cost (100%), blended r + floor(d x r / (d + r)), remainder lost. `ScrapTask.Perform`/`StarUpdateStep:117` read the fleet owner, give 70% (0% without a starbase) and credit it next turn. **Implemented (spec-9 pass):** see fleet-movement row 45. The "quarter for flagged designs" and Bleeding-Edge re-costing parts are not modelled (no such flag). | Discrepancy found this pass; the blend's handling of the un-blended remainder is unclear — see ambiguity list. |
| 31 | Per-race status bit removes 20% of a planet's resource output before funding the queue | ❌ | ❌ | Not found. | Same unexplained status-bit family as turn-generation rows 24/§5. |
| 32 | Min Terraform auto entry gets 0 room only when habitability > 0 **and** predicted growth >= 0 | ❌ | ❌ | No Min/Max split. |  |
| 33 | Terraform headroom by researched terraform reach (all-axis component sets a shared reach, a better single-axis one extends its own axis); immune axes count zero; peeled partial unit re-clamped next turn | ❌ | ❌ | Flat 15/30% from the original value. |  |
| 34 | Terraform step chooser: best habitability gain per point over the axis's whole remaining move | ⚠️ | ❌ | Picks the axis furthest from ideal. |  |
| 35 | Message set 10i: 62/63/123/140/185/205-207/297/298/303/186/313 - addressed to the planet owner, dropped for computer players, 123 once per environment point | ❌ | ❌ | The queue trims silently. |  |
| 36 | Mining is credited before the queue runs | ✅ | ⚠️ | `StarUpdateStep` calls `UpdateMinerals` before `manufacture.Items`. |  |
| 37 | "Terraform Environment" catalog item appears only when headroom > 0 | ❌ | ❌ | `ProductionViewModel.BuildCatalog` always lists it. |  |
| 38 | Alchemy shortfall conversion inside the purchase routine (resources/25 or /100) and message 140 counting it | ❌ | ❌ | Trigger conditions are not described (see ambiguity list). |  |
| 39 | Partial payment uses integer percent: each component paid to floor(cost x pct / 100); stored pct = max(floor((x + 1) x 100 / c) - 1, floor(x x 100 / c)) for the limiting component | ⚠️ | ❌ | Units pay a double fraction through `Resources * double`. |  |

**Summary**: 27/38 implemented, 20/35 tested (was 13/25 and 12/25 in v7; the file grew from 25 rows to 31).
This pass: Defense/Terraform costs re-fixed (8, 10), operable formula and build cap (3, 4, 9), auto-build
semantics (5, 22), Genesis Device/"Planet Rebirth" (18), auto-Alchemy persistence (12).

---

### race-designer-ui-and-availability.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 2 | RaceDraft holds Identity/Environment/Traits/Economy/Research groups | ✅ | ❌ | `Common/RaceDefinition/Race.cs:40-67`. |  |
| 3 | Identity stage fields | ⚠️ | ❌ | No summary/separate-color field. |  |
| 4 | Six-stage wizard | ⚠️ | ❌ | Growth Rate placed on Production page, not Environment. |  |
| 5 | Earlier selections lock later controls | ❌ | ❌ | No PRT-conditional disabling found. |  |
| 6 | Non-editable context (disabled but visible) | ❌ | ❌ | No read-only mode. |  |
| 7 | Preset-archetype selector (8 presets) | ❌ | ❌ | Not found — `DefaultRaces/*.race` files exist but unwired to any picker. |  |
| 8 | "Random" archetype generator | ❌ | ❌ | Not found anywhere. |  |
| 9 | Appearance/portrait cycling with wraparound | ⚠️ | ❌ | Clickable icon grid instead of cycling control. |  |
| 10 | Primary Racial Trait: exclusive selection among 10 | ✅ | ⚠️ | `Common/RaceDefinition/PrimaryTraits.cs:64-76`. |  |
| 11 | Lesser Racial Traits: 14 checkboxes + 2 flat-cost checkboxes | ✅ | ⚠️ | `SecondaryTraits.cs:79-94`. |  |
| 12 | Research-cost-class stage (Cheap/Normal/Expensive) | ✅ | ❌ | `Nova.Avalonia/ViewModels/ResearchCostViewModel.cs:14-68`. |  |
| 13 | Immunity as full-tolerance state per axis | ✅ | ❌ | `EnvironmentToleranceViewModel.cs:72-98`. |  |
| 14 | Minimum tolerance band width of 20 | ✅ | ❌ | `EnvironmentToleranceViewModel.cs:34,48-59`. |  |
| 15 | Immunity-off resets to 20-80 default band | ✅ | ❌ | `EnvironmentToleranceViewModel.cs:74,80-94`. |  |
| 16 | Availability formula (w(x), C_middle/C_uniform) | ✅ | ❌ | `Common/RaceDefinition/WorldAvailabilityEstimator.cs:44-111` — exact structural match. |  |
| 17 | Seven economic stepper rows with matching min/max clamps | ✅ | ❌ | `RaceDesignerViewModel.cs:368-424`. |  |
| 18 | Leftover-point spending choice, 5 options | ✅ | ❌ | `RaceDesignerViewModel.cs:116-123,355-366`. |  |
| 19 | Alternate Reality variant: UI-level row-disable | ⚠️ | ❌ | Point-formula branch fully implemented; no UI-level disabling/label-swap. |  |
| 20 | Named preset economic records as data | ⚠️ | ❌ | `DefaultRaces/*.race` exist, unwired to a picker. |  |
| 21 | Stepper step-by-1/shift-for-3/auto-repeat | ❌ | ❌ | Plain `NumericUpDown` only. |  |
| 22 | Trait stage: binary traits, per-row redraw | ✅ | ❌ | `SecondaryTraitOptionViewModel.cs:14-54`. |  |
| 23 | Draft-local working copy, accept/cancel as a unit | ❌ | ❌ | Direct-mutation bindings; Cancel doesn't revert. |  |
| 24 | Research-preference editor loads stored discipline/mode | ✅ | ❌ | `ResearchViewModel.cs:162-192`. |  |
| 25 | Research-preference accept: writes only if changed | ✅ | ❌ | `ResearchViewModel.cs:324-343`. |  |
| 26 | Battle-plan editor: working copy, cancel discards | ⚠️ | ❌ | No cancel/discard path exists at all. |  |
| 27 | First plan protected, plan-count limit | ✅ | ❌ | `BattlePlansViewModel.cs:78,85-88`. |  |
| 28 | New-plan naming: template copy + "(N)" suffix wraparound | ✅ | ❌ | `BattlePlansViewModel.cs:108-172`. |  |
| 29 | Opponent/targeting policy backend semantics | ✅ | ✅ | `Tests/UnitTests/BattlePlanAttackPolicyTest.cs` (5 tests). |  |
| 30 | Opponent-selector UI for specific-target Attack category | ❌ | ❌ | `TargetId` exists on the model but is unbound in the UI. |  |
| 31 | Cross-stage persistence (commit-on-accept, discard-on-cancel) | ❌ | ❌ | Same direct-mutation pattern applies designer-wide. |  |
| 32 | Help request available from any stage | ❌ | ❌ | Not found. | Implement |

**Summary**: 22/31 implemented, 4/31 tested.

---

### race-traits.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Race Wizard advantage-point formula (full table) | ✅ | ✅ | `Common/RaceDefinition/RaceAdvantagePointCalculator.cs:1-397`. `Tests/UnitTests/RaceAdvantagePointCalculatorTest.cs`. |  |
| 2 | HE: 2x growth rate, halved max population | ✅ | ❌ | `Star.cs:345-348`, `Race.cs:372-375`. |  |
| 3 | HE: starting armed scout + colonizers, exclusive hulls | ⚠️ | ✅ | Starting fleet implemented; hull-exclusivity/no-Stargate restriction unverified. |  |
| 4 | SS: 75% cloak, +1 warp minefield safety, passive research sharing | ⚠️ | ⚠️ | **Cloak fixed this pass**: `ShipDesign.Update` now folds in SS's flat +300 raw-cloak-unit baseline (300 raw units lands exactly on `CloakCalculator`'s 75% breakpoint - see combat-resolution.md row 32), tested in `Tests/UnitTests/CloakingTest.cs`. The other two sub-mechanics (+1 warp minefield safety, passive research sharing) are still not implemented. |  |
| 5 | WM: weapons -25% cost, invasion attacker bonus | ✅/⚠️ | ❌ | Cost confirmed; bonus magnitude unverified against spec (spec gives no exact multiplier). |  |
| 6 | WM: combat movement bonus, instant design recognition, no minelayers | ✅ | ✅ | **Fixed**: `ShipDesign.Update` adds a flat +0.5 ("half-square") "Battle Movement" bonus (which also required fixing an adjacent dead branch - `SumProperty` had no case for "Battle Movement" at all, so even an installed Overthruster/Maneuvering Jet's own bonus was silently dropped before this); `ScanStep.cs` now keeps a WM empire's scanned-enemy design fully fitted out instead of the usual bare-hull-only record; `components.xml` now restricts all 10 Mine Layer components to `<WM>0</WM>` (the Defense-category SDI/Missile-Battery-only restriction was already correctly encoded in the data). `Tests/UnitTests/WarMongerTest.cs`. |  |
| 7 | CA: free/instant terraforming, +1%/yr planet drift | ✅ | ✅ | **Fixed**: `StarUpdateStep.ApplyClaimAdjusterTerraforming` automatically improves a CA star's worst environment axis by 1%/turn for free (reusing `TerraformProductionUnit`'s own worst-axis-first selection and flat 15%/30% cap, now made `public static` for reuse - the same already-disclosed simplification for "up to current tech"); `ApplyClaimAdjusterPlanetDrift` gives a separate 10%-per-year chance of one randomly-chosen axis nudging 1% toward ideal, uncapped. Not implemented: the spec's "(reverts if the planet changes hands)" parenthetical, which would need tracking CA's free terraform contribution separately from ordinary paid terraforming across every ownership-change path - disclosed gap. `Tests/UnitTests/ClaimAdjusterTest.cs`. |  |
| 8 | CA: starting Orbital Adjuster ship, Retro Bomb component | ✅ | ✅ | `StarMapInitialiser.cs:512-547`. `Tests/IntegrationTests/NewGameTest.cs`. |  |
| 9 | IS: defenses -40%, weapons +25%, invasion defender bonus; repair ×2 (spec-8) | ✅ | ⚠️ | Defense discount is now the exact `×3/5` integer formula on all four cost fields (`DefenseProductionUnit` ctor; `DefenseAndTerraformCostTest.cs`); weapons `ShipDesign.cs:723-724`; defender ×2 `InvadeTask.cs:168-171`; **repair doubling** `TurnGenerator.RegenerateFleet` (`FleetRepairRateTest.cs`). Weapon bonus and defender bonus untested. |  |
| 10 | IS: faster colonist healing, Speed Trap minefields, bomb restrictions | ⚠️ | ❌ | None found. **Spec-9:** the five IS bomb exclusions (`IS=0`) do exist in the data; Inner Strength breeding is now exact and unimplemented (new row); Speed Trap layers are open to every non-WM race here. |  |
| 11 | SD: starting mine-laying ships | ✅ | ✅ | `StarMapInitialiser.cs:549-564`. |  |
| 12 | SD: differentiated minefield decay/detonation, mines-as-scanner | ⚠️ | ⚠️ | **Decay differentiation now implemented**: `MinefieldDecayStep` uses the star-count multiplier 1 (not 4) for Space Demolition owners (`MinefieldDecayTest.Step_SpaceDemolitionOwnersFields...`). Detonation flag and mines-as-scanner remain absent. |  |
| 13 | PP: second homeworld, mass accelerator, starting tech | ⚠️ | ✅ | Starting Energy tech deliberately deviates from spec's literal figure (documented rationale in code). **Spec-9:** **internal spec contradiction**: §2/§2a/§5a say PP *starts at Energy 4*; the "RESOLVED" open question frames 24 as the correct reading of "level 13" without saying PP starts there. `GameInitialiser.cs:259` starts PP at 24 - the "deliberate deviation" is now a bug unless the spec states otherwise. |  |
| 14 | PP: packet terraform-on-arrival, penetrating scanner on packets | ❌ | ❌ | No mass-packet mechanic exists at all. |  |
| 15 | IT: second homeworld, starting destroyer+privateer, tech | ✅ | ✅ | `StarMapInitialiser.cs:354-389,619-652`. |  |
| 16 | IT: cargo-through-Stargate exemption | ✅ | ✅ | `TurnGenerator.cs:789-797`. `StargateJumpTest.cs`. |  |
| 17 | IT: Stargates -25% cost, unlimited range/mass, reduced destruction chance | ✅ | ✅ | **Cost fixed this pass**: `ShipDesign.cs` now applies IT's confirmed 25% Stargate-cost discount (component category 0x0200, Stargate subtypes only, not Mass Drivers). "Reduced destruction chance" is already implemented and tested separately - see fleet-movement-scanning-cargo.md row 37 (`TurnGenerator.cs:883-892`, `INTERSTELLAR_TRAVELER_VANISH_SCALE`). "Unlimited range/mass" has no separate enforcement to remove - a race can already build the "any/any" Stargate variant in `components.xml` regardless of PRT; not a differentiation gap so much as a design-choice availability question. `Tests/UnitTests/InterstellarTravelerAndCheapEnginesCostTest.cs`. **Spec-9:** **CONTRADICTS**: Interstellar Traveler's per-ship vanish roll is skipped entirely (damage unchanged); `TurnGenerator.cs:965-968` scales it by 0.5. **Implemented (spec-9 pass):** Interstellar Traveler skips the vanish roll entirely (damage unchanged) - fleet-movement row 37. |  |
| 18 | AR: population-on-starbases resource/scan formulas, starbase -20%; **population capacity by starbase chassis (spec-8)** | ⚠️ | ⚠️ | `Star.GetAlternateRealityResourceRate`; missing edge cases (25% floor, over-capacity half-weighting). **Capacity** now follows the chassis table (2,500 / 5,000 / 10,000 / 20,000 / 30,000) — `Star.AlternateRealityCapacity`, `AlternateRealityCapacityTest.cs`; see population-growth.md row 12 for the no-starbase deviation. **Spec-9:** AR capacity table is 100x what the code uses (population-growth row 12); resource-formula edge cases still missing (H floored at 25, E floored at 1, the 0.1/+0.999 round-up, half-weighting above capacity, minimum 1). Innate mining sqrt(pop/100) is a separate row. |  |
| 19 | AR: no planetary installations, in-transit population loss | ✅ | ✅ | **Installations now enforced**: AR gets 0 operable mines/factories/defenses and 0 build caps (`Star.OperableBuildings/GetMaxDefenses`; `BuildingCapsTest.AlternateReality_*`); landing on an owned planet destroys the colonists (`InvadeTask.IsValid`; `AlternateRealityInvasionTest.cs`); a Genesis Device leaves an AR planet's infrastructure alone. The ~3% warp-acceleration colonist loss (msg 193) is still absent. **Spec-9:** exact AR loss formula now in fleet-movement row 54 (unimplemented). **AR planets mine nothing** here (0 operable mines) where the spec needs effective mines = sqrt(population / 100). **Implemented (spec-9 pass):** installations enforced, landing rule, transit loss (fleet-movement row 54) and innate mining are all in now. |  |
| 20 | JOAT: starting tech 3 all fields, +20% max population | ✅ | ✅/❌ | Tech confirmed and tested; population multiplier untested. |  |
| 21 | JOAT: built-in penetrating scanner on 3 hull types | ❌ | ❌ | Open TODO in code. |  |
| 22 | IFE: 15% less fuel, +1 starting Propulsion | ✅ | ❌ | `ShipDesign.cs:895-898`. |  |
| 23 | TT: free/no-tech terraform from turn 1, 30% cheaper (100 → 70) | ✅ | ✅ | `TerraformProductionUnit.cs` — flat cap, not the spec's per-tech-level progression; cost is now exactly 70 (was `(int)(110 × 0.7)` = 77 after v7's mistaken 110 base). `DefenseAndTerraformCostTest.cs`. |  |
| 24 | ARM: extra mining hulls, starting Midget Miners | ⚠️ | ❌ | Starting ship is a TODO stub. **Spec-9:** the starting fleet is pinned: 2 Midget Miners ("Potato Bugs") when ARM is set and OBRM is clear; Robo-Midget/Robo-Ultra also lack `ARM=2` in the data. |  |
| 25 | ISB: extra starbase designs, -20% cost, +20% inherent cloak | ⚠️ | ⚠️ | Cost confirmed. **Cloak fixed this pass**: `ShipDesign.Update` now folds in a +40 raw-cloak-unit baseline on starbase hulls for ISB races (back-solved from `CloakCalculator`'s curve to reproduce the known 20% figure - disclosed as not decompiled the way SS's 300 is, see the code comment), replacing the old flat hardcoded `fleet.Cloaked = 20` in `ServerState/Manufacture.cs` (now removed as redundant). `Tests/UnitTests/CloakingTest.cs`. "Extra starbase designs" is still unverified. **Spec-9:** the +40 starbase cloak is now confirmed by the executable (`FUN_1048_57b6`), not back-solved; the `ShipDesign.cs:849-855` comment is stale. |  |
| 26 | UR: 90%/45% scrap recovery, deferred to next turn | ✅ | ✅ | **Fixed**: see standout finding #4 - the resources share is now correctly deferred a full turn via `Star.DeferredScrapResources`, rather than being silently destroyed the same turn it was credited. `Tests/UnitTests/UltimateRecyclingDeferredResourcesTest.cs`. **Spec-9:** **REVERSED**: scrap minerals 9/10 at a starbase (4/5 without UR), 9/20 at a bare planet (1/3 without) - tested on the **planet owner** - and the resource credit is same-generation and blended (see fleet-movement row 45). `UltimateRecyclingDeferredResourcesTest.cs` asserts the old rule. **Implemented (spec-9 pass):** planet-owner UR, 9/10, 4/5, 9/20, 1/3 and the same-generation blend (fleet-movement rows 44/45). |  |
| 27 | MA: 4x resource-to-mineral efficiency | ✅ | ❌ | `AlchemyProductionUnit.cs:60`. |  |
| 28 | GR: 50%+15%×5 split (125% aggregate) | ✅ | ❌ | `StarUpdateStep.cs:161-168`. |  |
| 29 | NRSE: removes ramscoop above Warp 4, grants Interspace-10 | ✅ | ✅ | Components exist in data; unconditional Warp-10 exemption unverified in code. **Spec-9:** NRSE is component gating only (the Warp-10 exemption sub-claim can be dropped). **BUG**: `components.xml` uses `<NRSE>` but the trait key is `"NRS"` (`AllTraits.cs:102`), so `RaceRestriction(XmlNode)` silently drops all seven NRSE restrictions: ram scoops stay buildable by NRSE races and Interspace-10 is open to everyone. **Implemented (spec-9 pass):** `<NRSE>` tags renamed `<NRS>` in `components.xml` (and the dead `Traits.Contains("NRSE")` in `GameInitialiser`); `ComponentTraitGateTest.NoRamScoopEngines_...`. |  |
| 30 | CE: engines 50% cheaper, +1 Propulsion, 10%/yr failure above Warp 6 | ✅ | ✅ | **Fixed**: `ShipDesign.cs` now applies Cheap Engines' confirmed 50% engine-cost discount (whole Engines category), alongside the already-implemented failure chance and +1 Propulsion. `Tests/UnitTests/InterstellarTravelerAndCheapEnginesCostTest.cs`. |  |
| 31 | OBRM: Mini-Miner restriction, +10% max population | ✅ | ✅ | Hull restriction unverified (data-only). **Correction**: the +10% max-population claim here does not hold up against `population-growth.md`'s own decompiled bit-level trace, which ties that exact bonus to Inner Strength (bit 9) instead — independently cross-confirmed by `ship-design-and-components.md`'s bit-9=IS finding via the Bomb-category exclusions. This row's original claim likely just matched the pre-existing code (which had the same bug) rather than independently re-deriving the bit. The bonus has been moved to Inner Strength in code; see `population-growth.md` row 11. **Spec-9:** **REVERSED (see population-growth row 11)**: the +10% capacity bonus is OBRM's and the Inner Strength reading is withdrawn; the code gates it on IS. The OBRM/ARM robot restrictions are also missing from `components.xml` (empty `<Race_Restrictions/>` on every robot). **Implemented (spec-9 pass):** bonus on OBRM; robot and mining-hull gates added (`OBRM=0` on Robo-Midget/Miner/Maxi/Super/Ultra and the Midget/Miner/Ultra hulls, `ARM=2` on Robo-Midget/Ultra). |  |
| 32 | NAS: no pen-scanners, doubles conventional range | ✅ | ❌ | `ShipDesign.cs:749-756`. |  |
| 33 | LSP: 30% lower starting population | ✅ | ❌ | `Race.cs:404-407`. |  |
| 34 | **BET: 2x cost on unmet prereqs, differentiated miniaturization** | ✅ | ✅ | **Fixed**: see research-tech-tree.md row 11/12 - `ShipDesign.ApplyMiniaturizationAndBleedingEdge`. `Tests/UnitTests/MiniaturizationAndBleedingEdgeTechnologyTest.cs`. |  |
| 35 | **RS: shields +40% & 10%/round regen, armor 50% effective** | ✅ | ✅ | **Fixed**: `ShipDesign.Update` applies the +40%/-50% design-level multipliers; `BattleEngine.ApplyRegeneratingShields` restores 10% of MAXIMUM shields (not current) once per battle round. Implementing this surfaced and fixed an adjacent real bug: `ShipToken`'s constructor set `Shields` to a single ship's rating instead of the whole token's total, unlike `Armor` (which already scaled by `Quantity`) - every consumer (`BattleEngine`'s damage/attractiveness math, `TurnGenerator`'s per-turn recharge step) already assumed the totalled convention, so a freshly-built or just-split/merged multi-ship token understated its own total shields until the next turn's recharge step ran, parallel to the already-fixed "fresh ship shows 0 armor" bug. `Tests/UnitTests/RegeneratingShieldsTest.cs`. **Spec-9:** code-exact: shields + floor(2/5 x total) capped at 65,535; the armor penalty halves **only Armor-category parts** per slot (not hull base armor, Croby Sharmor, Langston Shell, Multi Cargo Pod); regeneration from round 2, only for tokens with shields above 0. `ShipDesign.cs:895-898` halves everything and `ApplyRegeneratingShields:628` regenerates zero-shield tokens; `RegeneratingShieldsTest.cs:54-61` locks the hull-armor halving in. **Implemented (spec-9 pass):** see combat rows 38/39. |  |
| 36 | PRT-specific starting tech-level table | ✅ | ✅ | `GameInitialiser.cs:208-288`. |  |
| 37 | "All Expensive fields start Tech 3/4" checkbox | ✅ | ✅ | `GameInitialiser.cs:375-390`. |  |
| 38 | Ship-cost modifiers (WM/IS/CA/CE/IT) | ✅ | ✅ | WM/IS/CA/CE/IT's own discounts covered elsewhere; the empire-wide Miniaturization/BET cost adjustment (research-tech-tree.md row 12) is now implemented too. |  |
| 39 | **IS: in-fleet colonist breeding** floor(R x C / 200), 1/3 chance of +1 when zero, overflow to an own orbited planet else lost | ✅ | ✅ | Nothing in the code (fleet-movement row 32). **Implemented (spec-9 pass):** see fleet-movement row 32. |  |
| 40 | **AR: innate mining** - effective mines = sqrt(population in hundreds) (`FUN_1048_4cce`) | ✅ | ✅ | `GetMiningRate` uses `GetMinesInUse`, which is 0 for AR. **Implemented (spec-9 pass):** `Star.AlternateRealityInnateMines` feeds `GetMinesInUse`/`GetFutureMiningRate`: floor(sqrt(pop / 100)), minimum 1 (24 mines at 57,700 colonists). `AlternateRealityCapacityTest`. | Rounding of the square root and whether the mine-production setting applies are unspecified. |
| 41 | **AR: a new colony receives the "Starter Colony" starbase; the homeworld starts with a Space Station** | ✅ | ✅ | See population-growth AR rows. **Implemented (spec-9 pass):** see population-growth rows 30/31. |  |
| 42 | Lesser-trait bit numbering (0 IFE, 1 TT, 2 ARM, 3 ISB, 4 GR, 5 UR, 6 MA, 7 NRSE, 8 CE, 9 OBRM, 10 NAS, 11 LSP, 12 BET, 13 RS; no lesser trait is Inner Strength) | ✅ | ⚠️ | `SecondaryTraits`/`AllTraits.TraitKeys` carry the same set (OBRM listed before CE - display order only). |  |

**Summary**: 33/42 implemented, 33/42 tested. (Spec-8 pass: rows 9, 12, 18, 19 gained implementation and/or tests — IS defense/repair, SD minefield decay, AR capacity and installations.) (Rows 17 and 30's Stargate/engine cost discounts, and rows 4/25's cloak sub-mechanics, fixed previously; row 26's Ultimate Recycling deferral, and rows 6/7/34/35/38's War Monger/Claim Adjuster/BET/Regenerating Shields mechanics, fixed this pass.)

---

### research-tech-tree.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Six tech fields, levels 0-26 | ✅ | ❌ | `Common/DataStructures/TechLevel.cs:45-55`. |  |
| 2 | Colony resource formula | ✅ | ❌ | `Common/GameObjects/Star.cs:214-219`. |  |
| 3 | Alternate Reality resource-from-Energy special case | ✅ | ⚠️ | `Star.cs:209-212`. |  |
| 4 | Research is 1:1 resource-to-progress | ✅ | ❌ | `ServerState/TurnSteps/StarUpdateStep.cs:112-174`. |  |
| 5 | Empire-wide research percentage setting | ✅ | ❌ | `Common/DataStructures/EmpireData.cs:75`. |  |
| 6 | Per-planet "leftover only" checkbox | ✅ | ❌ | `Star.cs:423-433`. |  |
| 7 | Cost curve (base + totalLevels×10 surcharge + costFactor) | ✅ | ✅ | `Common/Research.cs:42-70` — exact match. **Tested this pass**: `Tests/UnitTests/ResearchCostTest.cs` pins down each term (base cost at zero surcharge, the totalLevels×10 surcharge summed across all six fields, and per-field cost-factor scaling) independently. |  |
| 8 | **Slow Tech Advance global cost doubling** | ❌ | ❌ | Not implemented; no such setting exists. **Spec-9:** still unimplemented; no `SlowTech` symbol exists. |  |
| 9 | Race-design per-field cost setting | ✅ | ⚠️ | `Common/RaceDefinition/Race.cs:44`. |  |
| 10 | ExtraTech: flat starting level for "expensive" fields | ✅ | ⚠️ | `ServerState/NewGame/GameInitialiser.cs:375-389`. |  |
| 11 | **Bleeding Edge Technology (2x cost until ahead of prereqs)** | ✅ | ✅ | **Fixed**: `Common/Components/ShipDesign.cs`'s `ApplyMiniaturizationAndBleedingEdge` doubles a component's cost while the empire's current tech level hasn't yet exceeded that component's requirement by at least one level in EVERY required field (the same per-field minimum-surplus check row 12's discount uses - doubling fires exactly when that discount computes to 0%). Required threading the empire's current `TechLevel` through a new `ShipDesign.Update(Race, TechLevel)` overload, since (unlike every other cost modifier) this needs live tech state, not just the race. `Tests/UnitTests/MiniaturizationAndBleedingEdgeTechnologyTest.cs`. **Spec-9:** confirmed: 4% per level up to 75% (5% up to 80% with BET), from the smallest margin over any nonzero requirement; not applied to terraforming or planetary scanners/defenses (they bypass the per-component loop in `ShipDesign.Update`). Hull cost is not miniaturized - the spec is silent. |  |
| 12 | **Miniaturization (4%/level reduction, 75% cap; 5%/80% with BET)** | ✅ | ✅ | **Fixed**: same method as row 11 - for each tech field a component actually requires, takes the empire's minimum surplus (current level minus requirement) across all of them, then discounts cost 4%/level (5% for BET) up to a 75% (80% for BET) cap. A component with no tech requirement in any field is untouched by either mechanic. `Tests/UnitTests/MiniaturizationAndBleedingEdgeTechnologyTest.cs`. |  |
| 13 | Single "one-hot" research target field | ✅ | ❌ | `StarUpdateStep.cs:147-159`. |  |
| 14 | "Lowest field" auto-research-target option | ❌ | ❌ | Open TODO comment. |  |
| 15 | Generalized Research 50%/15%×5 split | ✅ | ⚠️ | `StarUpdateStep.cs:161-174`. |  |
| 16 | Super Stealth passive research bonus | ❌ | ❌ | Not found. **Spec-9:** still unimplemented; nothing in `ServerState` checks Super Stealth. |  |
| 17 | Research progress carryover across turns | ✅ | ⚠️ | `StarUpdateStep.cs:181-205`. |  |
| 18 | Multiple levels bought in one field in one turn | ✅ | ❌ | `StarUpdateStep.cs:181-205` (buy-loop). |  |
| 19 | totalLevels re-evaluated live within a turn | ✅ | ❌ | `Research.Cost` reads fresh every call. |  |
| 20 | Tech trading via scrapping | ✅ | ❌ | `ScrapTask.cs:148`. |  |
| 21 | Tech trading via battle kill | ✅ | ❌ | `BattleEngine.cs:1582`. |  |
| 22 | Tech trading via invasion | ✅ | ❌ | `InvadeTask.cs:231`. |  |
| 23 | Tech-gain probability formula | ✅ | ❌ | `Common/TechTrading.cs:88-120` — exact match. |  |
| 24 | One tech level gained per empire per turn, across sources | ✅ | ❌ | `TechTrading.cs:90-93`. |  |
| 25 | Turn order: scrap → WP0 invasion → battle → WP1 invasion | ⚠️ | ❌ | No WP0/WP1 split around battle exists — open TODO. |  |
| 26 | PRT-conditional exclusion from research-field list | ❌ | ❌ | Not found; all 6 fields always listed. |  |
| 27-28 | Starbase upgrade discounts | N/A | N/A | Out of this file's scope (production-queue/ship-design concern). |  |
| 29 | **"Turns until next level" forecast — GR bug** | ✅ | ⚠️ | **Fixed**: `Nova.Avalonia/ViewModels/Panels/ResearchViewModel.cs`'s `RefreshPreview` now divides outstanding cost by `Research.TargetFieldContributionFraction(race)`'s effective rate (0.5× for GR, matching `StarUpdateStep.ContributeResearch`'s split; the displayed `BudgetedEnergy` total itself is correctly left unscaled, since that figure is the whole per-turn contribution, just not all of it landing on the one target field). `Tests/UnitTests/ResearchCostTest.cs`'s `TargetFieldContributionFractionTest` covers the shared fraction helper directly; the `ResearchViewModel` wiring itself has no test (`Tests.csproj` doesn't reference `Nova.Avalonia.csproj`, and adding that dependency for one method was judged out of scope for this fix). |  |
| 30 | Component tech-clamp "4-level lookahead" window | ❌ | ❌ | Only exact `<` gating exists, no lookahead. **Spec-9:** availability is a **plain per-field minimum** (the resolved open question) and miniaturization never moves the point at which an item becomes available; `RaceComponents.cs:97` and `TechLevel.operator<` already behave so. **The spec contradicts itself**: §3 and §7 still describe a 4-level window. |  |
| 31 | Hull/component prerequisite table accuracy | ✅ | ✅ | Data-file audit, not independently re-verifiable (source docs no longer in-repo). **Spec-9:** the full 239-record prerequisite table is now exe-verified (six bytes at +2..+7, order E W P C El B; highest requirement 26; no trait changes a requirement). A scripted comparison of `components.xml` against it found all 238 components match **except Gravity ±15 (Biotech 3 in the data, exe 4) and Gravity Terraform ±11 (missing: P10/B3)**, so research jumps from ±7 to ±15. **Implemented (spec-9 pass):** Gravity Terraform ±11 added (P10/B3) and Gravity ±15 set to Biotech 4 in `components.xml`. `ComponentTraitGateTest.GravityTerraformPlusMinus11_...`. | Agreed, skip |
| 32 | **Trait/gift gates run before the tech check and never replace it** | ✅ | ✅ | **BUG**: `StarUpdateStep.TechLevelUp` (~:293-305) adds every component whose requirement was just crossed straight to `AvailableComponents` with no `Restrictions` check, and `RaceComponents.Add` has none - so after any advance a race gets trait-exclusive parts (Space Dock without ISB, Death Star without AR, ram scoops for NRSE). **Implemented (spec-9 pass):** `RaceComponents.IsRestrictedFor` is now consulted by `StarUpdateStep.TechLevelUp` as well as the initial determination. `ComponentTraitGateTest.TechLevelUp_DoesNotHandOutAComponentTheRacesTraitsBar`. | Reuse the `RaceComponents` restriction loop. |
| 33 | **NRSE tag mismatch**: `components.xml` `<NRSE>` vs trait key `"NRS"` | ✅ | ✅ | Seven NRSE restrictions silently dropped (`RaceRestriction(XmlNode)` matches only `TraitKeys`). The earlier ship-design row 7 claim "NRSE=0 verified" was false. **Implemented (spec-9 pass):** tags renamed; Interspace 10 is NRS-only and ram scoops are NRS-barred. |  |
| 34 | Graded tech-distance result: available / one level away / gap + 1 only when exactly one field is short and it is the field under research; otherwise a fixed "far away" | ⚠️ | ❌ | `ResearchViewModel.BuildBenefits` grades by levels away in the target field (1 / 2-4 / 5+) but iterates `AllComponents`, listing trait-barred and ungifted items. |  |

**Summary**: 23/35 implemented, 8/35 tested. (Row 29's Generalized Research forecast bug fixed, and row 7 gained real dedicated tests, earlier this session; rows 11/12's Bleeding Edge Technology and Miniaturization fixed this pass.)

---

### save-turn-file-format.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1-4, 6, 7, 9, 12-14, 20-21 | Binary wire-format/opcode/checksum mechanics | N/A | N/A | Deliberate XML format divergence — not applicable. | Agreed, skip |
| 3 | **Design slot caps: 16 hull + 10 starbase** | ❌ | ❌ | Constants exist (`GlobalDefinitions.cs:152-153`) but never enforced anywhere. | Skip |
| 5 | **Name fields capped at 31 characters** | ❌ | ❌ | No cap found anywhere. | Skip |
| 8 | **Score-history retained for 100 turns** | ❌ | ❌ | Only current-turn snapshot computed, never retained. | Implement |
| 10 | **Incoming record replay: per-record fault isolation, not all-or-nothing** | ✅ | ✅ | **Fixed**: `OrderReader.cs:114-160`'s per-node parsing now has its own inner try/catch, so one malformed order only costs that order — the rest of the file's commands (and the empire's `TurnSubmitted` flag) still land. `Tests/UnitTests/OrderReaderFaultIsolationTest.cs`. | Fix |
| 11 | Full state-dump per-race snapshot | ⚠️ | ⚠️ | `IntelWriter.cs:61-136` — XML equivalent exists; score-history component absent (see #8). | Agreed, sufficient |
| 15 | **Orders accepted only for the host's current turn / game / player; stale or foreign files silently skipped; failed opens retried up to ~4 s** (spec-8 §10.6, turn-generation §1a) | ✅ | ⚠️ | `OrderReader.ReadPlayerTurn`: a file whose `Turn` differs from the host's, or whose `Id` is another empire's, is skipped silently; the locked-file retry loop exists. Exercised indirectly by `OrderReaderFaultIsolationTest`. | The original also rejects a *newer* turn with an error and checks a per-generation nonce; neither applies to XML orders. |
| 16 | **Header record, file-kind byte, version range, per-generation nonce and the keystream cipher** (spec-8 §10: there is no compression — every payload except types 0/8 is XORed with a keystream seeded from public header fields) | N/A | N/A | Deliberate XML format divergence. | Agreed, skip |
| 17 | Score-history record (opcode 0x2d, 24 bytes/race) written per player under the own-race / game-over / eliminated / Public-Scores-after-turn-19 rule | ❌ | ❌ | Not found (same gap as row 8). | |

**Summary**: 3/16 implemented, 3/16 tested (rows 15 and 17 are new in spec-8, row 16 is N/A; spec-8 §10 is almost entirely binary-format detail). Row 10's order-file fault-isolation bug was fixed previously.

---

### ship-design-and-components.md (+ component-stats.tsv)

*Re-audited after the user's hand-edit of the spec file; this replaces the original 12-row + separate tsv-cross-check pass below.*

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Per-race design slot addressing: 16 hull-design + 10 starbase-design slots (§1) | ❌ | ❌ | `Common/DataStructures/EmpireData.cs:85` stores designs in an unbounded `Dictionary<long, ShipDesign>`. `Common/GlobalDefinitions.cs:169` defines `MaxDesignsAmount = 16` but it is never referenced anywhere else in the codebase (`grep` finds only the declaration) — dead constant, no 16/10 cap, no hull/starbase split enforced. | Skip - allows as many as like |
| 2 | Per-design installed-component slot count is hull-dependent, 2–16 (not a fixed number) (§2) | ✅ | ❌ | `Common/Components/Hull.cs`'s `Modules` list and `Common/Components/HullModule.cs` give each hull its own module list read from `components.xml`; slot counts vary per hull (e.g. Scout=3, Battleship=11, Nubian=13) matching the spec's corrected 2–16 range. No dedicated unit test found for slot-count-by-hull. |  |
| 3 | Slots filled sequentially via a per-race "next free slot" counter, hard-capped at index 15 (§2) | ❌ | ❌ | No equivalent counter/invariant found; `HullModule.AllocatedComponent` is set/cleared directly by index (`HullModule.cs:113-119` `Empty()`), UI can populate any slot in any order. | Skip |
| 4 | Removing a slot from an already-in-use design: retroactive fleet-consistency confirmation check (§3) | ❌ | ❌ | No code found (`grep` for "already built"/"confirm remove"/slot-removal fleet scan across the whole repo) — editing a saved design's component list does not check existing fleets built to that design. |  |
| 5 | Category/subtype resolver: 16 fixed component categories with per-category subtype tables (§4) | ✅ | ❌ | `components.xml`'s per-`<Item><Type>` grouping (Engine, Scanner, Shield, Armor, BeamWeapons, Torpedoes, Bomb, MiningRobot, MineLayer, Orbital, Hull, Electrical, Mechanical, Terraforming, PlanetaryInstallations = 15 buckets, Hull further splits into regular hulls vs starbase chassis) is the functional analogue of the spec's 16 category bitmask, though implemented as a string-typed `Type`/`Component.Type` enum rather than a bit-per-category word. | Agreed, skip |
| 6 | A slot's "allowed types" = bitwise-OR of category bits (multi-family slots) (§4, confirmed §15e) | ✅ | ❌ | `HullModule.ComponentType` stores compound strings like `"Scan+Shld+Arm+Beam+Torp+MLay+Elec+Mech"`-style unions (e.g. `Common/Components/ShipDesign.cs:706-736` iterates `Hull.Modules` and matches `AllocatedComponent.Type` against `module.ComponentType`), matching the spec's confirmed per-slot union-of-categories model. | Agreed, skip |
| 7 | Per-subtype PRT-exclusivity / boolean-trait gating embedded in the resolver (Engine, Bomb examples) (§5) | ✅ | ✅ | `Common/RaceDefinition/RaceRestriction.cs` + `Common/Components/RaceComponents.cs:96-118` implement this generically via a per-component `Race_Restrictions` dictionary (`required`/`not_available`/`not_required`). Verified correct for **all 6** Engine restrictions (Settler's Delight `HE=2`; all 5 present ram-scoop engines `NRSE=0`; Fuel Mizer/Galaxy Scoop `IFE=2`) and for Claim Adjuster's Retro Bomb (`CA=2`). **Fixed**: Smart Bomb, Neutron Bomb, and Enriched Neutron Bomb now also carry `IS=0` in `components.xml` (previously only Peerless/Annihilator did), completing all 5 of the spec's IS-excluded bomb types. `Tests/UnitTests/ComponentDataFixesTest.cs`. **Spec-9:** spec-9 pins the full gate table by tsv idx. **Contradictions in `components.xml`**: all mining robots have empty restrictions (need OBRM=0 on Robo-Midget/Miner/Maxi/Super/Ultra, ARM=2 on Robo-Midget and Robo-Ultra); Mine Dispenser 40/80/130 and Speed Trap 30/50 carry only `WM=0` (need `SD=2`; Speed Trap 20 needs SD **or** IS - the restriction model cannot express an OR); Energy Dampener has no restriction (needs SD=2); Midget/Miner/Ultra hulls lack OBRM=0; the NRSE tags are silently dropped (research row 33). Correct already: engines, scanners (NAS), Space Dock/Ultra Station ISB=2, Death Star AR=2, Total ±N TT=2, the five IS bombs, Retro CA. **Implemented (spec-9 pass):** robots, mining hulls, mine layers (Mine Dispenser 40/80/130, Speed Trap 30/50 now SD-only) and Energy Dampener (SD) fixed; `ComponentTraitGateTest`. **Still open**: Speed Trap 20 needs SD **or** IS (the restriction model has no OR gate). |  |
| 8 | Generic race-trait ability-bitmask gate — now identified as a one-time battle/event component-grant tracker for exactly 12 named components (§6, §14a) | ⚠️ | ✅ | **Fixed (faithful implementation, per your direction)**: all 12 named components (Multi Cargo Pod, Multi Function Pod, Langston Shell, Mega Poly Shell, Alien Miner, Hush-a-Boom, Anti Matter Torpedo, Multi Contained Munition, Mini Morph, Enigma Pulsar, Genesis Device, Jump Gate) added to `components.xml` with real stats from `component-stats.tsv`. A real one-time-per-race grant mechanism now exists: `EmpireData.GrantedSpecialComponents` (a persisted per-empire set, replacing the original's raw bitmask 1:1 in effect), `SpecialComponentGrants` (the ordered 12-name registry), and `BattleEngine.GrantOneTimeSpecialComponent` (a new method alongside the pre-existing, separate `GrantBattleTechGains` enemy-wreckage-study mechanic - every battle-surviving race gets a roughly-50% roll for one of its still-ungranted specials). `RaceComponents.DetermineRaceComponents` and `StarUpdateStep.TechLevelUp` both gate these 12 behind "granted AND tech-sufficient," never tech alone. **Disclosed simplifications**: the spec's own "13th roll slot" (a bare tech bump with no component, per its own hedged "most plausibly" framing) and the exact weighting of a documented "second weighted roll" are both left out, since neither is concretely quantified - a single ~50% roll picks uniformly from the still-ungranted pool instead, preserving the "never twice, per-race, roughly even odds" shape without fabricating specific unconfirmed numbers. A few individual components' secondary flavor stats (e.g. Multi Function Pod's exact electrical sub-family, Jump Gate's and Genesis Device's real gameplay effect) aren't spec-confirmed either and are left as inert data pending a future mechanic. `Tests/UnitTests/SpecialComponentGrantTest.cs`, `Tests/UnitTests/ComponentDataFixesTest.cs`. **Spec-9:** salvage never feeds bits 8 (Mini Morph), 10 (Genesis Device) or 12; only the Mystery Trader reaches them, and bit 12 is the Trader's "auxiliary ships" gift. **CONTRADICTS**: `BattleEngine.GrantOneTimeSpecialComponent` draws uniformly from all 12, so battles can grant Mini Morph and Genesis Device (removing them makes both unobtainable until the Trader exists). |  |
| 9 | Per-design aggregate-stats accumulation (percentage "value" multiplier, compounding mass/fuel multipliers) (§7) | ⚠️ | ⚠️ | `Common/Components/ShipDesign.cs:629-757` (`Update`/`SumProperty`) accumulates armor, cost, shield, cargo etc. by direct summation/scaling rather than the decompiled game's base-10000/base-1000 percentage-multiplier-and-rescale model; functionally covers the same aggregate stats (armor, mass, cost, fuel) via a materially simpler, additive algorithm. Indirectly exercised by `Tests/UnitTests/ManufactureFreshShipArmorTest.cs` and battle tests that call `Update()` transitively, but no test isolates `SumProperty`'s accumulation logic itself. |  |
| 10 | Min/max weapon-range and weapon-initiative brackets tracked per design, consumed by the battle engine's firing dispatcher to skip empty brackets (§7) | ❌ | ❌ | `Common/Components/Weapon.cs:56-57` stores `Range`/`Initiative` per individual weapon stack; `ShipDesign.Weapons` is a flat `List<Weapon>` (`ShipDesign.cs:51`, `833-836`) with no packed min/max bracket computed at the design level, and no such bracket lookup exists in `ServerState/BattleEngine.cs`. |  |
| 11 | Combined armor+shield components' secondary stat special-cases (Langston Shell 95%, Mega Poly Shell 80% defense multiplier; Croby Sharmor +65, Fielded Kelarium +50 flat points) (§7) | ❌ | ❌ | No special-casing by component identity found anywhere in `Common/Components/*.cs` or `ServerState/*.cs`; moot for 2 of the 4 named components since **Langston Shell and Mega Poly Shell don't exist in `components.xml` at all** (see rows 28/29). **Spec-9:** Langston Shell's +65 armor is also missing from `components.xml` (Shield 125 only). |  |
| 12 | AI/production design costing: cost/tier comparator + race-trait-gated cost estimator (§8) | ⚠️ | ❌ | No dedicated design cost/tier comparator found in `Nova.Ai/`. A narrower, differently-scoped mechanic exists: `ShipDesign.cs:717-727` applies War Monger (-25%) / Inner Strength (+25%) to weapon costs, and `ShipDesign.cs:740-743` applies Improved Starbases/Alternate Reality (-20%) to starbase costs — this matches `race-traits.md`'s §7 cost-modifier description the spec itself says is a *different* mechanism from §8's estimator, not §8 itself. |  |
| 13 | Cached per-design fields in the record tail `+0x87`-`+0x92` (spec-8 §9 rewrite: ships hold a weapon value, starbases a squared cloak factor `(100−cloak%)²`, ships also cache scanner ranges/counter-cloak) | ❌ | ❌ | N/A to this architecture — `ShipDesign.Summary` is always recomputed live via `Update()` (`ShipDesign.cs:629`); there is no separate "cached" vs "recomputed" dual path. The *behaviour* the starbase cloak factor drives is covered by fleet-movement row 53. | Agreed, skip |
| 14 | Turn-order delta-record protocol, opcodes `0x1b`/`0x1e` for design create/delete and slot writes (§10) | ❌ | ❌ | N/A — this codebase has no binary turn-file wire format; orders are serialized as XML (`ServerState/Persistence/OrderReader.cs`), so this mechanic has no analogue to implement. | Agreed, skip |
| 15 | 36-byte per-race/per-slot UI display-label cache (§12) | ❌ | ❌ | N/A — `HullModule.AllocatedComponent` (`HullModule.cs:41`) holds a direct object reference to the `Component`, so no separate name/quantity cache is needed in this architecture; nothing to port. | Agreed, skip |
| 16 | Unified hull-type definition table spanning regular ship hulls + starbase chassis under one index (§13) | ✅ | ❌ | `Common/Components/Hull.cs:155-166` (`IsStarbase => FuelCapacity == 0`, `CanRefuel`) and `components.xml`'s shared `Type=Hull` bucket (36 entries, both ship hulls and the 5 starbase chassis) match the spec's "one logical 37-entry table" finding conceptually. |  |
| 17 | Universal component record layout: 6 tech-level prerequisites, name, mass, 4-way resource cost (§15b) | ✅ | ❌ | `components.xml`'s `<Tech>` block (Energy/Weapons/Propulsion/Construction/Electronics/Biotechnology) and `<Cost>` block (Boranium/Ironium/Germanium/Energy=resources) match the spec's recovered universal layout field-for-field; spot-checked exactly against `component-stats.tsv` for Tritanium, Superlatanium, Mole-skin Shield, Trans-Galactic Drive, Alpha Drive 8, Neutronium, Valanium, Meta Morph, Settler's Delight, Retro Bomb — all match precisely. |  |
| 18 | Tech-level-shortfall check is graded (fully-met / one-field-short / further-away), not a plain boolean (§11 closing, `client-ui-dialog-catalog.md` cross-ref) | ❌ | ❌ | `Common/Components/RaceComponents.cs:86-90`: `if (tech < component.RequiredTech) { continue; }` — a component is either fully included or fully excluded from `RaceComponents`; no graded "how far short" result is computed or surfaced anywhere. **Spec-9:** the two near-miss grades apply only when the single short field is the one currently being researched. |  |
| 19 | Ship-scanner range combination = 4th root of the sum of 4th powers across a design's scanners (§15c) | ✅ | ❌ | `Common/Components/Scanner.cs:107-129` (`operator+`, `operator*`) implement exactly `Math.Pow(Math.Pow(a,4)+Math.Pow(b,4), 0.25)`, matching the spec's confirmed formula precisely, including citing "Manual section 9-7" in the class's own comment. No unit test exercises this formula directly. |  |
| 20 | No Advanced Scanners doubles conventional scanner range (§15c cross-ref) | ✅ | ❌ | `ShipDesign.cs:745-756`: `if (race.HasTrait("NAS") ...) scanner.NormalScan *= 2;`. |  |
| 21 | Per-hull slot layout: allowed-category mask + per-slot capacity + slot count varying 2–16 per hull, e.g. Meta Morph's 3/8/2/2/2/2/1 (§15e) | ✅ | ❌ | `HullModule.ComponentMaximum`/`ComponentType`/`CellNumber` (`HullModule.cs:42-44`) store exactly this per-slot data from `components.xml`; Meta Morph's entry (`components.xml:3430-3509`) has modules with capacities {2,2,3(Engine),8,1(Base Cargo pseudo-slot),1,2,2} — the 7 real equipment slots' capacity multiset {3,8,2,2,2,2,1} matches the spec's corrected "3/8/2/2/1/2/2" exactly once the non-equipment "Base Cargo" pseudo-module is excluded. |  |
| 22 | `component-stats.tsv` cross-check — Stargates/Mass Drivers (`0x0200`, 16 entries) | ✅ | ✅ | **Fixed**: all 16 entries' resource+mineral costs doubled in `components.xml` to match the tsv ground truth (e.g. Stargate 100/250: now Energy=400/Ironium=100/Boranium=40/Germanium=40). This was precisely the discrepancy the spec's §15c flags and resolves ("the game's own stored values are the real ones; commonly-published/wiki figures are already-halved") — `components.xml` had been sourced from the halved wiki figures for this entire category. `Tests/UnitTests/ComponentDataFixesTest.cs`. |  |
| 23 | `component-stats.tsv` cross-check — "Super Drvier 7" naming/tech bug | ✅ | ✅ | **Fixed**: renamed to "Mass Driver 7" (was "Super Drvier 7" — wrong family name plus a typo) and its Energy tech level corrected from 7 to 9, matching every other driver in the family and tsv idx9. `Tests/UnitTests/ComponentDataFixesTest.cs`. |  |
| 24 | `component-stats.tsv` cross-check — Starbase chassis (`0x0400`, 5 entries) | ✅ | ✅ | **Fixed**: all 5 entries' costs doubled in `components.xml` (Orbital Fort, Space Dock, Space Station, Ultra Station, Death Star), matching the same exact-half bug found in row 22 — a finding beyond what the spec itself flagged (§15c only calls out `0x0200`, not `0x0400`); regular ship hulls (`0x4000`) were spot-checked and confirmed NOT to have this bug, so it was isolated to exactly these two categories. `Tests/UnitTests/ComponentDataFixesTest.cs`. |  |
| 25 | `component-stats.tsv` cross-check — Ship hulls (`0x4000`, 32 entries) | ✅ | ❌ | **Fixed**: Mini Morph added (Engine slot cap 2 + 6 General Purpose slots per its tsv-listed layout), all 32 now present. |  |
| 26 | `component-stats.tsv` cross-check — Engines (16 entries) | ✅ | ❌ | **Fixed**: Enigma Pulsar added (all 16 now present); its tsv "special=6" selector is confirmed cosmetic-only (a UI caption index, not a gameplay flag - see §15c's own hedge), so it's a plain non-ramscoop engine here, not PRT-restricted. |  |
| 27 | `component-stats.tsv` cross-check — Ship scanners (16 entries) | ✅ | ❌ | All 16 present and named correctly; not spot-checked stat-by-stat beyond confirming presence/count match. |  |
| 28 | `component-stats.tsv` cross-check — Shields (10 entries) | ✅ | ❌ | **Fixed**: Langston Shell added (all 10 now present) - as a plain `Value=125,Type=Shield` entry, matching Croby Sharmor/Fielded Kelarium's own existing pattern; the separate 95%-defense-multiplier/+65-flat-points special-casing §7 traces is a distinct, not-yet-built feature (row 11), not something this data-only fix attempts. "Gorilla Delegator" in xml vs tsv's "Gorilla Delagator" is a cosmetic spelling difference only. |  |
| 29 | `component-stats.tsv` cross-check — Armor (12 entries) | ✅ | ❌ | **Fixed**: Mega Poly Shell added (all 12 now present), same plain `Value=400,Type=Armor` treatment; its 80%-multiplier/+100-point special-casing is likewise deferred to row 11. |  |
| 30 | `component-stats.tsv` cross-check — Beam weapons (24 entries) | ✅ | ❌ | **Fixed**: Multi Contained Munition added (all 24 now present). "Gatling Neutrino Cannnon" in xml (extra "n") vs tsv's "Gatling Neutrino Cannon" is a cosmetic typo only. |  |
| 31 | `component-stats.tsv` cross-check — Torpedoes/capital missiles (12 entries) | ✅ | ❌ | **Fixed**: Anti Matter Torpedo added (all 12 now present). "Armegeddon Missile" in xml vs tsv's "Armageddon Missile" is a cosmetic typo only. |  |
| 32 | `component-stats.tsv` cross-check — Bombs (15 entries) | ✅ | ❌ | **Fixed**: Hush-a-Boom added (all 15 now present) - see row 7 for a separate functional gating bug among the pre-existing ones. |  |
| 33 | `component-stats.tsv` cross-check — Mining robots (8 entries) | ✅ | ❌ | **Fixed**: Alien Miner added (all 8 now present). Naming variants like "Robo-Mini Miner" vs tsv's "Robo-Mini-Miner" are cosmetic hyphenation differences only. |  |
| 34 | `component-stats.tsv` cross-check — Mine layers (10 entries) | ✅ | ❌ | All 10 present; spot-checked Heavy Dispenser 110's Space-Demolition restriction (`SD=2`) matches the spec's finding that 8/10 mine-layer subtypes are Space-Demolition-exclusive. |  |
| 35 | `component-stats.tsv` cross-check — Electrical (17 entries) | ✅ | ❌ | **Fixed**: Multi Function Pod added (all 17 now present) - given as `Value=60,Type=Jammer`; the tsv's generic "family's single stat" schema doesn't disambiguate exactly which electrical sub-family a "multi function" item combines, so this is a disclosed best-effort single-property guess, not a spec-confirmed Jammer identity. **Spec-9:** note stale: the Multi Function Pod is now `Cloak 60`, not a Jammer. |  |
| 36 | `component-stats.tsv` cross-check — Mechanical (11 entries) | ✅ | ❌ | **Fixed**: both Multi Cargo Pod (`Value=250,Type=Cargo`, matching the already-confirmed "+250 kT third tier" finding rather than the tsv's raw sub-family code) and Jump Gate (data-only - no Property block, since its actual gameplay effect isn't spec-confirmed) added, all 11 now present. |  |
| 37 | `component-stats.tsv` cross-check — Terraforming (20 entries) | ✅ | ✅ | All 20 present and cost-matched (Total ±3 spot-checked: Energy=70 matches tsv exactly); xml's shorter names (e.g. "Total ±3" vs tsv's "Total Terraform ±3") are a naming-convention difference only, not a data gap. **Spec-9:** "all 20 present" is wrong: **Gravity Terraform ±11 is missing** and Gravity ±15 has Biotech 3 (exe 4) - see research row 31. **Implemented (spec-9 pass):** Gravity Terraform ±11 added, ±15 corrected. |  |
| 38 | `component-stats.tsv` cross-check — Planetary scanners/defenses (15 entries) | ✅ | ⚠️ | **Fixed**: Genesis Device added (all 15 now present). **Its gameplay effect is now implemented** (spec-8 production-queue §10c: it is a 5,000-resource production item that resets the planet) — see production-queue.md row 18, `GenesisDeviceProductionUnit`; the component record itself is still data-only. Also **fixed this session**: all 4 Snooper entries (320X/400X/500X/620X) had `PenetratingScan` hard-set to exactly half of `NormalScan`; now corrected to equal `NormalScan` (e.g. Snooper 620X: both 620), matching the tsv's single signed range field, whose magnitude encodes the full normal-range value for all four. `Tests/UnitTests/ComponentDataFixesTest.cs`. |  |
| 39 | Regenerating Shields armor halving applies only to Armor-category parts, per slot with floor | ✅ | ✅ | `ShipDesign.Update` (:887-899) multiplies the whole Armor total (hull + Croby) by 0.5. **Implemented (spec-9 pass):** see combat row 39. | See combat-resolution RS rows. |
| 40 | Cloak raw points per named component (§11 table) | ✅ | ✅ | **Four store half the points**: Chameleon Scanner 20 (should be 40), Shadow Shield 35 (70), Depleted Neutronium 25 (50), Orbital Adjuster 25 (50); **six have no Cloak property**: Enigma Pulsar 20, Langston Shell 20, Mega Poly Shell 40, Multi Contained Munition 20, Alien Miner 60, Multi Cargo Pod 20. Electrical values (300, 70, 140, 540, 60) are right. **Implemented (spec-9 pass):** all ten named components carry the spec's raw cloak points (Chameleon 40, Shadow Shield 70, Depleted Neutronium 50, Orbital Adjuster 50, Enigma Pulsar 20, Langston Shell 20, Mega Poly Shell 40, Multi Contained Munition 20, Alien Miner 60, Multi Cargo Pod 20). | Resolves the earlier "subtype to name" blocker. |
| 41 | Design-reveal mask (+0x8b): Space Demolition learns designs of fleets that hit its minefields, Packet Physics the starbase designs its packets strike; step 39 shows them in full | ❌ | ❌ | War Monger full-detail scan is done (`ScanStep` ~:227); the SD/PP reveal is not. |  |

**Summary**: 31/41 implemented (full or partial), 10/41 tested (full or partial). (Spec-8 changed this file only in §9 and one cross-reference; row 38's Genesis Device gained its gameplay effect via production-queue.md.)

**What changed since the previous pass**: The single most significant finding is a clean, exhaustively-verified correspondence between two independent gaps: `ServerState/BattleEngine.cs`'s `GrantBattleTechGains` explicitly documents (in its own comment) that it deliberately does *not* reproduce the original's per-component battle-reward table, and — checked component-by-component against the spec's newly-added §14a table — **all 12 of the specific one-time battle/event-granted components it names** (Multi Cargo Pod, Multi Function Pod, Langston Shell, Mega Poly Shell, Alien Miner, Hush-a-Boom, Anti Matter Torpedo, Multi Contained Munition, Mini Morph, Enigma Pulsar, Genesis Device, Jump Gate) **are entirely absent from `components.xml`**, with zero false positives or false negatives against that list — this single root cause explains nearly every "missing component" finding scattered across rows 25–38. The second major finding is a systematic, exactly-2x cost error affecting two full categories in `components.xml`: all 16 Stargates/Mass-Drivers and all 5 Starbase Chassis have resource+mineral costs exactly half of `component-stats.tsv`'s ground truth (e.g. Space Station: xml res=600 vs tsv res=1200). The spec's own §15c explicitly investigated and resolved this exact discrepancy for the Stargate/Driver category, concluding the doubled tsv value is the real in-game figure and that commonly-published/wiki numbers are the ones already halved — meaning `components.xml` was very likely built from those halved wiki figures for these two categories specifically (regular ship hulls, checked as a control, show no such halving — this Starbase Chassis half of the bug is new, beyond what the spec itself flagged). A third, smaller but concrete bug: Inner Strength's documented 5-bomb exclusion (Smart/Neutron/Enriched-Neutron/Peerless/Annihilator) is only enforced for 2 of the 5 (Peerless, Annihilator) in `components.xml` — Smart Bomb, Neutron Bomb and Enriched Neutron Bomb have empty `<Race_Restrictions/>` and are currently buildable by Inner Strength races. A fourth: "Mass Driver 7" is misnamed "Super Drvier 7" (wrong family name, typo, and a wrong tech-level value). A fifth: all 4 Snooper planetary scanners have `PenetratingScan` hard-set to exactly half of `NormalScan`, where the tsv's ground truth has penetrating range equal to the full normal range for all four. Also newly confirmed clean: the previously-RETRACTED "9-component-slot cap" claim is fully resolved — the real per-hull slot count (2–16) is already correctly implemented via `HullModule`, and Meta Morph's specific slot-capacity layout (3/8/2/2/2/2/1) matches the spec's own corrected worked example exactly. No regression tests exist anywhere in `Tests/` for the scanner 4th-power combination formula, `RaceRestriction`/`RaceComponents` availability gating, or any of `ShipDesign.Update()`'s per-component summation logic, despite all three being real, exercised code paths.

---

### turn-generation-engine.md

**This file was substantially rewritten in spec-8.** Its old 23-item "phase list" was replaced by a
40-step reconciled order read statement-by-statement from the master routine, and the spec now says
outright that several old items (combat at phase 9, diplomacy decay, tech-field reveal, hull-upgrade
cascade, autosave, ramscoop refuel, "two identical random-event passes", the squared per-design
"capability rating") were **wrong**. The table below is therefore re-cut around the new steps rather
than patched row-by-row; the old rows it supersedes are noted.

| # | Behavior (spec-8 step) | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Master routine is a strict 40-step ordered pipeline (§1) | ⚠️ | ✅ | `TurnGenerator.Generate()` is still coarser than 40 steps but now follows the reconciled *relative* order: orders → scrap → movement → minefield decay → production steps → **battle** → colonisation resolution → bombing/wormhole/decay steps → year++ → victory → scan/output. `Tests/UnitTests/TurnOrderTest.cs`. **Spec-9:** the 40-step order itself is **unchanged**; these *code* deviations exist (several pre-date spec-9 but were not flagged): fuel (22) and repair (25) run inside the per-fleet movement loop (`ProcessFleet` -> `RegenerateFleet`), i.e. before minefield decay, production and the battle - fleets are healed *before* they fight; orbital bombardment (23b) must precede colonise/invade (23f) but `BombingStep` (key 19) runs after `ColonizationResolver`; invasion happens on arrival during movement, so a starbase destroyed in battle does not open the planet the same turn; wormhole relocation (21) and salvage decay (18) run after the battle, and salvage decay ignores the one-year grace flag. |  |
| 2 | **Battle pass runs AFTER movement (16) and the production hub (20), not before (step 23)** | ✅ | ✅ | **Fixed (regression of an earlier fix).** The v5/v7 audits said "combat before movement — confirmed fixed", on the strength of the now-superseded 23-phase list; `Generate()` had `battleEngine.Run()` *before* the movement loop. It now runs after `ProcessFleet` movement and the production steps (keys 11/12) and before bombing (19). A fleet that arrives now fights the same turn; ships built this turn can be fought at their build location. `TurnOrderTest.Battle_RunsAfterTheProductionSteps_AndBeforeTheLaterSteps` (verified to fail with the old position). | Waypoint *tasks* still execute during movement (see row 9) rather than after battle, so "a fleet destroyed in battle does none of its post-battle tasks" is not yet reproduced. |
| 3 | Year counter (33), victory (35) and output/scan (39) come after all simulation | ✅ | ✅ | `Generate()`: `TurnYear++` then `victoryCheck.Victor()` now run after every production/battle/bombing/decay step; `ScanStep` (key 99) is last. `TurnOrderTest.YearCounterAndVictoryCome_...`. | Previously the year/victory ran *before* the production steps, so victory saw a pre-production galaxy. |
| 4 | Shuffle player order once per turn (step 5), used only by order application | ⚠️ | ✅ | Partially: shuffled order drives fleet processing and `RemoteMiningStep`, not order application. `TurnGeneratorTest.Generate_PopulatesShuffledEmpireOrder_WithEveryEmpire`. | |
| 5 | Step 6: orders applied per record; a bad record only costs itself | ✅ | ⚠️ | `TurnGenerator.ReadOrders()/ParseCommands()`; per-record fault isolation (standout finding #8). The original aborts the *whole run* on one failing record — this project deliberately does better. | Stale/foreign-game/nonce file checks are binary-format concerns, N/A for the XML order files. |
| 6 | Step 7: duplicate/invalid registration flags and notices 256/257/259 | ❌ | ❌ | Not found. | The spec deliberately does not describe the comparison (identity-block content), so there is nothing to implement. |
| 7 | Step 8: design normalisation (slot 0 forced to an engine) | ❌ | ❌ | Not found. | Design-editor rules already prevent the state; low value. |
| 8 | Steps 9-11: follow-fleet orders (mark, propagate up to 8 passes, message 312) | ❌ | ❌ | No follow-fleet waypoint exists. | A feature gap in the waypoint model. |
| 9 | Step 12 pre-movement stage: waypoint-zero tasks (Scrap only), colonise/invade resolution, research buy loop, cargo-transfer report | ⚠️ | ⚠️ | `ScrapFleetStep` runs pre-movement (matches mode 1). All *other* tasks run inside `UpdateFleet` during movement; colonisation resolves once (post-movement), not twice; research buys once, not three times. | Closest structural gap to the spec: tasks should be split into pre-move (modes 1/2) and post-battle (modes 3/4). |
| 10 | Step 13: race-definition integrity check with degrade loop (messages 279/386) | ❌ | ❌ | Not found; race legality is enforced only at design time in the race designer. | |
| 11 | Steps 14/15/21: special-object passes (mass packets, Mystery Trader, wormhole relocation) | ⚠️ | ❌ | `WormholeDriftStep` relocates wormholes; **mass packets and the Mystery Trader do not exist** (no packet or Trader class). | Two whole subsystems absent. |
| 12 | Step 16: fleet movement incl. minefield hits, fuel use, Stargate, Cheap-Engines balk, warp-10 strain | ✅ | ✅ | `UpdateFleet`, `CheckWarp10Destruction`, `TryStargateJump`; `StargateJumpTest`, `TurnGeneratorTest`. | The "5 safe warp-10 engines" exclusion is `Engine.FastestSafeSpeed == 10`. |
| 13 | Step 16: Alternate Reality fleets lose ~3% of >10 colonists to warp acceleration (msg 193) | ✅ | ✅ | Not found. **Spec-9:** now fully specified (see fleet-movement row 54). **Implemented (spec-9 pass):** fleet-movement row 54. | Spec itself says the surrounding condition was not traced — see ambiguity list. |
| 14 | Step 18: minefield decay `2 + 4×S`% (Space Demolition ×1), cap 50%, min loss `max(10, pct)`, no regrowth | ✅ | ✅ | **New.** `ServerState/TurnSteps/MinefieldDecayStep.cs`, called after movement for fields that existed before it (a field laid this turn is not decayed the turn it appears). Also removes the old decay that lived in `CheckForMinefields.Check` (1% once *per moving fleet*, mutating `AllMinefields` while enumerating it). `Tests/UnitTests/MinefieldDecayTest.cs`; `TurnGeneratorTest.Generate_LayMines_CreatesAMinefield` guards the lay-then-decay ordering. | "Third (speed-bump) type" minimum and the owner's "detonate" flag (+25 points) are not modelled — the codebase has neither concept. Supersedes old row 19 ("regrowth"). |
| 15 | Step 18: mass-packet decay in flight, resting-packet decay | ❌ | ❌ | No mass-packet class exists. | |
| 16 | Step 19: Inner Strength colonist growth aboard fleets (`growth% × colonists / 200`) | ✅ | ✅ | Not found. **Spec-9:** now fully specified: units are hundreds of colonists, growth = R x cargo / 200 truncated, 1/3 chance of +1 when zero, overflow only to an own orbited planet (msgs 251/344). **Implemented (spec-9 pass):** fleet-movement row 32. | Units (colonists vs kT) and the "2-in-3 chance of none when zero" wording are ambiguous — see list. |
| 17 | Step 20: production hub — mining, per-planet queue, growth, research, random events | ⚠️ | ⚠️ | `StarUpdateStep`/`Manufacture`; the **random-event wrapper does not exist** (row 27). | |
| 18 | Step 22: fuel pass — friendly dockable starbase refuels to full; otherwise +50 mg per Anti-matter Generator, +200 mg per Fuel Transport / Super-Fuel Transport ship; **ramscoops play no part** | ✅ | ✅ | **New.** `TurnGenerator.RegenerateFleet`, `Fleet.PassiveFuelGeneration`, `ShipDesign.FuelGenerationPerYear/IsFuelTransportHull`. `Tests/UnitTests/PassiveFuelGenerationTest.cs`. | Old row 25 credited "ramscoop flat 200" — retired as wrong. A friend's (not own) starbase still does not refuel (existing TODO). |
| 19 | Step 23: post-movement stage - battle, **orbital bombardment** (`FUN_10f0_6ea2`), Mystery Trader encounter, tasks modes 3/4, colonise/invade again | ⚠️ | ⚠️ | Battle ✅ (row 2). The meeting/exchange pass and Trader encounter ❌. **Spec-9:** `FUN_10f0_6ea2` is **orbital bombardment**, not a colonist/mineral "exchange": it kills at least 1 colonist, scales by defence and reverses up to 500 points of terraforming. `Bombing.cs`/`BombingStep` exist (min-kill present); the 500-point un-terraform is missing, the Trader encounter is absent, and the order is wrong (row 1). |  |
| 20 | Step 24: mine sweeping by fleets and starbases | ❌ | ❌ | Not found (no sweep rate, no sweeper). | Concrete rules in spec §11 (rate ≥2, ⅓ vs third type, messages 194/244/190). |
| 21 | Step 25: repair — moved 1%, deep space 2%, foreign orbit 3%, own planet 5%, own starbase 8% / 20% with dock; Inner Strength ×2; fuel-transport stacks +5/+10%; starbase self-repair 10% (15% IS) | ⚠️ | ✅ | **New/fixed.** `TurnGenerator.RegenerateFleet(fleet, movedThisYear)`. The spec's 5/10/15/25/40/100 are the same figures in fifths of a percent, so the existing 1/2/3/5/8/20% table was right; what was missing was the *moved-this-year* rule (the old code used "has a waypoint"), IS doubling, fuel-transport bonuses and the flat starbase rate (old: a starbase repaired itself at 8%/20% as if it were a ship orbiting it). `Tests/UnitTests/FleetRepairRateTest.cs`. **Spec-9:** exact rule: units are 1/500 of a ship's armor (the 1/2/3/5/8/20% reading is confirmed); fleets that fought or hit a minefield this generation are **skipped**; at its own planet whose starbase fought a fleet gets 5%; the fuel-transport bonus is **per fleet** (25 if any stack is a Fuel Transport, 50 if any is Super-Fuel, 50 - not 75 - with both), added after the Inner Strength doubling, applying even if the fleet moved; starbase self-repair is skipped if it fought. **CONTRADICTS** in `RegenerateFleet`: runs at step 16 not 25, no skip for fighters/mine-hit fleets, the transport bonus is added per *stack* (+5/+10% each), the "starbase fought" case is a TODO, `Math.Max(..., 1)` adds a 1-armor minimum. **Implemented (spec-9 pass):** the per-fleet fuel-transport bonus (max, not additive) and "no repair after a jump" are in. **Still open**: the skip for fleets that fought or hit a minefield, the starbase-fought 5% case, the 1-armor minimum and the step 16 -> 25 reordering. | "Not counted while the starbase is under attack" TODO remains. |
| 22 | Step 26: Claim Adjuster free terraforming + 10% permanent drift | ⚠️ | ✅ | `StarUpdateStep.ApplyClaimAdjuster*` (earlier this session). Uses the flat 15/30% cap, not researched terraform tech; "reverts on capture" not modelled. `ClaimAdjusterTest.cs`. | |
| 23 | Step 27: fleet terraforming / hostile un-terraforming | ❌ | ❌ | The "Terraforming" ship component property sums into designs but no turn pass uses it. | |
| 24 | Step 28: status-flag attrition pass (only if a race has status bit 2 and turn > 9) | ❌ | ❌ | Not found. | Racial-attrition event; flag meaning not documented. |
| 25 | Step 30: reported-population fuzz | N/A | N/A | Intel uses exact population; deliberate simplification. | |
| 26 | Steps 31-32, 37-38: host-file flag clear, backup-folder rotation, file moves, nonce re-roll | N/A | N/A | `BackupTurn()` keeps a per-year backup folder instead; binary-file housekeeping not applicable. | |
| 27 | Random events: comet (1-in-20, not before year 10), environment shift (1-in-20), mineral deposit (1 in 15−size), Mystery Trader; gated by "No Random Events" | ⚠️ | ✅ | Confirmed absent — no event code, and no "No Random Events" game option. **Spec-9:** **all four yearly events are now fully specified (§5a)** - see rows 48-53. The wrapper runs at the end of the production hub (step 20), comet -> environment shift -> deposit -> Trader creation, each rolling independently, before the year increment; "year" = turns generated (displayed year - 2400 in the original; `TurnYear - Global.StartingYear` here). **Implemented (spec-9 pass):** comet, environment shift and mineral deposit implemented as `RandomEventsStep` (key 15, after production, before the battle and `TurnYear++`) with a new "No Random Events" game setting (`GameSettings.NoRandomEvents`, `NewGameViewModel`, one checkbox in `NewGameView.axaml`; the mobile UI and the WinForms wizard lack it). **Mystery Trader not implemented.** | Spec §11 now gives concrete numbers but several details are still missing — see ambiguity list. |
| 28 | Queue cleanup after a disaster keeps auto-build entries (types 0-6), deletes the rest | ✅ | ✅ | N/A until disasters exist (row 27). **Spec-9:** scope narrowed: only the comet and the environment shift run it (not the mineral deposit, not Genesis). **Implemented (spec-9 pass):** `RandomEventsStep.CleanupQueueAfterDisaster` keeps auto-build entries, deletes the rest (comet and environment shift only). |  |
| 29 | Salvage rolls: 13-entry component table + 6-entry tech-field table fire only from battle wreckage, Scrap-at-starbase, planet capture | ⚠️ | ⚠️ | `BattleEngine` (battle) and `TechTrading` (scrap/invade) wire the triggers; the exact bit/percentage feed (rare parts +1%/unit capped 25%, bits 8/10/12 never fed) not re-audited. `SpecialComponentGrantTest.cs`. **Spec-9:** **CONTRADICTS**: salvage never feeds bits 8/10/12 (only the Trader reaches them) and feeds one point per installed rare part (cap 25%) only for bits 0-7, 9, 11; `BattleEngine.GrantOneTimeSpecialComponent` (~1657) draws uniformly from all 12 and ignores the destroyed designs. The 6-entry table's per-race bytes are tech levels and the reward equals the price of the next level. | Spec-8 corrects the *trigger* (was "per-turn random event"); this project's implementation already used the battle/scrap/capture triggers. |
| 30 | Colonisation/invasion: War Monger 165% / others 110%, Inner Strength defenders ×2, strongest wins, exact tie destroys all | ✅ | ✅ | `InvadeTask.Perform` (`1.1 × 1.5` = 165%), `ColonizationResolver`. `ColonizationTieBreakTest.cs`; invasion arithmetic itself untested. **Spec-9:** the winner's colonists are scaled by (largest - runner-up)/largest, truncated, minimum 1, where the runner-up is the strongest contender with a **lower race index** than the winner (0 if none); `ColonizationResolver.ApplyWinnerAndNotifyLosers` lands the full cargo, with no 110%/165% strength factor on the colonisation path. **Implemented (spec-9 pass):** fleet-movement row 55. |  |
| 31 | Alternate Reality landing on an *owned* planet: colonists destroyed (msg 87) — AR can still colonise an unowned planet (msg 11) | ✅ | ✅ | **New.** `InvadeTask.IsValid`. `Tests/UnitTests/AlternateRealityInvasionTest.cs`. **Spec-9:** message 87 is defensive and unreachable; an AR unload onto its **own** planet is an ordinary cargo transfer (consistent with `InvadeTask`'s own-planet path). **An AR race that wins a landing gets its starbase design slot 0 "Starter Colony" installed** - not implemented. **Implemented (spec-9 pass):** an AR winner now gets the Starter Colony starbase (population-growth row 30). | Own-planet AR transfers left alone — spec wording ("already owned") is ambiguous; see list. |
| 32 | Landing kills all colonists when the owned planet has a starbase (msg 88) | ✅ | ❌ | `InvadeTask.IsValid` starbase check. | |
| 33 | Planet-artifact research bounty on capture (`100 + rand(0..300)` in a random field, msg 94) | ❌ | ❌ | Not found; no artifact flag exists on planets. | |
| 34 | Game-option bits (§1b): Max Minerals, Slower Tech, No Random Events, Public Scores, Clumping, Alliances | ❌ | ❌ | Only `AcceleratedStart` exists (see row 35). `GameSettings` has none of the others. **Spec-9:** the option is now specified: an AI target-selection bias (skip computer-owned targets unless no human-owned one exists). Still no setting. | Six of seven documented options are unimplemented. |
| 35 | Accelerated BBS Play: home population × `(growth% + 5) × 2 / 10` (4× at 15%), +25% surface minerals, +5 to concentrations < 40 | ⚠️ | ✅ | **Fixed (population).** `Race.GetStartingPopulation` (was a flat 100,000, correct only at 15% growth). `Tests/UnitTests/AcceleratedStartPopulationTest.cs`. The +25% surface minerals and concentration boost are not applied. | |
| 36 | Victory evaluation is step 35 (after all simulation, before output) | ✅ | ✅ | See row 3. | Supersedes the "phase 20/21" wording. |
| 37 | Starbase cloak factor `(100−cloak%)²` hides a starbase from planet sweeps; no Tachyon multiplier for starbases | ⚠️ | ⚠️ | Starbase cloak is computed (`CloakCalculator`, Improved Starbases/Super Stealth baselines) and applied in `ScanStep`; whether Tachyon detection is (wrongly) applied to starbases was not re-checked. `CloakingTest.cs`. | |
| 38 | Step 39: per-player output incl. score-history rule (own race / game-over / eliminated / Public Scores + turn > 19) | ⚠️ | ❌ | `WriteIntel()` writes per-player intel; the score-visibility rule and the option it depends on are absent. | |
| 39 | Victory routine raises the elimination flag and posts 187/188 | ❌ | ❌ | No elimination notice exists. | See victory-conditions.md. |
| 40 | Research buy loop runs three times a turn; only step 20 adds income | ❌ | ❌ | One research pass per turn. | Observable only through salvage/bounty banked pools. |
| 41 | Battle engine: 16-round cap | ✅ | ✅ | `BattleEngine.cs:41,881-886`. | |
| 42 | Battle engine: parity-dependent rate + 3-bracket targeting | ⚠️ | ⚠️ | Not independently re-verified beyond combat-resolution.md's own audit. | |
| 43 | Battle VCR viewer is pure playback | N/A | N/A | UI concern, out of scope for this file. | Agreed |
| 44 | ~~Fleet-linked habitat-tolerance drift~~ **retracted by spec-9** | N/A | N/A | **Spec-9:** there is no habitat drift: the "26 or 10" is the tech-level cap and a tech total of 72-83 gives N - 2. Replaced by the Mystery Trader rows below. | Spec-8 re-attributes the old "wormhole cargo check" to the Mystery Trader. |
| 45 | Batch/auto turn generation (10/100/1000-turn loop) | ❌ | ❌ | No batch-generation entry point exists. | |
| 46 | Manual "ready/submitted" toggle independent of generation | ⚠️ | ❌ | Field exists (`EmpireData.TurnSubmitted`), no standalone toggle-command located. | |
| 47 | "Auto Generate Options" (auto-advance/force-generate timers) | N/A | N/A | Not found. **Spec-9:** the Auto Generate Options dialog is non-functional in the original; no auto/force-generate policy may be inferred. (The legacy `NovaConsole.cs:297` checkbox is a project addition.) |  |
| 48 | **Comet** (1/20 a year from counter >= 10; size random(4); owned planets with >= 5,100 colonists skipped while counter < 20): population loss 25 + 20 x size % (none for AR), +50-99 concentration (65-128 huge, cap 200) and 3,000-19,999 raw units divided by 16 on size + 1 minerals, 3-5 point steps on size + 1 axes (6-10 huge) on current and original clamped 1..99, messages 131-138 (everyone / owner), then queue cleanup | ✅ | ✅ | No event code. Fully implementable (`TurnYear - StartingYear` as the counter; run before `TurnYear++`). **Implemented (spec-9 pass):** `RandomEventsStep.Comet` (`RandomEventsTest`, 31 tests incl. boundaries and the huge-comet cap). Assumptions: huge = size 3, cap applies literally (min(old + gain, 200)), loss is floor(colonists x pct / 100) on raw colonists, message wording is ours. | Needs a galaxy-size index mapping from `MapWidth` (spec: diameter = (size + 1) x 400). |
| 49 | **Environment shift** (1/20, no year floor; planet uniform over all stars; one axis; step 3 + random(3) with a 3 re-drawn as 6 + random(3), sign 50/50, current and original, clamped 1..99; msg 253 to the owner; queue cleanup) | ✅ | ✅ | Not implemented. **Implemented (spec-9 pass):** `RandomEventsStep.EnvironmentShift`. |  |
| 50 | **Mineral deposit** (1 in (15 - size index) a year from counter >= 10; a random mineral of a random planet gains 5-19 if below 180; msg 254 even if nothing changed; no queue cleanup) | ✅ | ✅ | Not implemented. **Implemented (spec-9 pass):** `RandomEventsStep.MineralDeposit`; galaxy-size index s = clamp(MapWidth / 400 - 1, 0, 4) (`GalaxySizeIndex`). |  |
| 51 | **Mystery Trader creation and movement** (counter >= 40; chance by year rule; speed 8 + random(5); edge start/destination; msg 299; each year 1/25 speed +1 (msg 304), 1/3 of those a new destination; step speed^2 ly; on arrival removed or another pass, msg 192) | ❌ | ❌ | No Trader object exists. | Needs a new special-object type. |
| 52 | **Mystery Trader fleet encounter**: < 5,000 kT minerals refused (264/280); otherwise the fleet is absorbed and N = min(10, 6 + floor((m - 5,000)/1,200)) adjusted by tech total (>= 108 -> 1, 96-107 -> 2, 84-95 -> N - 3, 72-83 -> N - 2, 60-71 -> N - 1) tech advances, else a part (msgs 267/268/271) or ships (335/336) | ❌ | ❌ | Not implemented; the only route to Mini Morph, Genesis Device and the bit-12 ships gift. | Three Trader ship designs' contents are not in the spec. |
| 53 | **Mystery Trader AI planet trade** (skill >= 2, starbase, within 100 ly; price 3,500 / 5,000 kT paid Ge -> Bo -> Fe) | ❌ | ❌ | Not implemented. |  |
| 54 | **AR innate mining**: effective mines = sqrt(population / 100), minimum 1 | ✅ | ✅ | Contradicts the "AR gets 0 operable mines" rows (production-queue row 3). **Implemented (spec-9 pass):** population-growth / race-traits innate mining. |  |
| 55 | Repair skip for fleets that fought or hit a minefield; salvage record created this turn skips that year's decay (grace flag) | ❌ | ❌ | Neither exists (rows 1 and 21). |  |
| 56 | "No Random Events" game option | ✅ | ✅ | No setting; it would gate the wrapper of step 20 and the artifact bounty. **Implemented (spec-9 pass):** `GameSettings.NoRandomEvents` gates `RandomEventsStep` (the planet-artifact bounty it also gates does not exist here). |  |

**Rows retired as wrong by spec-8** (were rows 6, 7, 9, 10, 12, 16, 25, 28 of the v7 table): "tech-field reveal",
"hull-upgrade cascade", "combat-detection before movement", "diplomacy decay + war declaration", the
"home-planet-related flag pass", "ramscoop flat 200 fuel", and "per-design capability rating feeding
auto-build" (it is a starbase cloak factor, read only by the step-39 visibility sweeps).

**Summary**: 34/51 implemented (full+partial, excl. 3 N/A rows), 30/51 tested. (Spec-8 re-cuts this file
from 38 rows to 47; the v7 figure of 11/38 is not directly comparable.) This pass: battle order fixed
(row 2), year/victory order (row 3), minefield decay moved out of the per-fleet check (row 14), fuel pass
(18), repair rates (21), AR landing rule (31), Accelerated BBS population (35).

**Headline gaps**: the random-event wrapper (comet / environment shift / mineral deposit / Mystery
Trader) and the special-object passes (mass packets, Trader), fleet terraforming, mine sweeping,
follow-fleet orders, the post-battle task split, six of seven game-option bits, and Inner Strength's
colonist growth. Everything in the reconciled order that *is* implemented now runs in the spec's order.

---

### victory-conditions.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | 10 configurable victory items | ✅ | ❌ | `Common/Files/GameSettings.cs:58-67`. |  |
| 2 | Exactly 7 checkboxes (condition 3 has none of its own) | ⚠️ | ❌ | Codebase exposes 8 (adds one for NumberOfFields and SecondPlaceScore, the latter never in the original UI at all). |  |
| 3 | Spinner steps 1/5 via Page Up-Down | ❌ | ❌ | Plain `NumericUpDown`, no custom step logic. |  |
| 4 | Byte-encoded storage | N/A | N/A | Deliberately reimplemented as plain bool/int — behavior-equivalent. | Agreed, skip |
| 5 | UI numeric ranges reflect real thresholds | ✅ | ❌ | **Fixed**: `NewGameViewModel.cs:117` raises TotalScore's slider Max to 20000, above its own 11000 default. |  |
| 6 | Condition 3 only applies when condition 2 enabled | ✅ | ❌ | `ServerState/VictoryCheck.cs:163-169`. |  |
| 7 | Default values match confirmed client defaults | ✅ | ❌ | `GameSettings.cs:58-67` — previously audited/fixed. |  |
| 8 | Derived TargetsToMeet = min(raw, enabled-count) | ✅ | ❌ | `NewGameViewModel.cs:279-307`. |  |
| 9 | Year-gate meta-setting (MinimumGameTime) | ✅ | ❌ | `GameSettings.cs:67`; `VictoryCheck.cs:76-81`. |  |
| 10 | "Simple New Game" auto-seeds year-gate | ❌ | ❌ | No such flow exists. |  |
| 11 | Per-turn evaluation, gated by year, notifies all races | ✅ | ❌ | `VictoryCheck.cs:48-107`. |  |
| 12 | Detects sole eligible leader / ties | ✅ | ✅ | Just declares first-in-iteration-order qualifier; no tie handling. **Spec-9:** ties are now specified: rank = 1 + number of races with a strictly higher score, so ties share a rank; `SetRanks` numbers sequentially and `SecondPlaceScore` relies on `Rank == 2`. **Implemented (spec-9 pass):** `Scores.SetRanks`: rank = 1 + races with a strictly higher score; `VictoryCheck` second place = best score among the other races; `HighestScore` requires a sole leader (also fixes an old misplaced `break`). |  |
| 13 | **Evaluation runs after the year-counter increment — inverted** (spec-8: it is step 35 of 40, after *all* simulation, before the per-player files are written) | ✅ | ✅ | **Fixed, and tightened this pass**: `TurnYear++` then `victoryCheck.Victor()` now run after every production/battle/bombing/decay step (previously they ran *before* the production steps, so victory saw a pre-production galaxy). `Tests/UnitTests/VictoryCheckTest.cs`, `TurnOrderTest.YearCounterAndVictoryCome_...`. |  |
| 14 | Per-condition checks (7 formulas) | ✅ | ❌ | `VictoryCheck.cs:115-367`. |  |
| 15 | Condition 5: exceeds 2nd place by percentage, not flat multiply | ✅ | ❌ | `VictoryCheck.cs:356-366` — comment documents this exact fix. |  |
| 16 | Score formula components | ✅ | ✅ | `ServerState/Scores.cs:62-171` — several terms diverge from the spec's exact shape (population cap, tech bonus structure, capital-ship formula). **Spec-9:** **CONTRADICTS in six places** (`ServerState/Scores.cs`): population is `Colonists/100000` floored with no cap (spec: ceil(pop/1000) capped at 6 per planet); resources use `ResourcesOnHand.Energy` (leftover) not output; every starbase counts (spec: only hulls with dock > 0, 3 each); unarmed 0.5 / escort 2 each with no min(., planets) cap; the tech term is a flat 1-4 from `AllTechLevels`, which is **never populated outside tests**, so tech scores 0 (spec: per-field curve L / 2L-3 / 3(L-3) / 4L-18); ranks are not shared. Also `HasWeapons` is always true and `PowerRating` is a stub, so every token is an Escort and there are no Unarmed or Capital ships. **Implemented (spec-9 pass):** `Scores.GetScoreRecord` follows the spec: per planet min(6, ceil(colonists / 100,000)) (unit assumed), Resources / 30 from `GetResourceRate` output, 3 per dock-capable starbase, the per-field tech curve from `ResearchLevels`, min(.,planets) caps, capital term; ships counted not tokens, classes by a design weapon rating (`Scores.DesignWeaponRating`). `ScoreRecordTest`. Gaps: the beam "cut to a third" flag and the per-design adjustment term are unspecified; `ShipDesign.PowerRating`/`HasWeapons` remain stubs. |  |
| 17 | Setup screen and runtime check share one settings store | ✅ | ❌ | `GameSettings.Data` singleton used identically by both. |  |
| 18 | **Elimination flag** (spec-8): the same pass detects a race with all four scoring words zero, sets its status bit, and sends message 187 ("All traces of … eliminated") to every other race, 188 when one race is left; the bit removes the race from the Super-Stealth live-race count and gives every player its score record | ✅ | ✅ | No elimination detection or notice exists (`VictoryCheck` only counts conditions). **Spec-9:** the elimination test is exact: Planets, Unarmed, Escort and Capital all zero. **Implemented (spec-9 pass):** `VictoryCheck.CheckEliminations` sets `EmpireData.Eliminated` (persisted) and posts 187 to every other race, 188 to the survivor (wording of 188 is ours). | The score-record visibility half is turn-generation row 38. |
| 19 | A victory declared this turn is already in this turn's output files | ✅ | ✅ | Consequence of the order fix in row 13: victory is evaluated before `WriteIntel()`/`ScanStep`. `TurnOrderTest`. | |
| 20 | Condition 6 = total resource **output** / 1,000; condition 7 = the Capital word | ✅ | ✅ | `VictoryCheck.ProductionCapacity` sums per-planet floor(leftover energy / 1000) instead of floor(total output / 1000). **Implemented (spec-9 pass):** condition 6 reads the record's Resources / 1000 and condition 7 the Capital count. |  |

**Summary**: 17/19 implemented (excluding 1 N/A row), **6/19 tested** — rows 5 and 13's bugs (slider Max, evaluation-order inversion) were fixed, row 13 is tested via `Tests/UnitTests/VictoryCheckTest.cs` and row 19 via the new `TurnOrderTest`; no other test in the repo references `VictoryCheck`, `Scores`, or `TargetsToMeet`/`MinimumGameTime`.

---

## Ambiguities and missing information for the next spec pass

### Resolved by spec-9 (from the v8 list)

| v8 item | Resolution |
|---|---|
| 1. AR automatic Mineral Alchemy | **Retracted** - it does not exist (code evidence plus a live test). |
| 2. AR planet with no starbase | Bit 0x2 = "a starbase orbits this planet"; no starbase = capacity 0 (the original divides by zero); a won AR colonisation installs the "Starter Colony" starbase (slot 0, Orbital Fort hull). |
| 3. Random events | **Fully specified** (§5a): year = turns generated, uniform planet over all stars, comet size random(4), env-shift roll, deposit units, caps, size index (diameter = (size + 1) x 400), Trader creation rule. |
| 4. UR deferred blend | Same-generation, planet owner, 100%, remainder lost. |
| 5. IS breeding / 6. AR transit loss | Both exact (units are hundreds of colonists). |
| 7. Genesis re-roll | One draw per axis written to original and current; no queue cleanup. |
| 8. Cloak points by subtype | The component-name table is given (combat §11). |
| 9. Scrap divisors | Retracted; exact rates given. |
| 10. Repair units | Confirmed: 1/500 of armor, so the 1/2/3/5/8/20% reading. |
| 11. Auto-build beyond Factories/Mines | Live-measured for Defenses, Terraform and Packets (§10j). |
| 12. Defense cap input | The owner's habitability for the *current* environment. |
| 13. Unposted messages | Triggers and fields given (§10i). |

### Still missing or contradictory (new)

1. **Spec self-contradictions (which paragraph governs?).** research-tech-tree §3/§7 still describe a 4-level availability window although the resolved open question says plain minimum; fleet-movement §4 has a stale "habitability-driven Load Optimal" beside the fuel-slot rule, a stale line saying `FUN_10b0_312a` is the overgating routine, and the old community overgating bullets; production/combat "earlier history" paragraphs claim "no range term" and "no kill ceiling"; race-traits §2/§2a/§5a say PP starts at **Energy 4** while the resolved open question frames 24 as the starting reading; ai §3/§4/§9 are untouched legacy text no §12 driver path calls; ai §6 mass-driver bullet overlaps §12's packet rules inconsistently; new-game §5a keeps the superseded fleet table beside the consolidated one.
2. **AR owned planet with no starbase:** the original faults; what should the port do (clamp to a floor, decline, forbid)? Also the Starter Colony's components (only the hull is given) and the five unexplained AR-homeworld bits.
3. **Concentration storage:** new-game gives homeworlds 100-299; population-growth says concentration is an unsigned byte (max 255) with comets capped at 200 - how are 256-299 stored, and does depletion's "treat >= 101 as 100" apply to them?
4. **Mystery Trader gaps:** contents of the three Trader ship designs ("M.T. Lifeboat/Scout/Probe"); `FUN_1030_821a(race, 0x4e, fleet)` at absorption; the AI skill-tier to difficulty mapping (is tier 2 "Tough"?); the tech cap of 10 under a copy-protection-adjacent bit (treated as 26 here).
5. **Score formula units:** "population in thousands, max 6 per planet" is implausible in colonists - presumably internal 100-colonist units, i.e. ceil(colonists / 100,000); which fleet status flag excludes ships from the counts.
6. **Combat:** the rounding of the missile allotment split; hit test `<` versus `<=` (the code is likely off by one); what feeds the 0-63 initiative byte ("a fixed category/subtype pair" at rate 10 plus four unnamed adjacent subtypes) and how it reconciles with the 0-8 initiative value; token cost in the target score (per ship or total); the low 7 bits of the stored damage word (explicitly unverified).
7. **Fleet/cargo:** where cargo dumped by the Stargate "forced dump" goes; whether an AR colonist unload onto a foreign planet is *refused* at the Transport handler (spec §4) or destroyed at landing (message 87, now called unreachable); colonist unload onto an *unowned* planet; the hit-chance unit for minefields (per ly versus per turn) and the 0/1 type index; the overgating stack-count quirk (a fleet with one 100% stack plus one survivor is removed) - replicate or fix?
8. **Production:** the Alchemy "shortfall conversion" (resources/25 or /100) inside the purchase routine - when it fires and for which entries; tension between §7 ("only in the year needed") and §10a ("buys as many as possible"); confirm the public 70% / 35% UR wording is simply never used; whether the defense cap is an integer percent (population-growth's integer evaluator should govern).
9. **Race traits:** what design flag `0x80` at `+0x7c` is (the quarter-rate UR case) and whether UR's mineral base is the miniaturized or listed cost; AR innate mining rounding (floor or float) and whether the mine-production setting applies; whether AR transit loss means only the departure turn or every moving turn and whether a Stargate jump is excluded; IS overflow ordering relative to AR loss and colonisation; the Speed Trap per-tier PRT table (an OR gate: SD or IS - the restriction model cannot express it); BET's "global flag clear" doubling condition; the order of the LSP and Accelerated-Start multipliers.
10. **Research:** whether miniaturization applies to hull cost; the text of the four tech-distance message variants.
11. **AI (new §12):** units ("population over 1,100" etc.) are probably hundreds of colonists; items not traced - the leg-crosses-minefield test `FUN_1090_1240`, `FUN_1050_69c2` engine adjustments, the wormhole known-score `FUN_1118_0742`, "poor concentrations" in the Genesis rule, the 24 AI race template contents, the design templates, private routers; **architecture blockers**: the hub table and stale-design flags need cross-turn AI state (Nova's AI is stateless per turn), slot-indexed roles have no equivalent (designs are not in a fixed 16-slot table), and packet behaviours need a packet production item.
12. **UI/messages:** the per-type field-count table for messages and the list of message types that share a filter setting; `extracted-game-data/default-names.txt` is cited by dynamic-string-table.md but is not in the repo.
13. **Mine laying:** the 98% curve is demoted to unconfirmed; the flat /2 gated by fleet flag 0x20 ("has not moved") implies a moving layer lays half, which the spec never states outright.
14. **Bombardment:** `FUN_10f0_6ea2`'s relationship rule was not re-derived (it replaces the "cooperative meeting" mechanic).

### Decisions for you (not spec gaps)

- **Mini Morph / Genesis Device grants:** removing them from battle salvage (as the spec requires) makes both unobtainable until the Mystery Trader is built. Build the Trader first, keep them grantable meanwhile, or accept unobtainable?
- **Galaxy-size index:** the port has a continuous map size; the spec's events need a discrete index. Suggested `s = clamp(MapWidth / 400 - 1, 0, 4)`.
- **Retracted-spec tests** (listed in "Where spec-9 overturns earlier work" #16) must be rewritten alongside the code, not left asserting the old behaviour.

---

## What's next

Suggested implementation order once you have seen the gaps above (each item: implement, test, verify the test fails against the old behaviour):

1. **Data and gate bugs** (small, high leverage): the `<NRSE>`/`"NRS"` mismatch; apply race restrictions in `StarUpdateStep.TechLevelUp`; the `components.xml` fixes (robot ARM/OBRM gates, Mine Dispenser/Speed Trap SD gates, Energy Dampener, Gravity Terraform ±11, Gravity ±15 Biotech, cloak points, Langston Shell +65).
2. **Revert the retracted work:** +10% population back to OBRM; AR capacity x100 with the Starter Colony; Interstellar Traveler vanish 0 and the single-roll overgating; Ultimate Recycling same-turn blend; Regenerating Shields armor/zero-shield rules; the over-capacity dead band; the depletion clamp.
3. **Combat correctness:** wire beam deflectors; `Computer.operator+`; sapper rules; accuracy formula; capacitors, Chebyshev reach, truncating damage order; capital-missile timing; gatling; overflow.
4. **Scores and victory:** the exact score record, shared ranks, elimination.
5. **Random events and the Mystery Trader** (fully specified now; needs the galaxy-size-index decision and, for the Trader, a new special-object type).
6. **AI:** the colonisation/freighter retractions touch the most tests; the end-of-pass top-up and bomber advisor are the most contained pieces.

Pick a lane, or tell me which spec gaps you want to close first.
