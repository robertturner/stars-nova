# Research and Tech Tree Mechanics

Clean-room behavior specification, compiled entirely from public community
documentation (fan FAQs, the Stars!AutoHost wiki, and a GameFAQs strategy
guide) and restated in original wording. No game code, executable, or
disassembly was consulted. See **Sources** at the end for every URL used,
matched to the facts it supports. Where sources disagreed or a mechanic
could not be pinned down, this is flagged explicitly under **Open
Questions** rather than presented as settled fact.

## Overview

Stars! organizes all technology into six independent **fields**:

- Energy
- Weapons
- Propulsion
- Construction
- Electronics
- Biotechnology

Each field has its own integer **tech level**, running from 0 (the
starting value for most races) up to a maximum of 26. Raising a field's
level is what unlocks better hulls, components, weapons, armor, shields,
scanners, terraforming, and other race abilities — the "tech tree" is
really six separate ladders, with individual rungs (levels) gating
individual unlocks, and some unlocks gated behind a *combination* of
levels in two or three different fields at once.

Research is paid for with the same **resources** that a planet would
otherwise spend on factories, mines, terraforming, defenses, and ships.
Every colony produces resources every year from its population and its
operating factories; whatever isn't spent on that colony's own
construction queue can instead be funneled into the empire's shared
research effort. There is a single empire-wide "next-field(s) to
research" setting and a single "percent of resources devoted to
research" setting — research is not managed per-planet, only production
is.

The cost of buying the next level in a field grows steeply (a
Fibonacci-like curve for roughly the first dozen levels, then a flatter
climb), is further scaled by a per-field, per-race cost setting chosen at
race design time ("cheap" / normal / "expensive"), and also scales
gently with how much total technology the empire has already
accumulated across *all* fields combined.

Besides straightforward research spending, players can also acquire
tech levels from other empires — by scrapping, destroying in battle, or
invading and capturing enemy ships/planets that are more advanced in a
field than the player is.

## Mechanics

### 1. Generating resources (the fuel for research)

A colony's yearly resource output is controlled by five race-design
settings (chosen once, at race creation, each within the listed range):

| Symbol | Meaning | Range | Notes |
|---|---|---|---|
| A | Colonists needed per 1 resource | 700 – 2500 | lower = more resources per colonist |
| B | Resources produced per 10 operating factories | 5 – 15 | |
| C | Resources needed to build one factory | 5 – 25 | |
| D | Factories operable per 10,000 colonists | 5 – 25 | |
| G | Resources needed to build one mine | 3 – 15 | (mineral-side settings F/H are not relevant to research) |

A colony's resources for the year are:

```
resources = floor(population / A) + floor(operableFactories / 10) * B
```

where `operableFactories = min(factoriesBuilt, population/10000 * D)`.

A commonly cited illustrative default is A=1000 (1 resource per 1,000
colonists), matching the community shorthand "1 resource per 1000 pop
plus factory output" [gamefaqs.gamespot.com]. Only one of the two
production channels (population, factories) is needed for research
purposes — a fully "-f" (factory-less) race still produces resources
purely from population.

Because these five values are race-design choices, two races with
identical population can have very different resource (and therefore
research) output. The Alternate Reality PRT is a documented special
case: it has no planetary population/factories in the normal sense and
instead grows its planetary resource output as its **Energy** tech
level rises [wiki.starsautohost.org/wiki/Custom_Race_wizard].

### 2. From resources to research points

Resources allocated to research convert to research progress **1:1** —
there is no separate "research point" currency; the same numbers that
buy factories/mines/ships buy tech levels, just accounted against a
different budget line. Two settings control how many of a colony's
resources reach research in a given year:

- **Research percentage** (0–100%, empire-wide): the target share of
  total empire resource income earmarked for research before/alongside
  planetary construction.
- **Per-planet "contribute only leftover resources to research"
  checkbox**: when checked for a colony, that colony ignores the global
  research percentage entirely and instead sends research only whatever
  resources are left over after its own production queue (factories,
  mines, terraforming, defenses, ships) is fully funded for the year.

A widely repeated piece of community strategy advice observes that,
functionally, **a colony always spends any leftover resources on
research regardless of the percentage slider** — so setting the global
research percentage to 0% while leaving "contribute only leftover
resources" unchecked on every planet produces the same result as
checking that box everywhere: every planet builds everything it can
first, and only the remainder feeds research
[starsfaq.com/articles/sru/art82.htm]. Raising the percentage above 0%
(without the leftover-only checkbox) instead *reserves* a slice of a
colony's resources for research even when that colony still has useful
things it could build — deliberately trading short-term colony growth
for faster tech.

### 3. Cost curve for tech levels

The authoritative community derivation ("Guts of research costs",
credited to Bob Martin) gives the formula:

```
totalCost = (baseCost[level] + totalLevels * 10) * costFactor
```

- `baseCost[level]` — a fixed table, keyed only by the level being
  purchased (see below), identical for every field.
- `totalLevels` — the sum of the empire's current tech levels across
  **all six fields combined** (available on the in-game Score screen).
  This term makes every subsequent level, in every field, gradually
  more expensive as the empire's overall tech investment grows.
- `costFactor` — set per field at race-design time:
  - `0.5` if that field is set "Costs 50% less" ("cheap")
  - `1.0` if left at the normal setting
  - `1.75` if that field is set "Costs 75% extra" ("expensive")
- If the game parameter **"Slow Tech Advance"** is enabled for the
  game, the whole `totalCost` is doubled.

[starsfaq.com/advfaq/guts1.htm]

**Structurally confirmed by inspection of the exported client.** A component-design-import routine independently computes a component's usable tech level and separately applies a cost calculation matching the `baseLevelCost + totalLevels × 10` shape almost exactly, with a per-field cost-class modifier (Cheap/Normal/Expensive) applied afterward and the whole result doubled under a global flag consistent with "Slow Tech Advance." The same routine also clamps a component's usable tech level to a **4-level window** above the field's current level (i.e., a component becomes available up to 4 levels ahead of where the empire currently sits, not only exactly at the required level), with starbases exempted from part of this check — a previously undocumented interaction between research and component/miniaturization gating worth cross-referencing against `race-traits.md`'s miniaturization discussion. Separately, the game's per-player record structure independently confirms exactly 6 contiguous tech-level fields, matching the "six independent fields" claim at the data-layout level.

Base cost table (identical for all six fields):

| Level | Base Cost | Level | Base Cost |
|---|---|---|---|
| 1 | 50 | 14 | 18,040 |
| 2 | 80 | 15 | 22,440 |
| 3 | 130 | 16 | 27,050 |
| 4 | 210 | 17 | 31,870 |
| 5 | 340 | 18 | 36,900 |
| 6 | 550 | 19 | 42,140 |
| 7 | 890 | 20 | 47,590 |
| 8 | 1,440 | 21 | 53,250 |
| 9 | 2,330 | 22 | 59,120 |
| 10 | 3,770 | 23 | 65,200 |
| 11 | 6,100 | 24 | 71,490 |
| 12 | 9,870 | 25 | 77,990 |
| 13 | 13,850 | 26 | 84,700 |

The sequence approximates a Fibonacci progression through level ~12,
then flattens into roughly linear growth. Taking every one of the six
fields from level 0 to level 26 at normal cost, starting from an
otherwise-untouched empire, totals **4,185,240** resources (this figure
already folds in the `totalLevels * 10` surcharge along the way)
[starsfaq.com/advfaq/guts1.htm].

**Race-design cost settings.** Each of the six fields is independently
set to normal, "Costs 50% less," or "Costs 75% extra" during race
creation; this is a fixed racial trait for the whole game, not a
per-turn choice. A race can also check "All 'Costs 75% extra' research
fields start at Tech 3" (Tech 4 instead, for a Jack-of-All-Trades race),
which immediately grants free levels in every field so flagged, for a
flat race-design point cost regardless of how many fields qualify — so
the more fields marked "expensive," the more this flat-fee option is
worth taking [wiki.starsautohost.org/wiki/Custom_Race_wizard]. Community
guidance repeatedly notes that a first "cheap" field is a good value in
race-design-point terms, a second is markedly worse value, and the
efficient move is usually to pair a "cheap" field with an "expensive"
one so the point costs roughly offset (nicknamed "3.5 cheap techs" by
the community) [wiki.starsautohost.org/wiki/Race_Design],
[gamefaqs.gamespot.com].

Two lesser racial traits directly interact with cost:

- **Bleeding Edge Technology**: newly-reachable tech initially costs
  double; the cost drops back to normal once the empire's level in
  every prerequisite field exceeds the requirement by at least one
  level. It also changes component miniaturization from 4%/level
  (capped 75%) to 5%/level (capped 80%) — confirmed directly against
  the original game's own Player's Guide ("Bleeding Edge Technology"
  and "Conditions that Affect Production" topics), not just the wiki
  [wiki.starsautohost.org/wiki/Custom_Race_wizard],
  [stars.hlp, retrieved via wiki.starsautohost.org/wiki/Downloads].
  **Note:** this project's own code does not yet implement
  miniaturization (the baseline 4%/level cost reduction) or Bleeding
  Edge Technology at all — both are still `TODO` stubs in
  `ServerState/NewGame/GameInitialiser.cs`. This is a real gameplay gap,
  not just a documentation one.
- **Generalized Research** — see section 4 below; it does not change
  `totalCost`, only how a turn's research budget is spread across
  fields.

### 4. Allocating a turn's research budget across fields

By default, **all** of a turn's research resources go to a single
field at a time — the one the player has currently selected as the
research target (or, with the "lowest field" auto-target option, the
game auto-selects whichever of the six fields currently has the lowest
level and points the whole budget there).

**Confirmed and clarified by inspection of the exported client.** The Research dialog exposes exactly **6** mutually-exclusive radio buttons for field selection — directly confirming "all resources go to one field at a time," selected by radio button rather than any slider or percentage split. The dialog additionally stores a **second, independent setting** — a "next field" combo box whose values are the 6 named fields plus a "lowest field" auto-select option — packed into a different part of the same per-player byte as the active-field selector. This is a strong, code-level confirmation of the "lowest field" auto-target option specifically.

**Gap resolved by a later pass: the "percent of resources devoted to research" control does exist, in the same dialog.** It was missed by the earlier inspection because it is not a standard slider/scrollbar control but a custom-painted readout with two small increment/decrement hit-regions (sharing the same click-and-hold auto-repeat mechanism used elsewhere for spinner-style controls — see `client-ui-dialog-catalog.md`'s "Segmented bar/gauge and repeating-button controls"), clamped to the documented 0-100% range. Dragging or clicking either arrow immediately recomputes, live, the empire-wide total resources that would be diverted to research at the new percentage — summed across every production-queue entry belonging to the current player empire-wide, not per-planet — confirming this setting is exactly the empire-wide percentage this document's opening overview describes, not a per-planet value.

**New: a "turns until this field's next level completes" forecast, previously undocumented.** The same dialog computes and displays a live estimate of how many turns remain before the currently-targeted field reaches its next level, by dividing the field's outstanding cost (current cost table lookup minus progress already banked) by the just-recomputed empire-wide per-turn research contribution described above. A race-trait check (one bit of a per-race trait/flag table, gated the same way several other race-conditional client behaviors in this project are) halves the effective per-turn contribution used in this forecast for at least one race trait not yet cross-referenced to a specific named PRT/LRT — flagged as an open item below rather than guessed at. Separately, the same dialog's initialization independently re-derives the section 3 cost formula end-to-end: it sums the current race's own 6 tech-level fields into a running total (confirming, at a second, independent code site, that `totalLevels` is exactly one race's own cross-field level sum), adds the level-keyed base-cost table entry, and applies a per-field cost-class multiplier with three distinct branches matching cheap/normal/expensive — corroborating the community-sourced formula in section 3 from the dialog that actually uses it turn-to-turn, not only from the component-import routine noted there previously.

**New: the tech-level-distance grading mechanism, cross-referenced from `client-ui-dialog-catalog.md`'s "Slot/equipment editor" entry.** That entry documents a graded (not binary) unavailability distinction — "exactly one technology level away" versus "needs multiple fields raised" — and notes the mechanism computing this grading had not been traced to a specific function. This pass found a strong candidate in the same Research-dialog code cluster: a function that, given a target level for each of the 6 fields (e.g., a component's or hull's prerequisite combination), temporarily simulates leveling up every field currently below its target — accumulating the total additional resources such a climb would cost field-by-field using the same per-field cost calculation described above — then restores the real, unmodified tech levels before returning that total. This produces exactly the kind of number (and, incidentally, the field-by-field level deltas) a "how far away is this item, and what would it cost to get there" tooltip would need, though this pass did not conclusively trace its output being painted into a specific on-screen tooltip string, so the connection to the slot/equipment editor's grading is a strong inference, not a proven one.

**Individual accounting of the Research dialog's 7 numbered helper functions (Ghidra `FUN_10d8_*`), closing out the last item this section had left as an aggregate description.** A later pass read each of the segment's 7 numbered helpers individually rather than only sampling them; all 7 turn out to be exactly the paint/hit-test/cost-support roles already characterized above, with no new subsystem hiding among them:

- **Paint routine** — draws the dialog's live info panel: a sequence of bit-flag-gated label/value lines (tech-level list, current field's cost, empire resource contribution, and related stats), each looked up from the string table and formatted with the client's shared numeric-format helper. It also *inline-computes* the "turns until next level" forecast described above at the point it is drawn — the outstanding-cost/per-turn-contribution division and the race-trait-conditional halving both live directly in this paint routine, not in a separate calculation function.
- **Hit-test / stepper-drag routine** — tests a click point against the two increment/decrement rectangles the paint routine records, then drives a repeat-button drag loop that clamps the "percent of resources devoted to research" value to 0–100, recomputes the empire-wide contribution total on each step (via the recompute helper below), and repaints. Outside those rectangles it hit-tests dialog list rows and hands off to the segment-25 mode-switched hover-popup dispatcher (see "Toolbar, tooltip, and contextual popup" in `client-ui-dialog-catalog.md`) for row tooltips.
- **Cost-lookup formula** — the section 3 cost-formula re-derivation itself: sums the race's 6 tech-level fields, adds a level-and-field-indexed base-cost table entry, applies the starbase in-place-upgrade half-cost conditional, and applies the race's "expensive tech" doubling flag. This is the low-level primitive both the forecast (in the paint routine) and the tech-distance-grading candidate (below) call to price one additional level.
- **Shared detail-card renderer** — the already-documented ~1847-line object/context-detail-card renderer shared with `BROWSERDLG` and segment 25's hover popup; see `client-ui-dialog-catalog.md`'s "Search and record browser" section for its full write-up. No new findings from this pass.
- **Live-contribution recompute helper** — the function actually backing the "recomputes live, the empire-wide total resources that would be diverted to research at the new percentage" behavior described above: it temporarily overrides the race's stored percent-of-resources value with a candidate value, sums the resulting per-turn research contribution across every one of the player's colonies, then restores the real stored value before returning the total.
- **Tech-level-distance grading candidate** — the target-level-to-total-cost function discussed in the paragraph above (individually confirmed this pass; see also the corresponding update in `client-ui-dialog-catalog.md`'s "Slot/equipment editor" entry).
- **Category/context lockout-bit gate** — a small lookup that maps a (component-category, context-code) pair to one bit of a 32-bit flag word via a fixed table, then reports "available" only when that bit is *not* already set in a per-player 32-bit flag word. The category values are exactly the same 16 component-category bit-flags `ship-design-and-components.md` §4 documents, and the overall shape (category bit → per-race flag bit → available/blocked) matches that document's "generic 32-bit per-race special ability bitmask" gate described abstractly there — a plausible second, concrete implementation of that same gate, though not proven identical, and the context-code parameter's exact real-world meaning (possibly a control/accelerator id) was not pinned down.

The lesser racial trait **Generalized Research** changes this: only
half (50%) of the turn's research budget is applied to the
currently-selected field, and 15% of the budget is additionally applied
to *each* of the other five fields. Because 50% + 5×15% = 125%, this
trait yields more total research throughput than a focused strategy for
the same resource spend, at the cost of being unable to "rush" a single
field to unlock something urgently
[wiki.starsautohost.org/wiki/Generalized_Research],
[wiki.starsautohost.org/wiki/Custom_Race_wizard]. **125% is confirmed**
directly against the original game's own help text (the "Generalized
Research" topic in stars.hlp reads: "Only half of the resources
dedicated to research will be applied to the current field of
research. 15% of the total will be applied to each of the fields. (Yes,
we know this adds up to 125%.)" — the game acknowledges the odd total
itself). The "115%" figure a GameFAQs strategy-guide author attributed
to the in-game help text was therefore that author's own error, not a
genuine discrepancy in the game
[stars.hlp, retrieved via wiki.starsautohost.org/wiki/Downloads],
[gamefaqs.gamespot.com].

The Super Stealth PRT has an unrelated, passive way to gain resources
in *all six* fields simultaneously every year: it gains, in each field,
resources equal to half the average that all races (including itself)
spent in that field that turn, as long as at least one other race
exists in the game [wiki.starsautohost.org/wiki/Custom_Race_wizard].

### 5. Leftover / carryover of research progress

Community sources describe research spending as accumulating toward
the cost of the *next* level in a field, turn over turn, until the
threshold in the cost table (section 3) is met. Nothing in the sources
consulted suggests that progress is ever discarded mid-level — a colony
that contributes a small amount of research in one turn and more in a
later turn is described as building toward the same next-level target
over time (e.g., the "contribute only leftover resources" strategy
explicitly relies on dribbling in whatever a colony can spare, turn
after turn, until a level is eventually bought)
[starsfaq.com/articles/sru/art82.htm]. Whether resources in *excess* of
the exact amount needed to complete a level are banked forward toward
the following level (rather than simply lost for that turn) is treated
here as an **assumption** consistent with the community's descriptions,
not a fact any source states in so many words — see Open Questions.

### 6. Tech trading (acquiring levels from other empires)

Independent of the resources spent on research, a player can pick up a
single tech level per turn from another empire, via any one of:

1. **Scrapping**, at one of the receiving player's own starbases, a
   fleet built with a component requiring higher tech than the receiver
   currently has.
2. Having an **armed ship present in a battle** where an enemy ship
   with superior tech in some field is destroyed.
3. **Invading** a planet belonging to a player who leads in one or more
   fields.

For each qualifying event, there is first a flat 50% chance that
nothing at all is learned. If that 50% check passes, then for each tech
field in which the source (scrapped/destroyed/invaded) party is
strictly ahead, there's a further 50% chance of learning *that* field,
so the chance of learning at least one field given `n` fields of
advantage is:

```
P(learn) = 0.5 * (1 - 0.5^n)
```

Only one tech level, from one field, from one source, can be gained
per turn regardless of how many qualifying events occur; multiple
qualifying events in the same turn each roll independently, so playing
more of them (e.g. scrapping several separate fleets, or running
several invasion sites at once) raises the chance that *at least one*
succeeds, following `1 - (1 - P(learn))^k` for `k` independent
attempts. Turn order matters: scrapping resolves first, then
waypoint-zero invasions, then battles, then waypoint-one invasions
[starsfaq.com/advfaq/guts1.htm].

Two organized multiplayer techniques exploit this: a "wolf/sheep" ring
where allies deliberately destroy obsolete ships in battle to spread
tech gains among several allies at once, and a "tech trade by invasion"
loop where two allies repeatedly swap ownership of one low-population
planet each turn, each invasion carrying its own independent chance of
a tech gain for the invader [starsfaq.com/advfaq/guts1.htm].

### 7. Prerequisites for hulls, components, and other unlocks

Most items require a single field/level pair (e.g., "Construction 3"
unlocks the Destroyer Hull); a number of the more interesting items
require two or three fields to each reach a minimum level
simultaneously. Selected documented breakpoints
[wiki.starsautohost.org/wiki/Chapter_6:Early_Resource_Management]:

- **Energy 1** (+ Biotechnology 1): basic ±3% temperature terraforming.
- **Energy 2**: first minelayer; with Propulsion 3, the Maneuvering Jet;
  with Propulsion 6, the first ram scoop engine (Radiating-Hydro Ram
  Scoop).
- **Energy 3** (+ Electronics 7, Biotechnology 2): first penetrating
  scanner.
- **Weapons 5** (+ Electronics 6): first LBU installation-killing bomb;
  (+ Biotechnology 7): first Smart Bomb.
- **Weapons 10** (+ Propulsion 2): first torpedo with better than 50%
  base accuracy.
- **Propulsion 5** (+ Construction 5): first stargate.
- **Construction 3**: Cargo Pod, Destroyer Hull, Medium Freighter Hull.
- **Construction 4**: Privateer Hull; Space Dock starbase (requires
  also having chosen the Improved Starbases trait); (+ Electronics 2):
  Robo-Mini-Miner (requires Advanced Remote Mining).
- **Electronics 7** (+ Energy 3, Biotechnology 2): first scanner with
  penetrating range (unavailable to races with the No Advanced Scanners
  trait).
- **Biotechnology 4** (+ Energy 2): minelaying ability for non-Space
  Demolition races.

This is only a representative subset focused on early/mid-game
breakpoints; the full unlock table for every hull, engine, weapon,
armor, shield, and scanner is considerably larger and was not
exhaustively catalogued for this document. See Open Questions.

**Confirmed by inspection of the exported client — item selectability and cost gating in the
production-queue and research dialogs.** A cluster of functions supporting both the Research dialog
(section 4 above) and the production-queue/ship-design dialog shares one packed-integer
representation for a "buildable/researchable item": a quantity/count field, a category code (a small
set of values identifying "basic production item" — mine, factory, defense, terraforming, auto-build
— versus "ship or starbase design"), and, for designs, an index selecting which design slot (0-15
for a player's own saved designs, 16 and up for the game's built-in/standard hull designs, looked up
in a separate table). This confirms the prior characterization of this segment as "listbox selection
filtered by bit flags" and clarifies what is actually being filtered:

- **Hidden/obsolete flag, not a tech-level check, gates listbox visibility.** Each design record
  carries its own hidden/obsolete bit; an item with this bit set is skipped when building the
  selectable list (cost is reported as zero and it is excluded), independent of whether the design's
  required tech level is currently met. This is a simpler mechanism than "tech and environment bit
  flags jointly gate selectability" as originally hypothesized — tech-level gating for *researching*
  a field (as opposed to *listing* an already-designed ship/starbase) is handled by the separate
  cost-and-level-clamp logic already documented above (the 4-level miniaturization window), not by
  this segment.
- **PRT-based exclusion from the Research dialog's field-selection lists.** When building the
  candidate list of "next field to research" entries (feeding both the 6-way radio-button selector
  and the "lowest field" auto-target combo box documented in section 4), the code iterates the 6
  fields plus one extra slot (7 total, matching the already-confirmed combo-box count) and
  explicitly excludes candidates for two specific per-race PRT codes: one PRT code (independently
  confirmed elsewhere in this pass to mean **Alternate Reality**) excludes the first three fields
  (Energy/Weapons/Propulsion by this document's field ordering) from consideration in specific
  contexts; a second, unidentified PRT code excludes the last two (Electronics/Biotechnology) in an
  analogous way. This is a previously-undocumented, code-level-only detail — no community source
  consulted for this document mentions PRT-conditional exclusions from the field-selection list —
  and the exact gameplay meaning of "excluded from consideration" (hidden entirely vs. simply
  deprioritized) was not fully traced.
- **Starbase design cost: confirmed halving on in-place upgrade, plus an additional race-conditional
  discount.** Separately from the general cost-curve formula (section 3), the ship/starbase
  cost-computation routine confirms two additional, previously-undocumented rules: (a) when a design
  slot already holds an existing starbase and the player is upgrading it in place to a new component
  loadout rather than building a fresh one, the charged cost is exactly **half** the normal full
  design cost (rounded up to the next whole resource/kT), consistent with community folklore about
  "upgrading a starbase is cheaper than building new" but not previously confirmed against this
  project's own research; (b) a further roughly **20%** discount is applied to starbase costs
  specifically for races meeting a race-trait condition (a specific race-trait bit, checked together
  with the same Alternate Reality PRT code noted above) — plausibly corresponding to the "Improved
  Starbases" or a similar named racial trait, though the exact trait name was not cross-referenced in
  this pass.
- **The mineral-mining engine documented in `population-growth.md` section 5 is shared with this
  dialog.** The production-queue dialog's remote-mining-fleet preview calls the same core mining
  function identified in segment 6 (Ghidra `FUN_1028_*`) to compute a live estimate, confirming the
  preview shown to the player and the actual turn-processing calculation are the same code path.

**Segment 27's "surrounding dialog UI" gap, itemized.** The segment's coverage row previously rated
it "Full for cost/filtering logic, Partial for surrounding dialog UI" without saying what that
remaining UI actually was. A full read of all 12 numbered functions plus the one specially-named
procedure hidden among them resolves this:

- **The segment's first four numbered functions are the per-planet production-queue dialog's own
  launch/init/commit machinery**, not further cost logic: a dialog-launch/cleanup wrapper, the
  candidate-item-list builder already documented above (cost/filtering), an OK/Cancel commit
  handler, and a large WM_COMMAND-style dispatcher handling the dialog's own controls. The dispatcher
  independently confirms, at this second code site, `production-queue.md` §9's manual-add quantity
  stepper (no modifier = 1, Shift = 10, Ctrl = 100, Ctrl+Shift = the 1,020 near-max clamp, read via
  the same keyboard-state check pattern) and its Move-Up/Move-Down queue-reordering commands (already
  documented generically in `client-interface.md`'s "Object information and orders"), and additionally
  handles a "switch which saved template's items to offer" control that re-runs the cost/filtering
  candidate-list build against one of the four saved production templates (see
  `production-queue.md` §9) — i.e., the per-planet queue editor and the 4-slot template manager share
  this segment's candidate-list builder, not just their storage format.
- **A dedicated listbox-population routine** clears and rebuilds the candidate-item list's display
  strings each time it changes, skipping zero-cost (filtered-out) entries — an independent, second
  code-level confirmation of `production-queue.md` §9's "(Auto Build)" and "up to N" display-string
  claim, this time tied to a specific function in this segment.
- **New: item captions distinguish "Upgrade" from "Downgrade" for starbase in-place changes.** The
  per-item caption builder called by the listbox routine above appends an "upgrade" or "downgrade"
  suffix to a starbase design entry when the design slot already holds an existing starbase and the
  candidate would change its total tech-level sum up or down — a previously-undocumented UI detail
  sitting directly on top of the already-documented half-cost in-place-upgrade mechanic (this
  section, above), letting the player see which direction a swap goes before committing to it.
- **New: a custom-painted, itemized cost-breakdown panel.** A dedicated paint routine (not a standard
  list/report control) draws, in two passes — once for the currently-highlighted candidate item and
  once for the queue's running total — a small set of color-coded cost lines (the same
  category-typed cost/resource fields the shared cost dispatcher above computes) followed by a
  "resources available" summary line, giving the player a live, itemized breakdown rather than a
  single combined cost figure.
- **The 100-year "practically never" completion simulator (`production-queue.md` §8) is confirmed to
  live in this same segment.** A dedicated function repeatedly re-simulates the queue's projected
  yearly progress (reusing this segment's own cost dispatcher and segment 24's queue-insertion
  validator each iteration) up to a hard-coded 100-iteration cap, matching that document's claim
  exactly at a newly-identified code site.
- **The specially-named, uncounted dialog procedure hidden in this segment (following the
  segments-17/28/29/30 pattern) is `ZIPPRODDLG` — already documented, not a new mechanic.** It is the
  4-slot production-template manager described in `production-queue.md` §9 ("a 4-slot manager... each
  of the 4 slots holds up to 12 auto-build entries plus a single stored flag bit"): a tabbed dialog
  with one radio button per slot, per-slot rename (reusing the segment-17 rename dialog) and clear
  commands, and a per-slot listbox populated by a small sibling routine from the same saved-template
  table the main queue dialog's "switch template" control (above) reads from. This resolves the
  segment's UI gap rather than leaving it open: every piece of dialog-facing code here is now
  attributed either to the per-planet queue editor or to the (already-documented-elsewhere) template
  manager, with no further unaccounted UI logic in the segment.

### Worked Example A — single field, early game, normal cost

Assume a colony with population 500,000, A = 1,000 colonists/resource,
100 operating factories at B = 10 (10 resources per 10 factories),
producing:

```
resources = 500,000/1,000 + (100/10)*10 = 500 + 100 = 600 / year
```

The empire has all six fields at level 0 (`totalLevels = 0`) and is
researching **Energy** (normal cost, `costFactor = 1.0`), with the
research percentage set high enough that 150 resources/year reach
research.

- **Year 1**: 150 available.
  - Level 1 cost = (50 + 0×10) × 1.0 = **50**. Spend 50, 100 remain.
  - `totalLevels` is now 1 (Energy is 1, everything else 0).
  - Level 2 cost = (80 + 1×10) × 1.0 = **90**. Spend 90, 10 remain.
  - `totalLevels` is now 2.
  - Level 3 cost = (130 + 2×10) × 1.0 = **150**. Only 10 available —
    not enough; the 10 are banked toward level 3.
  - End of Year 1: **Energy 2**, with 10/150 banked toward Energy 3.
- **Year 2**: another 150 arrives, plus the 10 banked = 160 available
  toward the 150 needed for level 3.
  - Level 3 completes, spending 150; 10 resources are left over.
  - `totalLevels` is now 3.
  - Level 4 cost = (210 + 3×10) × 1.0 = **240**. The remaining 10 bank
    toward it.
  - End of Year 2: **Energy 3**, with 10/240 banked toward Energy 4.

### Worked Example B — cost-factor and Generalized Research interaction

An empire has taken **Generalized Research**, made **Weapons** "cheap"
(costFactor 0.5), and made **Biotechnology** "expensive" (costFactor
1.75); Energy, Propulsion, Construction, and Electronics are all
normal cost. Current levels: Energy 5, Propulsion 5, Construction 5,
Electronics 5, Biotechnology 5, Weapons 8 — so `totalLevels = 5×5 + 8 =
33`. The player has selected **Weapons** as the primary research
target, with a 400-resource/year research budget.

Generalized Research splits the 400 as: 200 (50%) to Weapons, and
60 (15%) to each of the other five fields (Energy, Propulsion,
Construction, Electronics, Biotechnology) — 200 + 5×60 = 500 total
resource-equivalent of research applied, 125% of the 400 spent.

- **Weapons** (8→9, cheap): cost = (2,330 + 33×10) × 0.5 = (2,330 +
  330) × 0.5 = **1,330**. The 200 allocated only covers 200/1,330 of
  the level.
- **Energy** (5→6, normal): cost = (550 + 330) × 1.0 = **880**. The 60
  allocated covers 60/880 ≈ 6.8% of the level.
- **Biotechnology** (5→6, expensive): cost = (550 + 330) × 1.75 =
  **1,540**. The same 60 allocated covers only 60/1,540 ≈ 3.9% of the
  level — visibly less progress than Energy received for the identical
  resource contribution, purely because of the 1.75× cost multiplier.

This illustrates why "expensive" fields are usually chosen for
techs a race cares least about: under Generalized Research's flat
side-allocations, an expensive field converts the same resources into
proportionally less progress every single turn.

### Worked Example C — multi-planet "leftover resources only" accumulation

Two colonies are both set to "contribute only leftover resources to
research," per the community strategy of leaving the global research
percentage irrelevant and letting fully-developed planets fund research
automatically [starsfaq.com/articles/sru/art82.htm]. The empire is
researching **Construction** (normal cost), currently at level 3, with
all other fields at 0, so `totalLevels = 3`.

- **Colony A**: generates 300 resources this year; its production queue
  (more factories, mines) still has 250 resources' worth of useful work
  queued, so only 300 − 250 = **50** resources are leftover for
  research.
- **Colony B**: generates 150 resources; it is already fully built out
  (queue empty), so all **150** resources are leftover for research.
- **Empire research income, Year 1**: 50 + 150 = 200.

Construction level 4 costs (210 + 3×10) × 1.0 = **240**. The 200
available is short by 40; it is banked toward level 4.

- **Year 2**: Colony A's production queue has advanced further and now
  only needs 180 resources of the same 300 it produces, leaving 120
  for research; Colony B still contributes its full 150 (still nothing
  else to build). Empire research income = 120 + 150 = 270.
  Combined with the 40 banked from Year 1: 310 available against the
  240 needed.
  - Construction reaches **level 4**, consuming 240; 70 resources are
    left over.
  - `totalLevels` becomes 4.
  - Construction 5 costs (340 + 4×10) × 1.0 = **380**. The 70 leftover
    resources bank toward it.
- End of Year 2: **Construction 4**, with 70/380 banked toward
  Construction 5, achieved without the player ever manually touching
  the research percentage slider on either colony.

## Open Questions / Uncertainties

- **Exact overflow/carryover rule.** No source consulted states in
  precise terms whether resources spent on research in excess of the
  exact amount needed to complete a level are carried forward to the
  next level in that field, or are instead lost for that turn. The
  worked examples above assume carryover, consistent with general
  community descriptions of gradual, turn-by-turn accumulation, but
  this specific edge case is an inference, not a confirmed rule.
- **Point in a turn at which `totalLevels` is evaluated.** The "Guts of
  research costs" formula defines `totalLevels` as the empire's current
  total across all fields, but does not say whether, within a single
  turn, a field that levels up partway through resource allocation
  immediately raises `totalLevels` for the *next* field's cost
  calculation that same turn, or whether `totalLevels` is fixed for the
  whole turn and only updates at turn-end. The worked examples in this
  document assume immediate updates (consistent with how the original
  FAQ's own weapons-cost examples reconcile against the base-cost
  table), but this was not independently confirmed by a second source.
- ~~**115% vs. 125% for Generalized Research.**~~ **Resolved** — see
  section 4. The original game's own help text confirms 125% and jokes
  about the odd total itself; "115%" was the GameFAQs guide author's
  own misremembering, not a real discrepancy.
- **Precise default values for the resource-generation constants.**
  Sources give a valid *range* for each race-design economic setting
  (e.g., colonists-per-resource 700–2500) and cite various example
  "defaults" (1,000 colonists/resource; 10 or 15 resources per 10
  factories; 9 or 10 resources to build a factory) that are not fully
  consistent with one another across sources — likely because different
  authors are describing different predefined races (e.g. the
  "Humanoid" starting race) rather than a single wizard-neutral
  default. This document treats 1,000/10/10/10 as a reasonable
  illustrative baseline for worked examples, not a verified universal
  default.
- ~~**Full hull/component prerequisite table.**~~ **Partially resolved
  (2026-09-05)**: `TECHITEM.DOC` (a 1997 fan-made per-tech-level unlock
  table, `techitem.zip` on the Stars!AutoHost wiki downloads page) is
  exactly this — every hull, engine, weapon, armor, shield, scanner,
  mine layer, and terraforming item, organized by field and level, with
  every secondary-field requirement spelled out. It was cross-referenced
  programmatically against this project's `components.xml` (226
  components; 188 matched by name). This found and fixed a genuine,
  systematic bug: **Smart Bomb, Neutron Bomb, Enriched Neutron Bomb,
  Peerless Bomb, Annihilator Bomb, and Energy Dampener** all had their
  secondary tech requirement mislabeled as `Electronics` in
  `components.xml` when the source doc — cited twice per item,
  independently, in both that field's own table and the
  Biotechnology/Energy table — consistently says `Biotechnology` (the
  five `<SMART>`-tagged bombs) or `Energy` (Energy Dampener). Every
  other `Electronics`-tagged bomb (LBU-17, LBU-32, LBU-74 — not
  `<SMART>`) was independently confirmed correct against the same
  source, so this wasn't a wholesale field mixup, just these six items.
  Not yet done: an exhaustive item-by-item pass (188 matches is a lot to
  eyeball one at a time) — the cross-check surfaced ~25 further
  "Biotechnology" discrepancies that all turned out to be a parsing
  artifact in the one-off script used for this pass (confirmed via
  spot-checking a few, e.g. Ultra Driver 12, against `components.xml`
  directly), not real bugs, so a cleaner re-parse would be needed before
  trusting further automated output from this source. The ~51 items in
  the doc with no matching name in `components.xml` were not
  investigated (could be items not yet modeled at all, or just naming
  differences).
- **Cross-reference: the runtime component category/subtype/race-trait gate.** A separate pass
  (`ship-design-and-components.md`) traced the actual code-level function consulted when the
  ship/starbase design editor decides whether a specific component is legal for the current race —
  this is the runtime counterpart to the static `TECHITEM.DOC`/`components.xml` prerequisite data
  above. It confirms components are grouped into exactly 16 fixed categories (one per bit of a
  16-bit word), each with its own subtype-count ceiling, and that PRT-exclusive/LRT-exclusive
  components are gated by two mechanisms: hard-coded per-subtype PRT-id or boolean-flag checks, and
  a generic 32-bit per-race "special ability" bitmask. That document also independently confirms
  this project's PRT-index convention (Hyper Expansion=0 ... Alternate Reality=8, JOAT=9) against
  two exact name/count matches (Engine-family and Bomb-family exclusive components) — potentially
  useful for resolving the "second, unidentified PRT code" noted below, though the two were not
  cross-checked against each other in this pass since they come from different segments.
- ~~**No "percent of resources devoted to research" control found.**~~ **Resolved** — see section 4's
  new "Gap resolved by a later pass" note. The control exists in the Research dialog; it was missed
  earlier because it is a custom-painted increment/decrement readout rather than a standard
  slider/scrollbar. That same pass also found a previously-undocumented live "turns until next level"
  forecast in the same dialog, and a candidate for the tech-level-distance grading mechanism referenced
  from `client-ui-dialog-catalog.md`'s "Slot/equipment editor" entry. **A later pass individually read
  all 7 of the dialog's numbered helper functions** (see section 4's "Individual accounting" note) —
  the tech-distance-grading candidate's own mechanism is now confirmed by direct inspection, not just
  inferred, and no additional undocumented functions were found among the 7. Still open: which specific
  race trait halves the forecast's effective per-turn rate, whether the distance-grading candidate
  function's output is actually what feeds the slot/equipment editor's on-screen grading text, and the
  exact real-world identity of the newly-found category/context lockout-bit gate's context-code
  parameter.
- **New from this pass: PRT-conditional exclusion from the Research dialog's field list, and two
  previously-undocumented starbase cost rules.** Inspection of the exported client (see section 7's
  new "Confirmed by inspection" note) found that building the Research dialog's 7-entry field list
  (6 fields + "lowest field") explicitly skips certain fields for two specific PRT codes (one
  confirmed to be Alternate Reality; the other not yet identified by name), and that upgrading an
  existing starbase design in place costs exactly half of a fresh build, with a further ~20% discount
  for a race-trait condition likely tied to a named racial trait ("Improved Starbases" or similar).
  None of this was previously documented from community sources and none of it was cross-referenced
  against this project's own race-trait/PRT naming table in this pass — doing so (matching the two
  PRT codes found here to this project's own PRT enum) is a natural follow-up.
- **"Slow Tech Advance" and other game-parameter interactions.** The
  source formula states this parameter doubles `totalCost`, but no
  source consulted described how it interacts with `costFactor` or
  `totalLevels` beyond a flat doubling of the final result — assumed
  here to apply after the `costFactor` multiplication, consistent with
  the formula's own ordering, but not independently verified.

- **Miniaturization and Bleeding Edge Technology are unimplemented.**
  The original game's own manual documents both mechanics precisely
  (see section on race-design cost traits, above), and this project's
  documentation now reflects the correct numbers, but neither mechanic
  exists yet in `Common`/`ServerState` — components never get cheaper
  as an empire's tech level rises past a requirement. This is a
  confirmed implementation gap, not just a documentation one.

## Sources

- [stars.hlp — the original Stars! Player's Guide, converted to HTML by the Stars!AutoHost wiki community](https://wiki.starsautohost.org/wiki/Downloads) (file `stars.hlp.html.rar`, under References; used here only to verify mechanics against this project's own clean-room documentation, not copied into it — see `HelpContent/NOTICE-HelpContent.txt` for where the actual converted text is used)
- `TECHITEM.DOC` (1997, from `techitem.zip` on the same Stars!AutoHost wiki downloads page, References section) — a per-tech-level table of every hull/component/weapon/armor/shield/scanner unlock across all six fields. Used only to verify `components.xml` (see the "Full hull/component prerequisite table" entry above); not copied into this repository.
- [Stars! Advanced and Technical FAQ — Table of Contents](http://www.starsfaq.com/advfaq/contents.htm)
- [Stars! Advanced and Technical FAQ — "Guts!" (bombing, tech trading §4.2, research costs §4.3)](http://www.starsfaq.com/advfaq/guts1.htm)
- ["The ultimate way of managing research" by Andrei Romanov — Stars!-R-Us Article](http://www.starsfaq.com/articles/sru/art82.htm)
- [Generalized Research — Stars!wiki](https://wiki.starsautohost.org/wiki/Generalized_Research)
- [Race Design — Stars!wiki](https://wiki.starsautohost.org/wiki/Race_Design)
- [Custom Race wizard (View Race Help) — Stars!wiki](https://wiki.starsautohost.org/wiki/Custom_Race_wizard)
- [Chapter 6: Early Resource Management — Stars!wiki (Stars! Strategy Guide)](https://wiki.starsautohost.org/wiki/Chapter_6:Early_Resource_Management)
- [Population Management — Stars!wiki](https://wiki.starsautohost.org/wiki/Population_Management)
- [Tech analysis: weapons Vs anything else, by Nick Bennett (5th April 2001) — Stars!wiki](https://wiki.starsautohost.org/wiki/Tech_analysis:_weapons_Vs_anything_else_by_Nick_Bennett_-_5th_April_2001)
- [Stars! Strategy Guide (PC), by "plague006" / Mars Jenkar — GameFAQs](https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043)
