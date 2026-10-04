# Behavior-Specs-7 Coverage — full game functionality audit

Audit date: 2026-09-28. Covers `docs/behavior-specs-7/` (18 files, roughly 2x-3x larger than
behavior-specs-6 in every file — this is a substantial content expansion, not a small delta) plus
`component-stats.tsv` (a raw binary data-table extraction supporting `ship-design-and-components.md`).

**Method**: one research agent per spec file, working independently and in parallel, each reading
its assigned file in full and cross-referencing every documented behavior against the current
codebase (`Common/`, `ServerState/`, `Nova.Ai/`, `Nova.Avalonia/`, `Nova/WinForms/` as legacy
reference, `Tests/`). Each row below is Implementation status (✅ Full / ⚠️ Partial / ❌ None / N/A)
and Test status (✅ Yes / ⚠️ Partial / ❌ No / N/A), with file:line evidence.

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
| ai-opponent-behavior.md | 17/38 | 17/38 |
| client-interface.md | 34/80 | 0/80 |
| client-ui-dialog-catalog.md | 26/45 | 4/45 |
| combat-resolution.md | 26/33 | 10/33 |
| diplomacy-relations.md | 9/11 | 4/11 |
| dynamic-string-table.md | 6/24 | 3/24 |
| fleet-movement-scanning-cargo.md | 24/51 | 15/51 |
| new-game-setup.md | 12/37 | 10/37 |
| population-growth.md | 22/25 | 19/25 |
| production-queue.md | 13/25 | 12/25 |
| race-designer-ui-and-availability.md | 22/31 | 4/31 |
| race-traits.md | 26/38 | 23/38 |
| research-tech-tree.md | 19/31 | 5/31 |
| save-turn-file-format.md | 2/14 | 2/14 |
| ship-design-and-components.md (+ .tsv) | 29/38 | 7/38 |
| turn-generation-engine.md | 11/38 | 8/38 |
| victory-conditions.md | 14/16 | 1/16 |
| ~~tutorial-system.md~~ | *excluded* | *excluded* |
| **Total** | **~280/549 (≈51%)** | **~121/549 (≈22%)** |

These totals are a rough aggregate across agents who each used slightly different row granularity
(some split one mechanic into 3 rows, others kept it as 1) — treat the percentage as directional,
not a precise score. The real value is in the per-row detail and the standout findings below, not
the top-line number.

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
4. ~~**Ultimate Recycling's scrap-fleet resource bonus is credited immediately, not deferred to next
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
5. ~~**Defense and Terraform base costs are stale.**~~ **FIXED.** Both now select the spec's exact
   PRT-branch costs (Defense 44/25/48, Terraform 110/70/120) in place of the old superseded
   community-guess figures (15 flat, 100/70), with Inner Strength's/Total Terraforming's discounts
   layered on top. *(production-queue.md)*
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
9. ~~**AI freighter routing's reachability cutoff is off by a factor of 180**~~ **FIXED** — now
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
14. **The auto-build population-throttle and Stargate speed-selection features built this session
    remain either unconfirmed or now spec-confirmed**: Stargate speed selection is now a clean
    ✅ CONFIRMED match; the auto-build throttle rate is still simply not addressed either way by any
    spec revision.
15. **Two entirely new mechanics this revision documents for the first time have zero implementation
    footprint**: Alternate Reality's automatic (non-queued) Mineral Alchemy conversion, and a
    "Planet Rebirth" disaster event that resets a queue's investment. *(production-queue.md)*

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
| 1 | Personality dispatch: 0-7 code selects driver, shared decision skeleton | ⚠️ | ✅ | `Nova.Ai/DefaultAi.cs:51-68,116-181`. Only one shared code path exists (not 6 distinct driver bodies). `Tests/UnitTests/DefaultAiPersonalityTest.cs`. |  |
| 2 | Named AI personality/difficulty exposed in setup UI vs abstract difficulty | N/A | N/A | UI/dialog-string forensics belongs to new-game-setup.md; no independent AI-behavior claim. |  |
| 3 | "Nearest match"/"best match" generic search primitives | ✅ | ✅ | `Nova.Ai/ColonizationTargetSelector.cs:103-120,230-266`. `Tests/UnitTests/AiTargetSelectionTest.cs`. |  |
| 4 | Colonization-target scoring (habitability + distance bands) | ⚠️ | ✅ | `ColonizationTargetSelector.cs:97-161` (self-documented reconstruction, not bit-verified). |  |
| 5 | Probabilistic acceptance (roll under score) | ✅ | ✅ | `ColonizationTargetSelector.cs:163-170`. |  |
| 6 | "Too eager" flag — extra flat 25% rejection | ✅ | ⚠️ | `ColonizationTargetSelector.cs:172-178` (`TooEagerScoreThreshold=80`). |  |
| 7 | Late-game distance-conservative colonization (turn 59+) | ✅ | ⚠️ | `ColonizationTargetSelector.cs:197-224`. No test exercises `currentTurn > 59`. |  |
| 8 | Random exploratory colonization (reservoir sampling, expanding radius) | ✅ | ✅ | `ColonizationTargetSelector.cs:230-266`. |  |
| 9 | Starbase mineral transfer (700 threshold, 3-way distance-weighted split) | ⚠️ | ⚠️ | `Nova.Ai/FreighterRoutingSelector.cs:63-64` — not starbase-specific, picks one mineral only, no 3-way split. |  |
| 10 | Starbase build decisions: Stargate/orbital build, colonist redirect, scrap/consolidate | ❌ | ❌ | Not found anywhere in `Nova.Ai/`. |  |
| 11 | Combat-readiness gate (distance>14, cargo>499, weapon-slots>15) | ⚠️ | ✅ | `Nova.Ai/CombatReadinessAdvisor.cs:58-81` — implemented but **not wired to any real fleet action** (no invasion/attack targeting uses it). `Tests/UnitTests/CombatReadinessAdvisorTest.cs`. |  |
| 12 | Base threat rating (4-6 + jitter) | ✅ | ✅ | `Nova.Ai/ThreatAssessment.cs:63-66`. `Tests/UnitTests/ThreatAssessmentTest.cs`. |  |
| 13 | Aggregate threat from nearby enemy fleets | ✅ | ✅ | `ThreatAssessment.cs:73-99`. |  |
| 14 | Defense-need evaluator (threshold formula, minefield cap, coin-flip, passive/turn-30 skip) | ✅ | ✅ | `ThreatAssessment.cs:106-120`. **Fixed**: `DefaultAi.DoMove` (`DefaultAi.cs:178-191`) now also skips `HandleDefense()` once `gameTime > 30`, alongside the existing personality-based skip - no separate "aggressiveness flag" concept exists elsewhere to check independently, so this treats "past turn 30" as the gate itself, matching the spec's own imprecise ("roughly") framing. Not independently unit-tested: `DefaultAi.DoMove`'s branching isn't unit-tested anywhere in this codebase (per `DefaultAiPersonalityTest.cs`'s own comment) since it needs a fully-initialized game save; verified by inspection instead. |  |
| 15 | Freighter routing (nearest reachable shortfall, double-assign avoidance, fallback) | ✅ | ✅ | `Nova.Ai/FreighterRoutingSelector.cs:95-176`. |  |
| 16 | **180 ly² reachability cutoff — BUG** | ✅ | ✅ | **Fixed**: `FreighterRoutingSelector.cs:75` now uses `ReachableCutoffSquared = 180.0 * 180.0` (32,400), matching the spec. `Tests/UnitTests/AiTargetSelectionTest.cs`. |  |
| 17 | Value-per-time-of-arrival scoring (normalized by warp-5²=25) | ⚠️ | ❌ | `FreighterRoutingSelector.cs:183-189`, self-documented as a reconstruction, not bit-verified. |  |
| 18 | Production-advisor fixed decision chain (overall shape) | ⚠️ | ⚠️ | `Nova.Ai/DefaultPlanetAI.cs:77-162` implements factories/mines/ships/defense-% only. |  |
| 19 | Defense-percentage advisor (decaying act-chance, 5% floor) | ✅ | ✅ | `Nova.Ai/DefensePercentageAdvisor.cs:46-55`. `Tests/UnitTests/DefensePercentageAdvisorTest.cs`. |  |
| 20 | Mass-driver/mineral-packet advisor | ❌ | ❌ | Not found. |  |
| 21 | Two further component-category advisors (cap 4) | ❌ | ❌ | Not found. |  |
| 22 | Resource-output percentage discount (gated by global flag) | ❌ | ❌ | Not found. |  |
| 23 | Auto-load colony cargo (surplus > 49 triggers load) | ❌ | ❌ | Not found (different from the pre-existing new-colonization cargo load). |  |
| 24 | Production-queue insertion cap (200 items) | ✅ | ❌ | `DefaultPlanetAI.cs:50,72-75`. No dedicated test. |  |
| 25 | Isolation check gating expansion investment (distance bands, >59 stars) | ❌ | ❌ | Not found. |  |
| 26 | Ship auto-design pipeline (assemble from hull + best components) | ⚠️ | ✅ | Only transport design auto-assembled (`DefaultAIPlanner.cs:174-262`, `ShipDesignRefresher.cs:101-118`). |  |
| 27 | Age-based design refresh scheduling (35/50-turn thresholds, grace period, category rotation) | ⚠️ | ✅ | `ShipDesignRefresher.cs:56-79` implements only the collapsed 35-turn threshold; urgency tiers/grace/rotation not implemented. |  |
| 28 | Obsolete-flag immediate replacement | ❌ | ❌ | Deliberately excluded (`ShipDesignRefresher.cs:36-44`). |  |
| 29 | Fleet turn-processing order randomization | ⚠️ | ❌ | Implemented (`DefaultAi.cs:141-144`), but spec's own §8 now **retracts** this as an unconfirmed legacy misattribution — Nova still does it regardless (harmless). |  |
| 30 | Planet per-turn processing-order shuffle (newly-confirmed real mechanism) | ✅ | ⚠️ | **Fixed**: `DefaultAi.cs` now Fisher-Yates shuffles `shuffledPlanetAIs` once per turn (mirroring the existing fleet shuffle), and `HandleProduction` iterates that instead of `planetAIs.Values` directly. Not independently unit-tested: `DoMove`'s branching isn't unit-tested anywhere in this codebase (needs a fully-initialized game save), consistent with the existing fleet-shuffle's own untested status. |  |
| 31 | Player (empire) turn-generation order shuffle (newly-confirmed real mechanism) | ⚠️ | ✅ | **Partially fixed**: `ServerData.ShuffledEmpireOrder` is now computed once per turn (`TurnGenerator.Generate()`) and used by the main fleet-processing loop and `RemoteMiningStep`'s per-star contested-mining order. This codebase's step-pipeline architecture (batch-processes all empires per step) has no equivalent to the original's true per-player-sequential dispatch, so the shuffle is applied at the specific places within that architecture where empire order provably affects a shared outcome, not universally rethreaded through every fleet-touching pass (`BattleEngine`, `ScrapFleetStep`, `BombingStep`, `Scores` still use the old fixed `IterateAllFleets()` order - not audited this pass for whether shuffling them is safe). `Tests/UnitTests/EmpireOrderShuffleTest.cs`, `Tests/UnitTests/RemoteMiningOrderFairnessTest.cs`. |  |
| 32 | Colonizer commitment: cargo-capacity threshold (5,000) | ✅ | ✅ | `Nova.Ai/ColonizerCommitmentAdvisor.cs:57,64-67`. `Tests/UnitTests/ColonizerCommitmentAdvisorTest.cs`. |  |
| 33 | Funding-source distance gate (squared-distance 10,000) | ✅ | ✅ | `ColonizerCommitmentAdvisor.cs:61,75-78`. |  |
| 34 | Design tech-upgrade ambition ladder + research-cost budget query | ❌ | ❌ | Deliberately not ported — spec gives no modulation formula. |  |
| 35 | Persistent per-decision audit-trail bitmask | N/A | N/A | Architecturally inapplicable — stateless per-turn AI. |  |
| 36 | Stale-slot sweep utility | N/A | N/A | Spec itself can't determine real-world meaning. |  |
| 37 | 16-slot pool redistribution | N/A | N/A | Same. |  |
| 38 | Production-catalog item identities | N/A | N/A | Belongs to production-queue.md. |  |

**Summary**: 17/38 implemented, 17/38 tested. (Rows 30 and 31's turn-order shuffles fixed this pass, plus row 14's turn-30 defense gate from an earlier pass in the same session.)

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
| 10 | "Tracked object" persistence encoding | N/A | N/A | No such feature exists to encode. |  |
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
| 34 | [Map] Tracked-object diamond marker | ❌ | ❌ | No "tracked object" concept distinct from selection. | Implement |
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
| 53 | Message view: 4 category-selection controls | ❌ | ❌ | Flat unfiltered list. | Only for Windowed UI |
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
| 14 | Slot editor graded compatibility | ⚠️ | ❌ | Binary compatible/incompatible only, no tech-distance grading. |  |
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
| 25 | Messages viewer (categories, Next/Prev, read/unread filter) | ⚠️ | ❌ | Flat list with battle-replay linking only. | Fix |
| 26 | Reports grid (4 types, toggleable columns, idle/ETA) | ⚠️ | ❌ | Separate simple tables per type; no shared grid, no idle/ETA column. | Fix |
| 27 | Report TSV/plain-text export | ❌ | ❌ | Not found. | Skip |
| 28 | Score display (3 cyclable modes, 9 categories) | ⚠️ | ❌ | Single-mode, current-turn-only. | Fix |
| 29 | Tutorial surface | ❌ | ❌ | Excluded per your direction. | Agreed |
| 30 | Event replay/Battle VCR (10×10 board, animation) | ⚠️ | ⚠️ | Plain stepped text log; richer board exists WinForms-only. | Implement |
| 31 | Map printing | ❌ | ❌ | Not found. | Skip for all |
| 32 | Progress indicator lifecycle | ❌ | ❌ | **Confirmed a total, deliberate gap** — Avalonia has NO progress UI for any lengthy operation, not just one path: `GameSession.cs:25-32`'s own comment explains the legacy WinForms `ProgressDialog` deadlocks when hosted outside a classic WinForms `Application`, so component loading now runs headless/synchronous with nothing shown at all. | Skip, unlikely to need this as modern computers so fast there's no discernable delay. |
| 33 | Hidden diagnostic/anti-cheat log-writing path | N/A | N/A | Original-binary leftover, not wanted. | Skip |
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

**Summary**: 26/45 implemented, 4/45 tested.

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
| 9 | Starbase +1 range bonus + fleeing-ships-misjudge-range quirk | ❌ | ❌ | Not found. |  |
| 10 | 7-way target-type classifier | ⚠️ | ✅ | `BattleEngine.cs:683-715`; Freighters/Fuel Transports are a disclosed heuristic substitute. |  |
| 11 | Attractiveness formula | ✅ | ✅ | `BattleEngine.cs:734-798` — **Fixed**: removed the beam range-falloff term the spec's §4 exhaustively confirms doesn't exist in the real targeting formula (it belongs only to the separate beam range-dissipation formula applied to actual damage, §6). `Tests/UnitTests/BattleAttractivenessRangeTest.cs`. |  |
| 12 | Initiative computation (PRT bonus, 0-8 clamp, mass penalty) | ⚠️ | ❌ | `Common/Components/ShipDesign.cs:463-478` — hull+computer only, no PRT bonus/clamp/mass term. |  |
| 13 | Firing-order tie-break (range then persistent coin-flip) | ⚠️ | ❌ | `ServerState/WeaponDetails.cs:58-69` — unstable sort, not a persisted coin-flip. |  |
| 14 | Per-weapon-slot firing, ShipsInToken scaling | ✅ | ❌ | `BattleEngine.cs:1445-1471`. |  |
| 15 | Beam range dissipation | ✅ | ❌ | `BattleEngine.cs:1458-1463`. |  |
| 16 | Beam deflector stacking (0.9^n) | ✅ | ❌ | `Common/Components/ProbabilityProperty.cs:118-137`. |  |
| 17 | Shields absorb before armor | ✅ | ❌ | `BattleEngine.cs:1273-1290,1354-1387`. |  |
| 18 | Torpedo/missile independent per-missile hit/miss | ✅ | ❌ | `BattleEngine.cs:1307-1345` — matches spec's worked example exactly. |  |
| 19 | Capital missile double damage post-shield-depletion | ⚠️ | ❌ | Approximated via all-or-nothing check, not precise prorating (disclosed). |  |
| 20 | Accuracy formula (jam/computer subtractive cancellation) | ❌ | ❌ | `BattleEngine.cs:1489-1501` uses a simpler approximation, self-flagged as unconfirmed — spec now fully confirms the real formula. |  |
| 21 | Whole-ship-kill division + 1/500 armor-quantization exploit | ⚠️ | ❌ | Correct division, but per-token not per-1/500-quantized (disclosed). |  |
| 22 | Energy/flux capacitor beam-damage bonus | ❌ | ❌ | `BattleEngine.cs:1466-1469` — explicit "not modeled" comment. |  |
| 23 | Gatling-type "hits-all" fire mode | ❌ | ❌ | `WeaponType.gatlingGun` exists in data but no multi-target logic. |  |
| 24 | Battle-end conditions | ✅ | ⚠️ | `BattleEngine.cs:576-593`. |  |
| 25 | Disengage retreat (7 squares) | ✅ | ✅ | `BattleEngine.cs:47,972-991`. `Tests/UnitTests/BattleTargetTypeAndDisengageTest.cs`. |  |
| 26 | Salvage = 1/3 mineral cost | ✅ | ❌ | `BattleEngine.cs:238-259`. |  |
| 27 | Deep-space salvage decay | ✅ | ❌ | `Common/GameObjects/DeepSpaceMinerals.cs:67-87`. |  |
| 28 | Tech-gain-from-battle | ⚠️ | ❌ | `BattleEngine.cs:1544-1594` — uses Nova's existing 6-field system, not the original's undeciphered weighted tables. |  |
| 29 | Planetary bombing | ✅ | ⚠️ | `ServerState/Bombing.cs:48-161`. |  |
| 30 | Component diminishing-returns stacking, 63%-cap distinction | ⚠️ | ❌ | Generic formula used uniformly; no 63%-cap variant. |  |
| 31 | Canonical component stat tables | ✅ | N/A | Spot-checked against spec §10a — exact match. |  |
| 32 | Cloaking: piecewise curve, PRT baseline, Tachyon Detector table | ✅ | ✅ | **Fixed**: `Common/Components/CloakCalculator.cs` implements the decompiled piecewise raw-units-to-percent curve and the 18-entry Tachyon Detector counter-cloak table; `ShipDesign.SumProperty`/`Update` now sum raw cloak units per design (Super Stealth's flat +300 baseline, and a back-solved +40 for Improved Starbases — disclosed as not decompiled like SS's figure, see the code comment) instead of the old flat hardcoded 20% (`ServerState/Manufacture.cs`'s ISB special-case, now removed as redundant); `Fleet.RecalculateCloak` combines a fleet's designs as a mass-weighted average; `ScanStep.cs` applies the Tachyon Detector multiplier to the target's cloak before the effective-scan-range check. `Tests/UnitTests/CloakingTest.cs` (21 tests: curve breakpoints, detector table, raw-unit summation vs. probability-combination, SS/ISB baselines, fleet mass-weighting, and a full `ScanStep` detector-extends-range integration test). |  |
| 33 | Per-hull weapon-slot firing order | N/A | N/A | Nova aggregates weapons per design, no discrete slot-order concept. |  |

**Summary**: 26/33 implemented, 10/33 tested (rows 11 and 32 now tested after this pass's fixes).

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
| 7 | Relationship gates cooperative colonist/mineral/fuel "meetings" | ❌ | ❌ | Not found — only combat/bombing/invasion consume relations. |  |
| 8 | Invasion requires Enemy relation | ✅ | ❌ | `Common/Waypoints/InvadeTask.cs:110-129`. |  |
| 9 | Battle Plan Attack resolves 5 categories from relation table | ✅ | ✅ | `BattlePlan.cs:88-91`; `BattleEngine.cs:828-860`. `Tests/UnitTests/BattlePlanAttackPolicyTest.cs`. |  |
| 10 | Same resolution reused for bombing | ✅ | ⚠️ | `Bombing.cs:66`. |  |
| 11 | No built-in AI ever submits a relation-change order | ✅ | ❌ | Confirmed via grep across `Nova.Ai/`. |  |

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
| 23 | Wormhole heavy-mineral-cargo transit | ❌ | ❌ | Explicitly left unimplemented (disclosed). |  |
| 24 | Base cargo capacity per hull, fuel as separate pool | ✅ | ❌ | `Fleet.cs:387-427`. |  |
| 25 | Cargo-pod bonuses (+50/+100 kT) | ✅ | ❌ | `components.xml` — matches exactly. |  |
| 26 | Multi Cargo Pod (+250 kT third tier) | ❌ | N/A | Component doesn't exist in `components.xml`. |  |
| 27 | Fuel-tank bonuses (+250/+500mg) | ✅ | ❌ | `components.xml` — matches exactly. |  |
| 28 | Anti-matter Generator +200mg fuel bonus | ✅ | ❌ | `components.xml` — matches exactly. |  |
| 29 | Fixed-amount Load/Unload cargo task | ✅ | ❌ | `Common/Waypoints/CargoTask.cs:180-228`. |  |
| 30 | Conditional "Set Waypoint to `<amount>`" cargo automation | ❌ | N/A | Only fixed-amount supported. |  |
| 31 | Fill-to-percentage / "Load Optimal" cargo modes | ❌ | N/A | Not found. |  |
| 32 | Inner Strength: colonists reproduce in cargo hold | ❌ | N/A | Not found. |  |
| 33 | 100-vs-25 colonist/fuel transfer-chunk divisor (MA) | ❌ | N/A | Transfers are instantaneous, no chunking. |  |
| 34 | Cargo via Stargate: fuel-only except IT | ✅ | ✅ | `TurnGenerator.cs:789-799`. `StargateJumpTest.cs`. |  |
| 35 | Theft: Pick Pocket/Robber Baron cargo siphon | ❌ | N/A | Not found. |  |
| 36 | Stargate overgating damage formulas | ✅ | ✅ | `TurnGenerator.cs:801-856`. `StargateJumpTest.cs`. |  |
| 37 | Overgating vanish-chance + IT reduced risk | ✅ | ✅ | `TurnGenerator.cs:883-892`. |  |
| 38 | No dynamic interception (static waypoint x/y) | ✅ | ⚠️ | `Common/Waypoints/Waypoint.cs`. |  |
| 39 | Repeat Orders flag | ❌ | N/A | Not found. |  |
| 40 | 10-entry waypoint-task table; Patrol/Route/Transfer Fleet absent | ❌ (3/10) | N/A | Confirmed still missing. |  |
| 41 | Patrol: radius-gated automatic hostile-pursuit | ❌ | N/A | Not implemented. |  |
| 42 | Route: redirect remaining orders through another object | ❌ | N/A | Not implemented. |  |
| 43 | Transfer Fleet: cross-player gift (512-cap check) | ❌ | N/A | Not implemented; the 512-fleet cap constant is defined but never enforced anywhere. |  |
| 44 | **Scrap Fleet recovery rate — now confirmed stale** | ⚠️ | ❌ | `ScrapTask.cs:109-131` uses 33/80/90/45%; spec now resolves the real values as 33/25/20/10/5%. |  |
| 45 | Ultimate Recycling: resources deferred to next turn | ✅ | ✅ | **Fixed**: see standout finding #4 - `Star.DeferredScrapResources`, credited by `StarUpdateStep` only after this turn's own production spend. `Tests/UnitTests/UltimateRecyclingDeferredResourcesTest.cs`. |  |
| 46 | Waypoint-task feasibility/partial-fulfillment with stochastic loss | ❌ | N/A | Tasks either fully succeed or fully fail. |  |
| 47 | Split Fleet/Merge Fleets | ✅ | ⚠️ | `Common/Waypoints/SplitMergeTask.cs`. |  |
| 48 | Merge Fleets probabilistic fuel-stranding | ⚠️ | ✅ | Whole-fleet-fraction approximation, disclosed. `FleetMergeFuelShortfallTest.cs`. |  |
| 49 | Post-merge ability-stat recombination (scanner max, 12-slot sum cap) | ❌ | N/A | Not found. |  |
| 50 | Fleet creation fills smallest unused ID (gap reuse) | ❌ | N/A | Monotonic counter instead, never reuses freed IDs. |  |
| 51 | Remote Mining (fleet-based, unowned-star extraction) | ✅ | ✅ | `ServerState/TurnSteps/RemoteMiningStep.cs`. |  |

**Summary**: 24/51 implemented, 15/51 tested. (Row 9's fuel-generation bug fixed previously; row 16 and row 45's Ultimate Recycling deferral fixed this pass.)

---

### new-game-setup.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Two setup paths: Simplified vs Detailed wizard | ❌ | ❌ | One unified tabbed screen instead. |  |
| 2 | Galaxy Size: 5 discrete options | ❌ | ❌ | Continuous MapWidth/Height sliders instead. | Agreed, skip |
| 3 | Star Density: 4 discrete options | ❌ | ❌ | Continuous 1-100 slider instead. | Agreed, skip |
| 4 | Starting Distance setting | ❌ | ❌ | Not found. |  |
| 5 | 7 boolean game-option flags | ⚠️ | ❌ | Only "Accelerated Start" (≈Accelerated BBS Play) exists. |  |
| 6 | "Beginner: Maximum Minerals" forces flat concentration | ❌ | ❌ | Not found. |  |
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
| 21 | Per-player starting fleet keyed by PRT | ✅ | ✅ | `StarMapInitialiser.cs:423-950`. Extensively tested in `Tests/IntegrationTests/NewGameTest.cs`. |  |
| 22 | Second home planet for PP/IT on non-tiny galaxies | ⚠️ | ✅ | Implemented but without the galaxy-size gate. |  |
| 23 | Batch/scripted game-creation format | ❌ | ❌ | Only simple CLI switches exist, not the full format. |  |
| 24-28 | Various binary-record/decompilation-only mechanics | N/A | N/A | Different architecture, not comparable 1:1. | skip |
| 27 | Bulk default-relation bookkeeping flag | ❌ | ✅ (different mechanism) | Unconditionally sets Neutral instead of a bookkeeping bit. |  |
| 28 | 22-entry named starting-ship template table | N/A | N/A | Rebuild hardcodes named designs per PRT branch instead. | Agreed |
| 29 | Per-PRT starting-ship list for all 10 PRTs | ⚠️ | ⚠️ | Plausible ships granted but different names/hulls than spec's table. | Fix |
| 30 | Per-PRT default diplomatic relation presets | ❌ | ❌ | Everyone starts Neutral regardless of PRT. | Fix |
| 31 | Unique display-name assignment (~20 retries + fallback) | ⚠️ | ❌ | Different retry/fallback trigger shape. |  |
| 32 | Home-star selection via eligibility + nearest-to-anchor search | ⚠️ | ✅ | Much simpler pre-reserved-position approach instead. | Fix |
| 33 | Isolation-distance check | N/A | N/A | Belongs to ai-opponent-behavior.md; not found there either. |  |
| 34 | Game-name/file validation with cursor/status handling | ⚠️ | ❌ | Validates via try/catch + StatusMessage; no busy-cursor state. | Skip |
| 35 | New-game commit reuses master turn-generation routine | ✅ (different means) | ⚠️ | Structurally different but functionally analogous. | Skip |
| 36 | Save-As common file dialog | ⚠️ | ❌ | Cross-platform `PlatformHooks` abstraction instead. | Skip |
| 37 | "Reset settings to defaults + regenerate" | ❌ | ❌ | Not found. |  |

**Summary**: 12/37 implemented, 10/37 tested. (Rows 19 and 20's mineral concentration/tonnage bugs fixed this pass.)

---

### population-growth.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Per-axis tolerance → normalized clicks-from-center | ✅ | ✅ | `Race.NormalizeHabitalityDistance`, `Common/RaceDefinition/Race.cs:310-324`. |  |
| 2 | Immunity pins an axis to its best value | ✅ | ✅ | `Race.cs:312-315`. |  |
| 3 | In-band habitability combination formula | ⚠️ | ⚠️ | Only the older boxed sqrt formula implemented; spec's alternate reconstruction not distinguished by tests. | Fix |
| 4 | Out-of-band malus additive across axes, each capped at 15 | ✅ | ✅ | `Race.cs:168-186`. |  |
| 5 | Single-axis malus hard-capped at 15 (not doubled for TT) | ✅ | ✅ | `Race.GetMaxMalus()` `Race.cs:283-286`. |  |
| 6 | Growth crowding formula (16/9)(1-capPct)² above 25% | ✅ | ✅ | `Star.CalculateGrowth`, `Common/GameObjects/Star.cs:340-403`. |  |
| 7 | **Population capacity should be habValue×maxPopulation — BUG** | ✅ | ✅ | **Fixed**: `Star.Capacity(Race)` now scales `race.MaxPopulation` by `race.HabValue(this)` before dividing (still 25,000 flat for non-positive habitability, unchanged). `Tests/UnitTests/PopulationCapacityTest.cs`. |  |
| 8 | Hyper Expansion doubles growth-rate | ✅ | ⚠️ | `Global.GrowthFactorHyperExpansion=2`. |  |
| 9 | Hyper Expansion halves max population | ✅ | ❌ | `Global.PopulationFactorHyperExpansion=0.5`. |  |
| 10 | Jack of All Trades +20% population capacity | ✅ | ❌ | `Global.PopulationFactorJackOfAllTrades=1.2`. |  |
| 11 | **Inner Strength +10% pop-capacity bonus — wired to wrong trait** | ✅ | ✅ | **Fixed**: `Race.MaxPopulation` now gates the +10% bonus on `HasTrait("IS")` instead of `HasTrait("OBRM")` (renamed `Global.PopulationFactorOnlyBasicRemoteMining` → `PopulationFactorInnerStrength` accordingly). Note: this contradicts `race-traits.md`'s own row 31 ("OBRM: ... +10% max population"), which appears to have just matched the pre-existing (buggy) code rather than independently re-deriving the bit; this row's decompiled bit-9 trace is cross-confirmed by `ship-design-and-components.md`'s independent bit-9=Inner-Strength finding (the same bit gates the Bomb-category IS exclusions), so it was treated as the more authoritative source — see race-traits.md row 31, updated to flag this. `Tests/UnitTests/PopulationCapacityTest.cs`. |  |
| 12 | Alternate Reality: population capacity from orbital-base lookup | ❌ | N/A | No AR-specific branch; falls through to same flat logic as everyone else. |  |
| 13 | Negative-habitability decline shape | ⚠️ | ✅ | `Star.CalculateGrowth` matches `0.1×Colonists×habitalValue` exactly. |  |
| 14 | Decline floor + persisted fractional-carry byte | ❌ | ❌ | No fractional-carry state; truncates to nearest 100 each turn. |  |
| 15 | Deterministic fractional-carry-byte growth technique | ❌ | ❌ | Neither technique implemented. |  |
| 16 | **Population declines (not plateaus) past capacity+10 — now contradicted** | ✅ | ✅ | **Fixed**: `Star.CalculateGrowth`'s full-planet branch now implements the spec's traced decline formula (`n = max(99 - capPct1000/10, -300)`, `popChange = population*n/2500`), replacing the old "plateau at 0" branch; the two existing tests that asserted the superseded plateau behavior were updated to the new decline values, and a new test covers the "capacity to capacity+9 still zero growth" window. `Tests/UnitTests/StarTest.cs`. |  |
| 17 | Global-flag growth-rate-halving quirk | N/A | N/A | Correctly not modeled — spec itself says it's an internal marker, not a real trait. |  |
| 18 | No enforced minimum colonist count to found a colony | ✅ | ⚠️ | `ColoniseTask.IsValid` only checks nonzero cargo. |  |
| 19 | Fresh colony starts at 0% capacity, grows at growthRate×habValue | ✅ | ⚠️ | Implicit via `CalculateGrowth`'s low-capacity branch. |  |
| 20 | Homeworld: 100% habitability, 1M cap, starting resources | ✅ | ⚠️ | `StarMapInitialiser.cs` ~1026-1047. |  |
| 21 | Per-turn mined amount formula | ✅ | ✅ | `Star.GetMiningRate`/`MineForFleet`. `RemoteMiningStepTest.cs`. |  |
| 22 | Fractional kT rounded stochastically | ✅ | ⚠️ | `Global.StochasticRound` — matches exe algorithm exactly, no test exercises the random branch itself. |  |
| 23 | **Concentration depletion curve breakpoints — now confirmed wrong** | ✅ | ✅ | **Fixed**: `Star.KtToDropOnePoint` now implements the spec's decompiled 2-tier curve (`12500 / effectiveConcentration`, clamped to 25 for concentration 5-24 and 10 below 5), replacing the old community-sourced 27/462/1000/2000 figures. `Tests/UnitTests/ConcentrationDepletionCurveTest.cs`. |  |
| 24 | Mine efficiency shouldn't accelerate concentration loss | ✅ | ✅ | **Fixed**: `Star.KtToDropOnePoint` now takes the mining race's `MineProductionRate` and scales the kT-per-point threshold proportionally (`1250 * mineProductionRate / effectiveConcentration` — reduces to the unscaled formula at the baseline rate of 10), threaded through `Star.Mine`/`ApplyMining`/`MineForFleet` (now takes an explicit `mineProductionRate` parameter) and `RemoteMiningStep.cs` (looks up the mining fleet's OWNING empire's race, not the star owner's, since remote mining applies at unowned/foreign stars too). A higher-efficiency race now extracts more minerals per point of concentration lost, instead of draining the planet faster. `Tests/UnitTests/MineEfficiencyThresholdScalingTest.cs`. |  |
| 25 | Remote-mining fleet cap of 4,000 mine-equivalents | ✅ | ❌ | `Global.MaxRemoteMiningEquivalents=4000`. |  |
| 26 | Multiple mining sources deplete concentration in sequence | ✅ | ⚠️ | `ApplyMining` shares state across `Mine`/`MineForFleet`. |  |
| 27 | Mining summary popup window chrome | N/A | N/A | Original client UI detail, not a gameplay mechanic. | Agreed, skip |

**Summary**: 22/25 implemented, 19/25 tested. (Rows 7, 11, 16, and 23's bugs fixed across earlier passes; row 24's mine-efficiency scaling fixed this pass.)

---

### production-queue.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Population resources = floor(pop/setting) | ✅ | ⚠️ | `Common/GameObjects/Star.cs:216`. |  |
| 2 | Resources per factory + build cost (CF trait -1kT) | ✅ | ⚠️ | `Common/RaceDefinition/Race.cs:330-334`. |  |
| 3 | Operable factories/mines population cap ("grow into infrastructure") | ✅ | ✅ | `Star.cs:110-124,148-158,182-196`. `Tests/UnitTests/ProductionQueueAutoBuildTest.cs`. |  |
| 4 | Auto-build population-support throttle (this session's feature) | ✅ (not spec-sourced) | ✅ | `Common/Production/ProductionOrder.cs:128-151` — **spec-7 is silent on this specific pacing rate**, neither confirming nor contradicting. |  |
| 5 | Mines: independent per-mineral extraction, 30% home floor | ✅ | N/A | `Star.cs:265-285` (30% floor covered by population-growth.md). |  |
| 6 | Race-design economic settings table (7 settings) | ✅ (data model) | ❌ | Fields exist; wizard-bounds enforcement is a different spec's scope. |  |
| 7 | Defenses: type upgrades, 100/planet hard cap | ⚠️ | ⚠️ | Cap enforced; no defense-type concept exists at all (structural gap, not a formula tweak). |  |
| 8 | **Defense build cost: PRT-keyed 25/44/48 — now confirmed stale** | ✅ | ✅ | **Fixed**: `DefenseProductionUnit.cs:87-94` now selects 44/25/48 by PP/IT, with Inner Strength's 40%-off discount layered on top. `Tests/UnitTests/DefenseAndTerraformCostTest.cs`. |  |
| 9 | Terraforming: 1%/unit, axis auto-selection, tech-gated cap | ✅ (core) / ⚠️ (variants) | ⚠️ | `TerraformProductionUnit.cs:101-125`; no Min/Max item-type distinction. |  |
| 10 | **Terraform cost: PRT-keyed 70/110/120 — now confirmed stale** | ✅ | ✅ | **Fixed**: `TerraformProductionUnit.cs:66-79` now selects 110/70/120 by PP/IT, with Total Terraforming's 30%-off discount layered on top. `Tests/UnitTests/DefenseAndTerraformCostTest.cs`. |  |
| 11 | Mineral Alchemy (100/25 res → 1kT each mineral) | ✅ | ⚠️ | `AlchemyProductionUnit.cs:58-63,116-133`. |  |
| 12 | **Alternate Reality automatic non-queued Alchemy conversion — new mechanic, zero footprint** | ❌ | ❌ | Not found anywhere. |  |
| 13 | Queue is strict top-to-bottom | ✅ | ✅ | `ServerState/Manufacture.cs:50-88`. |  |
| 14 | Insertion pauses (doesn't erase) progress; deletion forfeits | ✅ | ⚠️ | Implicit in top-down walk. |  |
| 15 | Mineral shortfall blocks ordinary items but not auto-build | ✅ | ✅ | `ProductionOrder.IsBlocking`. `ProductionQueueAutoBuildTest.cs`. |  |
| 16 | "Leftover only" per-planet checkbox semantics | ✅ | ❌ | `StarUpdateStep.cs:57-64,117-127`. |  |
| 17 | **Planetary disaster ("Planet Rebirth") — new mechanic, zero footprint** | ❌ | ❌ | Not found anywhere. |  |
| 18 | 100-year "practically never" completion simulator | ✅ | ✅ | `Common/Production/ProductionCompletionEstimator.cs`. |  |
| 19 | Packed queue-record format: 1,023-unit/200-entry caps | ❌ | ❌ | Not enforced; UI's own ad hoc 1,000 clamp is coincidentally close. |  |
| 20 | Full queue-item color scheme (green/blue/red/gray) | ✅ | ✅ | `ProductionCompletionEstimator.cs:159-178`. Already confirmed in the v5 audit. |  |
| 21 | Manual batch add via Shift/Ctrl (×1/10/100/max) | ⚠️ | N/A | Ramping +/- stepper instead, disclosed intentional touch redesign. |  |
| 22 | Auto-build items are persistent "up to N" standing orders | ✅ | ✅ | `ProductionOrder.cs:107-183`. |  |
| 23 | 4-slot production template manager | ❌ | ❌ | Not found. | Implement something suitable not strictly equivalent |
| 24 | Default template auto-applies to new/captured colonies | ❌ | ❌ | New stars get an empty queue. | Implement "favourite or default" template which gets applied |
| 25 | "(Auto Build)"/"up to N" verbatim UI wording | ⚠️ | N/A | Functionally equivalent, different wording. | Agreed, skip |

**Summary**: 13/25 implemented, 12/25 tested. (Rows 8 and 10's Defense/Terraform cost bugs fixed this pass.)

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
| 9 | IS: defenses -40%, weapons +25%, invasion defender bonus | ✅ | ❌ | `DefenseProductionUnit.cs:83`, `ShipDesign.cs:723-724`, `InvadeTask.cs:168-171`. |  |
| 10 | IS: faster colonist healing, Speed Trap minefields, bomb restrictions | ❌ | ❌ | None found. |  |
| 11 | SD: starting mine-laying ships | ✅ | ✅ | `StarMapInitialiser.cs:549-564`. |  |
| 12 | SD: differentiated minefield decay/detonation, mines-as-scanner | ❌ | ❌ | Minefield decay is unimplemented for any race, not just undifferentiated for SD. |  |
| 13 | PP: second homeworld, mass accelerator, starting tech | ⚠️ | ✅ | Starting Energy tech deliberately deviates from spec's literal figure (documented rationale in code). |  |
| 14 | PP: packet terraform-on-arrival, penetrating scanner on packets | ❌ | ❌ | No mass-packet mechanic exists at all. |  |
| 15 | IT: second homeworld, starting destroyer+privateer, tech | ✅ | ✅ | `StarMapInitialiser.cs:354-389,619-652`. |  |
| 16 | IT: cargo-through-Stargate exemption | ✅ | ✅ | `TurnGenerator.cs:789-797`. `StargateJumpTest.cs`. |  |
| 17 | IT: Stargates -25% cost, unlimited range/mass, reduced destruction chance | ⚠️ | ✅ | **Cost fixed this pass**: `ShipDesign.cs` now applies IT's confirmed 25% Stargate-cost discount (component category 0x0200, Stargate subtypes only, not Mass Drivers). "Reduced destruction chance" is already implemented and tested separately - see fleet-movement-scanning-cargo.md row 37 (`TurnGenerator.cs:883-892`, `INTERSTELLAR_TRAVELER_VANISH_SCALE`). "Unlimited range/mass" has no separate enforcement to remove - a race can already build the "any/any" Stargate variant in `components.xml` regardless of PRT; not a differentiation gap so much as a design-choice availability question. `Tests/UnitTests/InterstellarTravelerAndCheapEnginesCostTest.cs`. |  |
| 18 | AR: population-on-starbases resource/scan formulas, starbase -20% | ✅ (core) | ❌ | `Star.cs:209-237`; missing edge cases (25% floor, over-capacity half-weighting). |  |
| 19 | AR: no planetary installations, in-transit population loss | ❌ | ❌ | None found. |  |
| 20 | JOAT: starting tech 3 all fields, +20% max population | ✅ | ✅/❌ | Tech confirmed and tested; population multiplier untested. |  |
| 21 | JOAT: built-in penetrating scanner on 3 hull types | ❌ | ❌ | Open TODO in code. |  |
| 22 | IFE: 15% less fuel, +1 starting Propulsion | ✅ | ❌ | `ShipDesign.cs:895-898`. |  |
| 23 | TT: free/no-tech terraform from turn 1, 30% cheaper | ✅ | ✅ | `TerraformProductionUnit.cs:63,94` — flat cap, not the spec's per-tech-level progression. |  |
| 24 | ARM: extra mining hulls, starting Midget Miners | ⚠️ | ❌ | Starting ship is a TODO stub. |  |
| 25 | ISB: extra starbase designs, -20% cost, +20% inherent cloak | ⚠️ | ⚠️ | Cost confirmed. **Cloak fixed this pass**: `ShipDesign.Update` now folds in a +40 raw-cloak-unit baseline on starbase hulls for ISB races (back-solved from `CloakCalculator`'s curve to reproduce the known 20% figure - disclosed as not decompiled the way SS's 300 is, see the code comment), replacing the old flat hardcoded `fleet.Cloaked = 20` in `ServerState/Manufacture.cs` (now removed as redundant). `Tests/UnitTests/CloakingTest.cs`. "Extra starbase designs" is still unverified. |  |
| 26 | UR: 90%/45% scrap recovery, deferred to next turn | ✅ | ✅ | **Fixed**: see standout finding #4 - the resources share is now correctly deferred a full turn via `Star.DeferredScrapResources`, rather than being silently destroyed the same turn it was credited. `Tests/UnitTests/UltimateRecyclingDeferredResourcesTest.cs`. |  |
| 27 | MA: 4x resource-to-mineral efficiency | ✅ | ❌ | `AlchemyProductionUnit.cs:60`. |  |
| 28 | GR: 50%+15%×5 split (125% aggregate) | ✅ | ❌ | `StarUpdateStep.cs:161-168`. |  |
| 29 | NRSE: removes ramscoop above Warp 4, grants Interspace-10 | ⚠️ | ❌ | Components exist in data; unconditional Warp-10 exemption unverified in code. |  |
| 30 | CE: engines 50% cheaper, +1 Propulsion, 10%/yr failure above Warp 6 | ✅ | ✅ | **Fixed**: `ShipDesign.cs` now applies Cheap Engines' confirmed 50% engine-cost discount (whole Engines category), alongside the already-implemented failure chance and +1 Propulsion. `Tests/UnitTests/InterstellarTravelerAndCheapEnginesCostTest.cs`. |  |
| 31 | OBRM: Mini-Miner restriction, +10% max population | ⚠️ | ❌ | Hull restriction unverified (data-only). **Correction**: the +10% max-population claim here does not hold up against `population-growth.md`'s own decompiled bit-level trace, which ties that exact bonus to Inner Strength (bit 9) instead — independently cross-confirmed by `ship-design-and-components.md`'s bit-9=IS finding via the Bomb-category exclusions. This row's original claim likely just matched the pre-existing code (which had the same bug) rather than independently re-deriving the bit. The bonus has been moved to Inner Strength in code; see `population-growth.md` row 11. |  |
| 32 | NAS: no pen-scanners, doubles conventional range | ✅ | ❌ | `ShipDesign.cs:749-756`. |  |
| 33 | LSP: 30% lower starting population | ✅ | ❌ | `Race.cs:404-407`. |  |
| 34 | **BET: 2x cost on unmet prereqs, differentiated miniaturization** | ✅ | ✅ | **Fixed**: see research-tech-tree.md row 11/12 - `ShipDesign.ApplyMiniaturizationAndBleedingEdge`. `Tests/UnitTests/MiniaturizationAndBleedingEdgeTechnologyTest.cs`. |  |
| 35 | **RS: shields +40% & 10%/round regen, armor 50% effective** | ✅ | ✅ | **Fixed**: `ShipDesign.Update` applies the +40%/-50% design-level multipliers; `BattleEngine.ApplyRegeneratingShields` restores 10% of MAXIMUM shields (not current) once per battle round. Implementing this surfaced and fixed an adjacent real bug: `ShipToken`'s constructor set `Shields` to a single ship's rating instead of the whole token's total, unlike `Armor` (which already scaled by `Quantity`) - every consumer (`BattleEngine`'s damage/attractiveness math, `TurnGenerator`'s per-turn recharge step) already assumed the totalled convention, so a freshly-built or just-split/merged multi-ship token understated its own total shields until the next turn's recharge step ran, parallel to the already-fixed "fresh ship shows 0 armor" bug. `Tests/UnitTests/RegeneratingShieldsTest.cs`. |  |
| 36 | PRT-specific starting tech-level table | ✅ | ✅ | `GameInitialiser.cs:208-288`. |  |
| 37 | "All Expensive fields start Tech 3/4" checkbox | ✅ | ✅ | `GameInitialiser.cs:375-390`. |  |
| 38 | Ship-cost modifiers (WM/IS/CA/CE/IT) | ✅ | ✅ | WM/IS/CA/CE/IT's own discounts covered elsewhere; the empire-wide Miniaturization/BET cost adjustment (research-tech-tree.md row 12) is now implemented too. |  |

**Summary**: 26/38 implemented, 23/38 tested. (Rows 17 and 30's Stargate/engine cost discounts, and rows 4/25's cloak sub-mechanics, fixed previously; row 26's Ultimate Recycling deferral, and rows 6/7/34/35/38's War Monger/Claim Adjuster/BET/Regenerating Shields mechanics, fixed this pass.)

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
| 8 | **Slow Tech Advance global cost doubling** | ❌ | ❌ | Not implemented; no such setting exists. |  |
| 9 | Race-design per-field cost setting | ✅ | ⚠️ | `Common/RaceDefinition/Race.cs:44`. |  |
| 10 | ExtraTech: flat starting level for "expensive" fields | ✅ | ⚠️ | `ServerState/NewGame/GameInitialiser.cs:375-389`. |  |
| 11 | **Bleeding Edge Technology (2x cost until ahead of prereqs)** | ✅ | ✅ | **Fixed**: `Common/Components/ShipDesign.cs`'s `ApplyMiniaturizationAndBleedingEdge` doubles a component's cost while the empire's current tech level hasn't yet exceeded that component's requirement by at least one level in EVERY required field (the same per-field minimum-surplus check row 12's discount uses - doubling fires exactly when that discount computes to 0%). Required threading the empire's current `TechLevel` through a new `ShipDesign.Update(Race, TechLevel)` overload, since (unlike every other cost modifier) this needs live tech state, not just the race. `Tests/UnitTests/MiniaturizationAndBleedingEdgeTechnologyTest.cs`. |  |
| 12 | **Miniaturization (4%/level reduction, 75% cap; 5%/80% with BET)** | ✅ | ✅ | **Fixed**: same method as row 11 - for each tech field a component actually requires, takes the empire's minimum surplus (current level minus requirement) across all of them, then discounts cost 4%/level (5% for BET) up to a 75% (80% for BET) cap. A component with no tech requirement in any field is untouched by either mechanic. `Tests/UnitTests/MiniaturizationAndBleedingEdgeTechnologyTest.cs`. |  |
| 13 | Single "one-hot" research target field | ✅ | ❌ | `StarUpdateStep.cs:147-159`. |  |
| 14 | "Lowest field" auto-research-target option | ❌ | ❌ | Open TODO comment. |  |
| 15 | Generalized Research 50%/15%×5 split | ✅ | ⚠️ | `StarUpdateStep.cs:161-174`. |  |
| 16 | Super Stealth passive research bonus | ❌ | ❌ | Not found. |  |
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
| 30 | Component tech-clamp "4-level lookahead" window | ❌ | ❌ | Only exact `<` gating exists, no lookahead. |  |
| 31 | Hull/component prerequisite table accuracy | N/A | N/A | Data-file audit, not independently re-verifiable (source docs no longer in-repo). | Agreed, skip |

**Summary**: 19/31 implemented, 5/31 tested. (Row 29's Generalized Research forecast bug fixed, and row 7 gained real dedicated tests, earlier this session; rows 11/12's Bleeding Edge Technology and Miniaturization fixed this pass.)

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

**Summary**: 2/14 implemented, 2/14 tested. (Row 10's order-file fault-isolation bug fixed this pass.)

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
| 7 | Per-subtype PRT-exclusivity / boolean-trait gating embedded in the resolver (Engine, Bomb examples) (§5) | ✅ | ✅ | `Common/RaceDefinition/RaceRestriction.cs` + `Common/Components/RaceComponents.cs:96-118` implement this generically via a per-component `Race_Restrictions` dictionary (`required`/`not_available`/`not_required`). Verified correct for **all 6** Engine restrictions (Settler's Delight `HE=2`; all 5 present ram-scoop engines `NRSE=0`; Fuel Mizer/Galaxy Scoop `IFE=2`) and for Claim Adjuster's Retro Bomb (`CA=2`). **Fixed**: Smart Bomb, Neutron Bomb, and Enriched Neutron Bomb now also carry `IS=0` in `components.xml` (previously only Peerless/Annihilator did), completing all 5 of the spec's IS-excluded bomb types. `Tests/UnitTests/ComponentDataFixesTest.cs`. |  |
| 8 | Generic race-trait ability-bitmask gate — now identified as a one-time battle/event component-grant tracker for exactly 12 named components (§6, §14a) | ✅ | ✅ | **Fixed (faithful implementation, per your direction)**: all 12 named components (Multi Cargo Pod, Multi Function Pod, Langston Shell, Mega Poly Shell, Alien Miner, Hush-a-Boom, Anti Matter Torpedo, Multi Contained Munition, Mini Morph, Enigma Pulsar, Genesis Device, Jump Gate) added to `components.xml` with real stats from `component-stats.tsv`. A real one-time-per-race grant mechanism now exists: `EmpireData.GrantedSpecialComponents` (a persisted per-empire set, replacing the original's raw bitmask 1:1 in effect), `SpecialComponentGrants` (the ordered 12-name registry), and `BattleEngine.GrantOneTimeSpecialComponent` (a new method alongside the pre-existing, separate `GrantBattleTechGains` enemy-wreckage-study mechanic - every battle-surviving race gets a roughly-50% roll for one of its still-ungranted specials). `RaceComponents.DetermineRaceComponents` and `StarUpdateStep.TechLevelUp` both gate these 12 behind "granted AND tech-sufficient," never tech alone. **Disclosed simplifications**: the spec's own "13th roll slot" (a bare tech bump with no component, per its own hedged "most plausibly" framing) and the exact weighting of a documented "second weighted roll" are both left out, since neither is concretely quantified - a single ~50% roll picks uniformly from the still-ungranted pool instead, preserving the "never twice, per-race, roughly even odds" shape without fabricating specific unconfirmed numbers. A few individual components' secondary flavor stats (e.g. Multi Function Pod's exact electrical sub-family, Jump Gate's and Genesis Device's real gameplay effect) aren't spec-confirmed either and are left as inert data pending a future mechanic. `Tests/UnitTests/SpecialComponentGrantTest.cs`, `Tests/UnitTests/ComponentDataFixesTest.cs`. |  |
| 9 | Per-design aggregate-stats accumulation (percentage "value" multiplier, compounding mass/fuel multipliers) (§7) | ⚠️ | ⚠️ | `Common/Components/ShipDesign.cs:629-757` (`Update`/`SumProperty`) accumulates armor, cost, shield, cargo etc. by direct summation/scaling rather than the decompiled game's base-10000/base-1000 percentage-multiplier-and-rescale model; functionally covers the same aggregate stats (armor, mass, cost, fuel) via a materially simpler, additive algorithm. Indirectly exercised by `Tests/UnitTests/ManufactureFreshShipArmorTest.cs` and battle tests that call `Update()` transitively, but no test isolates `SumProperty`'s accumulation logic itself. |  |
| 10 | Min/max weapon-range and weapon-initiative brackets tracked per design, consumed by the battle engine's firing dispatcher to skip empty brackets (§7) | ❌ | ❌ | `Common/Components/Weapon.cs:56-57` stores `Range`/`Initiative` per individual weapon stack; `ShipDesign.Weapons` is a flat `List<Weapon>` (`ShipDesign.cs:51`, `833-836`) with no packed min/max bracket computed at the design level, and no such bracket lookup exists in `ServerState/BattleEngine.cs`. |  |
| 11 | Combined armor+shield components' secondary stat special-cases (Langston Shell 95%, Mega Poly Shell 80% defense multiplier; Croby Sharmor +65, Fielded Kelarium +50 flat points) (§7) | ❌ | ❌ | No special-casing by component identity found anywhere in `Common/Components/*.cs` or `ServerState/*.cs`; moot for 2 of the 4 named components since **Langston Shell and Mega Poly Shell don't exist in `components.xml` at all** (see rows 28/29). |  |
| 12 | AI/production design costing: cost/tier comparator + race-trait-gated cost estimator (§8) | ⚠️ | ❌ | No dedicated design cost/tier comparator found in `Nova.Ai/`. A narrower, differently-scoped mechanic exists: `ShipDesign.cs:717-727` applies War Monger (-25%) / Inner Strength (+25%) to weapon costs, and `ShipDesign.cs:740-743` applies Improved Starbases/Alternate Reality (-20%) to starbase costs — this matches `race-traits.md`'s §7 cost-modifier description the spec itself says is a *different* mechanism from §8's estimator, not §8 itself. |  |
| 13 | Cached per-design value/combat-power fields, recomputed on demand vs. read from cache under a mode flag (§9) | ❌ | ❌ | N/A to this architecture — `ShipDesign.Summary` is always recomputed live via `Update()` (`ShipDesign.cs:629`); there is no separate "cached" vs "recomputed" dual path. | Agreed, skip |
| 14 | Turn-order delta-record protocol, opcodes `0x1b`/`0x1e` for design create/delete and slot writes (§10) | ❌ | ❌ | N/A — this codebase has no binary turn-file wire format; orders are serialized as XML (`ServerState/Persistence/OrderReader.cs`), so this mechanic has no analogue to implement. | Agreed, skip |
| 15 | 36-byte per-race/per-slot UI display-label cache (§12) | ❌ | ❌ | N/A — `HullModule.AllocatedComponent` (`HullModule.cs:41`) holds a direct object reference to the `Component`, so no separate name/quantity cache is needed in this architecture; nothing to port. | Agreed, skip |
| 16 | Unified hull-type definition table spanning regular ship hulls + starbase chassis under one index (§13) | ✅ | ❌ | `Common/Components/Hull.cs:155-166` (`IsStarbase => FuelCapacity == 0`, `CanRefuel`) and `components.xml`'s shared `Type=Hull` bucket (36 entries, both ship hulls and the 5 starbase chassis) match the spec's "one logical 37-entry table" finding conceptually. |  |
| 17 | Universal component record layout: 6 tech-level prerequisites, name, mass, 4-way resource cost (§15b) | ✅ | ❌ | `components.xml`'s `<Tech>` block (Energy/Weapons/Propulsion/Construction/Electronics/Biotechnology) and `<Cost>` block (Boranium/Ironium/Germanium/Energy=resources) match the spec's recovered universal layout field-for-field; spot-checked exactly against `component-stats.tsv` for Tritanium, Superlatanium, Mole-skin Shield, Trans-Galactic Drive, Alpha Drive 8, Neutronium, Valanium, Meta Morph, Settler's Delight, Retro Bomb — all match precisely. |  |
| 18 | Tech-level-shortfall check is graded (fully-met / one-field-short / further-away), not a plain boolean (§11 closing, `client-ui-dialog-catalog.md` cross-ref) | ❌ | ❌ | `Common/Components/RaceComponents.cs:86-90`: `if (tech < component.RequiredTech) { continue; }` — a component is either fully included or fully excluded from `RaceComponents`; no graded "how far short" result is computed or surfaced anywhere. |  |
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
| 35 | `component-stats.tsv` cross-check — Electrical (17 entries) | ✅ | ❌ | **Fixed**: Multi Function Pod added (all 17 now present) - given as `Value=60,Type=Jammer`; the tsv's generic "family's single stat" schema doesn't disambiguate exactly which electrical sub-family a "multi function" item combines, so this is a disclosed best-effort single-property guess, not a spec-confirmed Jammer identity. |  |
| 36 | `component-stats.tsv` cross-check — Mechanical (11 entries) | ✅ | ❌ | **Fixed**: both Multi Cargo Pod (`Value=250,Type=Cargo`, matching the already-confirmed "+250 kT third tier" finding rather than the tsv's raw sub-family code) and Jump Gate (data-only - no Property block, since its actual gameplay effect isn't spec-confirmed) added, all 11 now present. |  |
| 37 | `component-stats.tsv` cross-check — Terraforming (20 entries) | ✅ | ❌ | All 20 present and cost-matched (Total ±3 spot-checked: Energy=70 matches tsv exactly); xml's shorter names (e.g. "Total ±3" vs tsv's "Total Terraform ±3") are a naming-convention difference only, not a data gap. |  |
| 38 | `component-stats.tsv` cross-check — Planetary scanners/defenses (15 entries) | ✅ | ⚠️ | **Fixed**: Genesis Device added (all 15 now present) - data-only (no Property block), since its tsv stat field is 0 and its real gameplay effect (a terraforming/genesis-style mechanic, going by its name) isn't spec-confirmed at all. Also **fixed this session**: all 4 Snooper entries (320X/400X/500X/620X) had `PenetratingScan` hard-set to exactly half of `NormalScan`; now corrected to equal `NormalScan` (e.g. Snooper 620X: both 620), matching the tsv's single signed range field, whose magnitude encodes the full normal-range value for all four. `Tests/UnitTests/ComponentDataFixesTest.cs`. |  |

**Summary**: 29/38 implemented (full or partial), 7/38 tested (full or partial). (Row 8's one-time-grant mechanism, and the 11 previously-missing components it names across rows 25/26/28-33/35/36/38, all fixed this pass.)

**What changed since the previous pass**: The single most significant finding is a clean, exhaustively-verified correspondence between two independent gaps: `ServerState/BattleEngine.cs`'s `GrantBattleTechGains` explicitly documents (in its own comment) that it deliberately does *not* reproduce the original's per-component battle-reward table, and — checked component-by-component against the spec's newly-added §14a table — **all 12 of the specific one-time battle/event-granted components it names** (Multi Cargo Pod, Multi Function Pod, Langston Shell, Mega Poly Shell, Alien Miner, Hush-a-Boom, Anti Matter Torpedo, Multi Contained Munition, Mini Morph, Enigma Pulsar, Genesis Device, Jump Gate) **are entirely absent from `components.xml`**, with zero false positives or false negatives against that list — this single root cause explains nearly every "missing component" finding scattered across rows 25–38. The second major finding is a systematic, exactly-2x cost error affecting two full categories in `components.xml`: all 16 Stargates/Mass-Drivers and all 5 Starbase Chassis have resource+mineral costs exactly half of `component-stats.tsv`'s ground truth (e.g. Space Station: xml res=600 vs tsv res=1200). The spec's own §15c explicitly investigated and resolved this exact discrepancy for the Stargate/Driver category, concluding the doubled tsv value is the real in-game figure and that commonly-published/wiki numbers are the ones already halved — meaning `components.xml` was very likely built from those halved wiki figures for these two categories specifically (regular ship hulls, checked as a control, show no such halving — this Starbase Chassis half of the bug is new, beyond what the spec itself flagged). A third, smaller but concrete bug: Inner Strength's documented 5-bomb exclusion (Smart/Neutron/Enriched-Neutron/Peerless/Annihilator) is only enforced for 2 of the 5 (Peerless, Annihilator) in `components.xml` — Smart Bomb, Neutron Bomb and Enriched Neutron Bomb have empty `<Race_Restrictions/>` and are currently buildable by Inner Strength races. A fourth: "Mass Driver 7" is misnamed "Super Drvier 7" (wrong family name, typo, and a wrong tech-level value). A fifth: all 4 Snooper planetary scanners have `PenetratingScan` hard-set to exactly half of `NormalScan`, where the tsv's ground truth has penetrating range equal to the full normal range for all four. Also newly confirmed clean: the previously-RETRACTED "9-component-slot cap" claim is fully resolved — the real per-hull slot count (2–16) is already correctly implemented via `HullModule`, and Meta Morph's specific slot-capacity layout (3/8/2/2/2/2/1) matches the spec's own corrected worked example exactly. No regression tests exist anywhere in `Tests/` for the scanner 4th-power combination formula, `RaceRestriction`/`RaceComponents` availability gating, or any of `ShipDesign.Update()`'s per-component summation logic, despite all three being real, exercised code paths.

---

### turn-generation-engine.md

| # | Behavior | Impl | Test | Evidence | Notes |
|---|---|---|---|---|---|
| 1 | Master routine strict phase-ordered pipeline (overview) | ⚠️ | ⚠️ | `ServerState/TurnGenerator.cs` `Generate()` — much shorter/coarser than spec's 23-phase list. |  |
| 2 | Open/validate order files per player | ✅ | ❌ | `TurnGenerator.ReadOrders()`. |  |
| 3 | Randomize AI/player processing order | ⚠️ | ✅ | **Partially fixed** - see ai-opponent-behavior.md row 31's evidence for the same finding (scoped to the fleet-processing loop and RemoteMiningStep, not universally rethreaded through every empire-touching pass). |  |
| 4 | Per-player progress reporting | ⚠️ | ❌ | Not tied to any randomized order. |  |
| 5 | Detect duplicate/conflicting player identity records | ❌ | ❌ | Not found. |  |
| 6 | Tech-field "reveal" pass | ❌ | ❌ | Not found (only a trade-tech-gate flag exists). |  |
| 7 | Fleet-flag reset/starbase-reveal pass | ❌ | ❌ | Not found as a distinct phase. |  |
| 8 | General per-fleet capability recompute pass | ⚠️ | ❌ | Done ad hoc via `Design.Update()`, not one dedicated pass. |  |
| 9 | Hull-upgrade cascade (auto-promote fleet composition) | ❌ | ❌ | Not found anywhere. |  |
| 10 | Combat-detection/resolution before movement | ✅ | ✅ | `TurnGenerator.cs:152-168` — confirmed fixed, well-tested. |  |
| 11 | Fleet-meets-foreign-colony exchange pass | ❌ | ❌ | Not found. |  |
| 12 | Diplomacy decay + war-declaration threshold | ❌ | ❌ | No automatic decay exists (manual stance-change only). |  |
| 13 | Economic pass: remote-mining + waypoint-task execution | ✅ | ⚠️ | `RemoteMiningStep.cs`; waypoint tasks in `UpdateFleet`. |  |
| 14 | **Random map-feature/event pass (comet, Mystery Trader, attrition)** | ❌ | ❌ | Confirmed absent — no event-table code anywhere. |  |
| 15 | Fleet movement execution | ✅ | ✅ | `TurnGenerator.cs:165-168`, `UpdateFleet`. |  |
| 16 | Home-planet-related fleet-flag pass | ❌ | ❌ | Not found. |  |
| 17 | **Mass-packet decay-in-flight + blast-radius delivery** | ❌ | ❌ | Confirmed absent — no mass-packet class exists. |  |
| 18 | Colonization simultaneous-arrival tie-break | ✅ | ✅ | **Fixed**: `ColoniseTask.Perform` now registers an attempt (`Star.PendingColonizations`) instead of applying it immediately; `ColonizationResolver.ResolvePendingColonizations` (called once every fleet has had a chance to arrive) picks the attempt with the largest accumulated population as the winner, with an exact tie between the top two meaning no one colonizes - all contenders are notified either way. `Tests/UnitTests/ColonizationTieBreakTest.cs`. |  |
| 19 | Minefield regrowth + dedicated all-fleets sweep | ⚠️ | ⚠️ | Only a single fleet-movement-triggered pass exists, with an explicit `FIXME` acknowledging this. |  |
| 20 | Minefield orders via queued-command mechanism | ✅ | ⚠️ | `LayMinesTask`/`LayMines.cs`. |  |
| 21 | Mine-laying rate formula | ⚠️ | ✅ | `ServerState/LayMines.cs` — specific lookup values/98% cap not verifiable. |  |
| 22 | Alternate Reality automatic resource→mineral conversion | ❌ | ❌ | Not found (same gap as production-queue.md #12). |  |
| 23 | Research allocation with two-pass overflow/carryover | ✅ | ⚠️ | `StarUpdateStep.cs:105-127`. |  |
| 24 | Second random map-feature/event pass | ❌ | ❌ | Same gap as #14 — no two-pass structure at all. |  |
| 25 | Starbase refuel pass (ramscoop flat bonus, dock refuel) | ⚠️ | ❌ | Dock refuel exists; no ramscoop flat-fuel contribution found. |  |
| 26 | Second fleet-flag-reset/scan pass | ⚠️ | ⚠️ | `ScanStep` runs once, not twice; waypoint-clearing is inline, not separate. |  |
| 27 | Turn-file generation + notification | ✅ | ❌ | `WriteIntel()`. |  |
| 28 | Per-design capability rating feeding auto-build eligibility | ❌ | ⚠️ | Not found as described. |  |
| 29 | Turn/year counter increment timing (~2/3 through) | ⚠️ | ✅ | Runs late-middle; exact relative position hard to compare against the much-shorter codebase pipeline. |  |
| 30 | Cleanup (scratch memory/UI state) | N/A | N/A | Not portable to a managed reimplementation. |  |
| 31 | Battle engine: 16-round cap | ✅ | ✅ | `BattleEngine.cs:41,881-886`. |  |
| 32 | Battle engine: parity-dependent rate + 3-bracket targeting | ⚠️ | ⚠️ | Not independently re-verified beyond combat-resolution.md's own audit. |  |
| 33 | Battle VCR viewer is pure playback | N/A | N/A | UI concern, out of scope for this file. | Agreed |
| 34 | Racial-trait attrition event (4th random event) | ❌ | ❌ | Same gap as #14/#24. |  |
| 35 | Fleet-linked habitat-tolerance drift (wormhole cargo) | ❌ | ❌ | `WormholeDriftStep.cs` only drifts the wormhole's own position, not fleet habitat. |  |
| 36 | Batch/auto turn generation (10/100/1000-turn loop) | ❌ | ❌ | No batch-generation entry point exists. |  |
| 37 | Manual "ready/submitted" toggle independent of generation | ⚠️ | ❌ | Field exists (`EmpireData.TurnSubmitted`), no standalone toggle-command located. |  |
| 38 | "Auto Generate Options" (auto-advance/force-generate timers) | ❌ | ❌ | Not found. |  |

**Summary**: 11/38 implemented, 8/38 tested. (Rows 3 and 18's turn-order/colonization-tie-break gaps fixed this pass.)

**Headline gaps**: the two random-event tables and racial-attrition event, mass packets, simultaneous-colonization tie-break, fleet/foreign-colony exchange, and diplomacy decay/war-declaration are all fully absent. The per-turn minefield sweep is fleet-movement-triggered rather than a dedicated pass (an existing `FIXME` in the code already acknowledges this). Combat-before-movement ordering is confirmed correctly fixed and well-tested.

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
| 12 | Detects sole eligible leader / ties | ⚠️ | ❌ | Just declares first-in-iteration-order qualifier; no tie handling. |  |
| 13 | **Evaluation runs after the year-counter increment — inverted** | ✅ | ✅ | **Fixed**: `TurnGenerator.cs:176-178` now calls `TurnYear++` before `victoryCheck.Victor()`, matching the spec's phase order. `Tests/UnitTests/VictoryCheckTest.cs`. |  |
| 14 | Per-condition checks (7 formulas) | ✅ | ❌ | `VictoryCheck.cs:115-367`. |  |
| 15 | Condition 5: exceeds 2nd place by percentage, not flat multiply | ✅ | ❌ | `VictoryCheck.cs:356-366` — comment documents this exact fix. |  |
| 16 | Score formula components | ⚠️ | ❌ | `ServerState/Scores.cs:62-171` — several terms diverge from the spec's exact shape (population cap, tech bonus structure, capital-ship formula). |  |
| 17 | Setup screen and runtime check share one settings store | ✅ | ❌ | `GameSettings.Data` singleton used identically by both. |  |

**Summary**: 14/16 implemented (excluding 1 N/A row), **1/16 tested** — rows 5 and 13's bugs (slider Max, evaluation-order inversion) were fixed and row 13 is now tested via `Tests/UnitTests/VictoryCheckTest.cs`; no other test in the repo references `VictoryCheck`, `Scores`, or `TargetsToMeet`/`MinimumGameTime`.

---

## What's next

This is a lot of surface area. Given the scale, I'd suggest picking a lane rather than trying to
work the whole backlog at once — happy to go wherever you point: the concrete standout bugs (many
are small, isolated fixes: the two victory-check bugs, the order-file fault-isolation bug, the
Defense/Terraform/Stargate/MassDriver cost corrections, the freighter-routing 180² bug, the
population-habitability-scaling bug), a themed pass (e.g. "make Race Traits' secondary effects
real" or "cloaking, properly"), or the UI differences table above once you've triaged which rows
you actually want built.
