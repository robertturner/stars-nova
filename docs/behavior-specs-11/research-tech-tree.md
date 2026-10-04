# Research and Tech Tree Mechanics

Clean-room behavior specification, compiled originally from public community
documentation (fan FAQs, the Stars!AutoHost wiki, and a GameFAQs strategy
guide) and restated in original wording. Later revisions added findings from
analysis of the original executable (the component prerequisite table and the
tech-level availability check), cited as evidence pointers by function and
line number; no code, disassembly or unique strings are reproduced. See **Sources** at the end for every URL used,
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

| Symbol | Meaning | Range | Default | Notes |
|---|---|---|---|---|
| A | Colonists needed per 1 resource | 700 – 2500 | 1,000 | lower = more resources per colonist; stored as 7–25, displayed ×100 |
| B | Resources produced per 10 operating factories | 5 – 15 | 10 | |
| C | Resources needed to build one factory | 5 – 25 | 10 | |
| D | Factories operable per 10,000 colonists | 5 – 25 | 10 | |
| G | Resources needed to build one mine | **2** – 15 | 5 | (mineral-side settings F/H — mine output 5–25, default 10, and mines operable per 10,000 colonists 5–25, default 10 — are not relevant to research) |

**Ranges and defaults are recovered directly from `stars.exe` (this pass).** Earlier they were community-sourced. They come from the race wizard's per-setting min/max bound table and its default (Humanoid) preset record. See Open Questions below and `race-designer-ui-and-availability.md` "Economic-settings stage" for the extraction and cross-checks. Every range except G matches the published figures. G's floor is 2, not 3, and the point-total formula's dedicated sub-3 mine-cost branch corroborates that (`race-traits.md` §1a).

A colony's resources for the year are the sum of two parts:

- **Population part:** the population divided by A, rounded down.
- **Factory part:** the number of operable factories divided by 10, rounded down, then multiplied by B.

The number of operable factories is the smaller of the factories actually built and the population divided by 10,000, multiplied by D.

The wizard default is A=1000 (1 resource per 1,000
colonists; confirmed from the binary this pass), matching the community shorthand "1 resource per 1000 pop
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
- **Per-planet leftover-only research
  checkbox**: when checked for a colony, that colony ignores the global
  research percentage entirely and instead sends research only whatever
  resources are left over after its own production queue (factories,
  mines, terraforming, defenses, ships) is fully funded for the year.

A widely repeated piece of community strategy advice observes that,
functionally, **a colony always spends any leftover resources on
research regardless of the percentage slider** — so setting the global
research percentage to 0% while leaving the leftover-only research
box unchecked on every planet produces the same result as
checking that box everywhere: every planet builds everything it can
first, and only the remainder feeds research
[starsfaq.com/articles/sru/art82.htm]. Raising the percentage above 0%
(without the leftover-only checkbox) instead *reserves* a slice of a
colony's resources for research even when that colony still has useful
things it could build — deliberately trading short-term colony growth
for faster tech.

### 3. Cost curve for tech levels

The authoritative community derivation ("Guts of research costs",
credited to Bob Martin) gives the formula: the total cost of a level equals (the base cost for
that level plus 10 times the empire's total tech levels) multiplied by
the field's cost factor. The terms are:

- **Base cost for the level** — a fixed table, keyed only by the level being
  purchased (see below), identical for every field.
- **Total tech levels** — the sum of the empire's current tech levels across
  **all six fields combined** (available on the in-game Score screen).
  This term makes every subsequent level, in every field, gradually
  more expensive as the empire's overall tech investment grows.
- **Cost factor** — set per field at race-design time:
  - 0.5 if that field is set "Costs 50% less" ("cheap")
  - 1.0 if left at the normal setting
  - 1.75 if that field is set "Costs 75% extra" ("expensive")
- If the game parameter **"Slow Tech Advance"** is enabled for the
  game, the whole total cost is doubled.

[starsfaq.com/advfaq/guts1.htm]

**Structurally confirmed by inspection of the exported client.** A component-design-import routine independently computes a component's usable tech level and separately applies a cost calculation matching the "base cost plus 10 per total tech level" shape almost exactly, with a per-field cost-class modifier (Cheap/Normal/Expensive) applied afterward and the whole result doubled under a global flag consistent with "Slow Tech Advance." ~~The same routine also clamps a component's usable tech level to a 4-level window above the field's current level, with starbases exempted from part of this check.~~ **Superseded — there is no availability window.** Availability is a plain per-field minimum (see the resolved Open Questions entry "Full hull/component prerequisite table", *How the game checks the bytes*): an item is available only when every one of the six fields is at or above its stored requirement. The only level clamp in the component-cost path is on the *miniaturization margin* used for cost: the race's smallest surplus over any nonzero requirement, capped at 19 levels, turned into a 4%-per-level discount capped at 75% (5%/80% with Bleeding Edge Technology), with terraforming and planetary scanners/defenses exempt (component-cost routine, `stars.exe.export.c:36956`-`36996`). It changes cost only, never the level at which an item becomes available. Separately, the game's per-player record structure independently confirms exactly 6 contiguous tech-level fields, matching the "six independent fields" claim at the data-layout level.

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
already folds in the 10-per-total-level surcharge along the way)
[starsfaq.com/advfaq/guts1.htm].

**Race-design cost settings.** Each of the six fields is independently
set to normal, "Costs 50% less," or "Costs 75% extra" during race
creation; this is a fixed racial trait for the whole game, not a
per-turn choice. A race can also check the race-wizard option that makes every
"Costs 75% extra" field start at Tech 3 (Tech 4 instead, for a Jack-of-All-Trades race),
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
- **Generalized Research** — see section 4 below; it does not change
  `totalCost`, only how a turn's research budget is spread across
  fields.

### 4. Allocating a turn's research budget across fields

By default, **all** of a turn's research resources go to a single
field at a time — the one the player has currently selected as the
research target (or, with the "lowest field" auto-target option, the
game auto-selects whichever of the six fields currently has the lowest
level and points the whole budget there).

**Confirmed and clarified by inspection of the exported client.** The Research dialog exposes exactly **6** mutually-exclusive radio buttons for field selection — directly confirming "all resources go to one field at a time," selected by radio button rather than any slider or percentage split. The dialog additionally stores a **second, independent setting** — a "next field" combo box whose values are the 6 named fields plus a "lowest field" auto-select option — packed into a different part of the same per-player byte as the active-field selector. This is a strong, code-level confirmation of the "lowest field" auto-target option specifically. *(Exact list, code pass: the combo holds eight entries, dynamic strings 83-90, in this order: keep the same field, the six fields, the lowest field; stored as 6, 0-5 and 7. The lowest-field choice is the field with the lowest current level among all six, ties to the earlier field, for every race; there is no PRT-dependent exclusion, §7. When the current field reaches level 26 with "same field" selected, the next field becomes the lowest field, `FUN_10b8_4ce4` `:80158`-`80173`.)*

**Gap resolved by a later pass: the "percent of resources devoted to research" control does exist, in the same dialog.** It was missed by the earlier inspection because it is not a standard slider/scrollbar control but a custom-painted readout with two small increment/decrement hit-regions (sharing the same click-and-hold auto-repeat mechanism used elsewhere for spinner-style controls — see `client-ui-dialog-catalog.md`'s "Segmented bar/gauge and repeating-button controls"), clamped to the documented 0-100% range. Dragging or clicking either arrow immediately recomputes, live, the empire-wide total resources that would be diverted to research at the new percentage — summed across every production-queue entry belonging to the current player empire-wide, not per-planet — confirming this setting is exactly the empire-wide percentage this document's opening overview describes, not a per-planet value.

**New: a "turns until this field's next level completes" forecast, previously undocumented.** The same dialog computes and displays a live estimate of how many turns remain before the currently-targeted field reaches its next level, by dividing the field's outstanding cost (current cost table lookup minus progress already banked) by the just-recomputed empire-wide per-turn research contribution described above. **Identified, not just gated by an unnamed bit:** a race-trait check (bit 4 of the same 32-bit per-race trait/flag word, at struct offset 0x4e (the accessor's shift is confined to 0-31), read through the shared bit-test accessor also used for Ultimate Recycling's bit 5 check in `race-traits.md` §3a — same function, adjacent bit) halves the effective per-turn contribution used in this forecast. This bit is **Generalized Research**, confirmed two ways: the mechanical fit (Generalized Research sends only 50% of a turn's budget to the currently-selected field, so halving the forecast's per-turn-contribution figure for that field is exactly correct), and a direct code-level corroboration — the same forecast function, roughly 90 lines later, reuses the identical bit-4 test to decide whether to paint an on-screen label reading the real recovered string "Generalized Research" (a neighboring bit-12 test in the same block similarly gates a "Bleeding Edge Technology" label), so the trait identity comes from the game's own label text, not analogy alone. Separately, the same dialog's initialization independently re-derives the section 3 cost formula end-to-end: it sums the current race's own 6 tech-level fields into a running total (confirming, at a second, independent code site, that the total tech level count is exactly one race's own cross-field level sum), adds the level-keyed base-cost table entry, and applies a per-field cost-class multiplier with three distinct branches matching cheap/normal/expensive — corroborating the community-sourced formula in section 3 from the dialog that actually uses it turn-to-turn, not only from the component-import routine noted there previously.

**New: the tech-level-distance grading mechanism, cross-referenced from `client-ui-dialog-catalog.md`'s "Slot/equipment editor" entry.** That entry documents a graded (not binary) unavailability distinction — "exactly one technology level away" versus "needs multiple fields raised" — and notes the mechanism computing this grading had not been traced to a specific function. This pass found a strong candidate in the same Research-dialog code cluster: a function that, given a target level for each of the 6 fields (e.g., a component's or hull's prerequisite combination), temporarily simulates leveling up every field currently below its target — accumulating the total additional resources such a climb would cost field-by-field using the same per-field cost calculation described above — then restores the real, unmodified tech levels before returning that total. This produces exactly the kind of number (and, incidentally, the field-by-field level deltas) a "how far away is this item, and what would it cost to get there" tooltip would need. **Confirmed, not just inferred, by a later pass:** the grading function's return value is consumed directly, within the same enclosing function, by a string-table lookup and text-drawing call selecting one of four message variants (including a distinct "unavailable" style label for the boundary case) *(the four variants, exact, gap-report pass; detail-card renderer `FUN_10d8_1e40` `:90055`-`90094`, cost routine `FUN_10d8_4a86` `:91753`-`91833`: they are not "one level short / further away" wordings. Below the item's list of required tech levels (or a "none" placeholder, dynamic string 847, when it has no requirement) the card prints one status line. The card first asks the availability resolver about the item. If the answer is "forbidden for this race" or "not a valid item", the line is string 850, a short "unavailable" label drawn in red. Otherwise it asks `FUN_10d8_4a86` for the research resources still needed to bring every deficient field up to the item's requirement: the cost of every missing level, field by field, less what is already banked in each field, never below zero per field, with the current player's real levels restored afterwards. An item with any requirement above 26 (a part that cannot be researched) also gets the red "unavailable" line. A total of 0 prints string 851, a plain "available" label. A total from 1 to 99,999 prints string 849, a cost line giving that number of resources. A total of 100,000 or more prints string 848, the same cost line in thousands with a "k" suffix, rounded to the nearest thousand. The resolver's finer grades, "only the field being researched is short, by one level" and "by more levels", get no wording of their own: the card treats every grade from 1 up the same and shows the cost. Message ids are dynamic-string ids in `extracted-game-data/dynamic-strings.txt`.)* — that enclosing function is the shared detail-card renderer itself (see the "Shared detail-card renderer" bullet below), which paints via the Search/record-browser dialog's paint handler and via segment 25's hover popup. This closes the gap: the tech-distance-grading figure genuinely is painted as on-screen text through the shared detail-card renderer that feeds the Slot/equipment editor's hover/selection display. ~~One softer point remains open: a second, separate caller of the shared detail-card renderer (reached from an unidentified dispatcher in segment `10c0`) was not independently confirmed to be the Ship/Starbase Designer by name~~ — **resolved this pass.** Segment `10c0` *is* segment 25 in this project's own ordinal numbering (the same `10b8`→24, `10c0`→25, `10c8`→26, `10d0`→27, `10d8`→28 sequence already used throughout this document and `production-queue.md`), so this "unidentified segment-`10c0` dispatcher" is the same mode-switched popup dispatcher `client-ui-dialog-catalog.md`'s "Toolbar, tooltip, and contextual popup" section already documents — concretely, `FUN_10c0_0130`. Reading that dispatcher's own switch directly: **case 9** (of cases 5 through 10 plus a default) calls the shared detail-card renderer (`FUN_10d8_1e40`) exactly. `client-ui-dialog-catalog.md`'s own independent trace of that same dispatcher's **mode 9** (matching this case number precisely) already identified every one of mode 9's triggering call sites as living *inside the Ship and Starbase Designer's own code* — a component-slot drag-and-drop legality check and the slot editor's component-category listbox subclass procedure, both reached from the same code region already documented there as building the slot editor's offered-component list. So the second caller is confirmed: it is reached from the Ship and Starbase Designer, via this shared segment-25 popup dispatcher's mode/case 9, not called directly by name from the Designer's own dialog procedure — a one-hop indirection through a shared dispatcher rather than a direct call, but the identity itself is no longer in doubt.

**Individual accounting of the Research dialog's 7 numbered helper functions (Ghidra `FUN_10d8_*`), closing out the last item this section had left as an aggregate description.** A later pass read each of the segment's 7 numbered helpers individually rather than only sampling them; all 7 turn out to be exactly the paint/hit-test/cost-support roles already characterized above, with no new subsystem hiding among them:

- **Paint routine** — draws the dialog's live info panel: a sequence of bit-flag-gated label/value lines (tech-level list, current field's cost, empire resource contribution, and related stats), each looked up from the string table and formatted with the client's shared numeric-format helper. It also *inline-computes* the "turns until next level" forecast described above at the point it is drawn — the outstanding-cost/per-turn-contribution division and the race-trait-conditional halving both live directly in this paint routine, not in a separate calculation function.
- **Hit-test / stepper-drag routine** — tests a click point against the two increment/decrement rectangles the paint routine records, then drives a repeat-button drag loop that clamps the "percent of resources devoted to research" value to 0–100, recomputes the empire-wide contribution total on each step (via the recompute helper below), and repaints. Outside those rectangles it hit-tests dialog list rows and hands off to the segment-25 mode-switched hover-popup dispatcher (see "Toolbar, tooltip, and contextual popup" in `client-ui-dialog-catalog.md`) for row tooltips.
- **Cost-lookup formula** — the section 3 cost-formula re-derivation itself: sums the race's 6 tech-level fields, adds a level-and-field-indexed base-cost table entry, applies the per-field cost-class multiplier (cheap/normal/expensive), and finally applies the game-wide Slow Tech Advance doubling (see the Open Questions entry on Slow Tech Advance). This is the low-level primitive both the forecast (in the paint routine) and the tech-distance-grading candidate (below) call to price one additional level.
- **Shared detail-card renderer** — the already-documented ~1847-line object/context-detail-card renderer shared with `BROWSERDLG` and segment 25's hover popup; see `client-ui-dialog-catalog.md`'s "Search and record browser" section for its full write-up. No new findings from this pass.
- **Live-contribution recompute helper** — the function actually backing the "recomputes live, the empire-wide total resources that would be diverted to research at the new percentage" behavior described above: it temporarily overrides the race's stored percent-of-resources value with a candidate value, sums the resulting per-turn research contribution across every one of the player's colonies, then restores the real stored value before returning the total.
- **Tech-level-distance grading candidate** — the target-level-to-total-cost function discussed in the paragraph above (individually confirmed this pass; see also the corresponding update in `client-ui-dialog-catalog.md`'s "Slot/equipment editor" entry). **Now fully confirmed** (not just a candidate): its return value is consumed directly by the shared detail-card renderer's own text-drawing code, closing the link to the on-screen tooltip.
- **Category/context lockout-bit gate** — a small lookup that maps a (component-category, context-code) pair to one bit of a 32-bit flag word via a fixed table, then reports "available" only when that bit *is* set in a per-player flag word (*corrected 2026-10-01:* this was earlier written as "not already set". The gate at `stars.exe.export.c:91908` blocks a listed item whose bit is still clear, so the bit is a "has been given this part" record; see `ship-design-and-components.md` §14a). The category values are exactly the same 16 component-category bit-flags `ship-design-and-components.md` §4 documents, and the overall shape (category bit → per-race flag bit → available/blocked) matches that document's per-race special-ability (one-time-gift) mask gate, first described there as 32 bits wide but since shown to be a 16-bit word (§14a) — a plausible second, concrete implementation of that same gate, though not proven identical. **The "context-code" parameter's identity is now resolved, this pass.** The function itself is `FUN_10d8_4b8e` (segment 28); three of its literal call sites in the exported source (`stars.exe.export.c:3294`, `:3499`, `:3604`) are all *inside* `FUN_1008_5194` — the very same segment-2 component/hull category-and-subtype availability resolver `ship-design-and-components.md` §4/§14 already documents — and each call passes that resolver's own first input (the packed category/subtype word) straight through unchanged. In other words, the "context code" is not a UI control/accelerator id as previously speculated; it is the **subtype index within the category**, the identical (category, subtype) descriptor already used throughout this project's component/hull identity work, read from the low byte of the same packed word the resolver itself was just given. This lockout-bit gate is therefore best understood as one further, generic per-(category,subtype) availability check layered directly inside `FUN_1008_5194`'s own dispatch — called immediately before it returns "available" for several (though not all — roughly 11 of the 16 categories have at least one subtype wired into this gate's fixed table) categories — rather than a separately-reached UI-only mechanism.

**A fourth call site was found this pass, outside `FUN_1008_5194` entirely, and it corroborates the "subtype byte, not a UI-context id" reading independently.** `stars.exe.export.c:89738` calls `FUN_10d8_4b8e` directly on a pair of the dialog's own global cursor fields, with no intervening resolver call — and this call site lives inside `BROWSERDLG` (the Search/record browser dialog, `stars.exe.export.c:89482`-onward, already documented in `client-ui-dialog-catalog.md`'s "Search and record browser" section as the host of the shared detail-card renderer this document's section 4 discusses elsewhere). There, those two adjacent globals are the dialog's own live "currently highlighted category, currently highlighted subtype" cursor pair (the same globals `FUN_1008_5194` itself is repeatedly called against a few lines earlier in the same loop, at `stars.exe.export.c:89659` and `:89705`, to step the cursor to the next available item) — confirming, via a second, independent, resolver-free call site, that `FUN_10d8_4b8e`'s second field really is just "whichever subtype the caller currently has selected," with no separate UI-context enum anywhere in the picture. This also corrects the earlier count: the gate function has (at least) **four** literal call sites, not three, though the fourth changes nothing about the parameter's identity — it uses the identical packed-word convention as the other three.

The lesser racial trait **Generalized Research** changes this: only
half (50%) of the turn's research budget is applied to the
currently-selected field, and 15% of the budget is additionally applied
to *each* of the other five fields. Because 50% + 5×15% = 125%, this
trait yields more total research throughput than a focused strategy for
the same resource spend, at the cost of being unable to "rush" a single
field to unlock something urgently
[wiki.starsautohost.org/wiki/Generalized_Research],
[wiki.starsautohost.org/wiki/Custom_Race_wizard]. **125% is confirmed**
directly against the original game's own help text (the Generalized
Research topic in stars.hlp states, in paraphrase, that half of the
research budget goes to the current field and 15% of the total goes to
each of the fields, and it openly acknowledges that this sums to 125%). The "115%" figure a GameFAQs strategy-guide author attributed
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
over time (e.g., the leftover-only research strategy
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
so the chance of learning at least one field, given n fields of
advantage, is 0.5 multiplied by (1 minus 0.5 raised to the power n).
For example, one field of advantage gives 25%, two give 37.5%, and
three give 43.75%; the chance approaches but never reaches 50%.

Only one tech level, from one field, from one source, can be gained
per turn regardless of how many qualifying events occur; multiple
qualifying events in the same turn each roll independently, so playing
more of them (e.g. scrapping several separate fleets, or running
several invasion sites at once) raises the chance that *at least one*
succeeds: with k independent attempts, the chance is 1 minus (1 minus
the single-event chance) raised to the power k. Turn order matters: scrapping resolves first, then
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
- **Weapons 5** (+ Electronics 8; the community source's "Electronics 6" is
  corrected by the executable's own record): first LBU installation-killing bomb;
  (+ Biotechnology 7): first Smart Bomb.
- **Weapons 10** (+ Propulsion 2): first torpedo with better than 50%
  base accuracy.
- **Propulsion 5** (+ Construction 5): first stargate.
- **Construction 3**: Cargo Pod, Destroyer Hull, Medium Freighter Hull.
- **Construction 4**: Privateer Hull; Space Dock starbase (requires
  also having chosen the Improved Starbases trait); (+ Electronics 2):
  Robo-Miner (barred to Only Basic Remote Mining races). *Corrected from
  the executable's records:* this rung was earlier given as the
  Robo-Mini-Miner needing Advanced Remote Mining. In fact the
  Robo-Mini-Miner needs only Construction 2 + Electronics 1 and no trait.
  The robots that need Advanced Remote Mining are the Robo-Midget and
  Robo-Ultra Miners.
- **Electronics 7** (+ Energy 3, Biotechnology 2): first scanner with
  penetrating range (unavailable to races with the No Advanced Scanners
  trait).
- **Biotechnology 4** (+ Energy 2): minelaying ability for non-Space
  Demolition races (the Mine Dispenser 50, which War Monger races cannot
  use).

This is only a representative subset focused on early/mid-game
breakpoints. The complete table for all 239 hull and component records
is read directly from the executable. It is summarized, with the exact
rules the game uses to check it, in the resolved Open Questions entry
"Full hull/component prerequisite table", and the full per-item dump is
in `extracted-game-data/component-stats.tsv`.

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
  a field (as opposed to *listing* an already-designed ship/starbase) is handled elsewhere, not by
  this segment: item availability is the plain per-field minimum check `FUN_1008_5916` (see the
  resolved Open Questions entry "Full hull/component prerequisite table"); there is no "4-level
  window" (an earlier reading, superseded in §3), and the only level clamp nearby is the
  miniaturization *cost* margin.
- ~~**PRT-based exclusion from the Research dialog's field-selection lists.**~~ **Withdrawn (code pass): there is no PRT-conditional exclusion anywhere in research.** The 7-step loop described below is the production catalog's auto-build list in `FUN_10d0_010c` (`:86304`-`86319`): its seven entries are the auto-build item types 0-6, not research fields, and the two exclusions are Alternate Reality losing Mines, Factories and Defenses (types 0-2) and Claim Adjuster losing Min and Max Terraform (types 4-5) (`production-queue.md` 10l). The Research dialog (`RESEARCHDLG`, `:88717`-`88770`) enables all six field buttons and fills its next-field list with all eight entries (dynamic strings 83-90: the same-field choice, the six fields, the lowest-field choice) for every race. The lowest-field choice, both in the host's research buy loop (`FUN_10b8_4ce4`, `:80161`-`80173` and `:80239`-`80250`) and in the computer player's research chooser (`:62851`-`62859`), takes the field with the lowest level among all six, ties going to the earlier field in the order Energy, Weapons, Propulsion, Construction, Electronics, Biotechnology, with no race test. The superseded reading follows for the record. When building the
  candidate list of "next field to research" entries (feeding both the 6-way radio-button selector
  and the "lowest field" auto-target combo box documented in section 4), the code iterates the 6
  fields plus one extra slot (7 total, matching the already-confirmed combo-box count) and
  explicitly excludes candidates for two specific per-race PRT codes: one PRT code (independently
  confirmed elsewhere in this pass to mean **Alternate Reality**) excludes the first three fields
  (Energy/Weapons/Propulsion by this document's field ordering) from consideration in specific
  contexts; a second PRT code excludes the last two (Electronics/Biotechnology) in an
  analogous way — **now identified: PRT index 3, which this project's PRT-index convention
  (`ship-design-and-components.md` §5) maps to Claim Adjuster.** The code-level identification is
  solid (exact structural match: same 7-iteration loop, same per-race PRT-index accessor, same
  two-disjoint-group shape as the confirmed Alternate Reality branch), but the gameplay rationale for
  why Claim Adjuster specifically would exclude Electronics/Biotechnology from research targeting was
  not independently corroborated by any community source (the rule itself is code-confirmed; a
  community rationale would add flavour, not behaviour). **Partially corroborated this pass from
  the game's own recovered text.** Claim Adjuster's primary-trait description, recovered directly
  from the client's dynamic-string table (`extracted-game-data/dynamic-strings.txt`, identifiers
  285-287), reads in substance: the race starts with Tech 6 in Biotechnology already, its
  terraforming ability (framed as the trait's whole identity) works "from orbit" at no resource cost
  and scales with whatever tech level the race already has, and planets it holds long-term have a
  chance to permanently improve on their own. This gives a plausible in-fiction half-explanation for
  **Biotechnology**'s exclusion specifically: Claim Adjuster's signature ability already starts
  substantially teched-up and improves passively without further investment, so a "what field should
  I research next" helper reasonably has less reason to ever suggest it. No equivalent connection to
  **Electronics** was found anywhere in Claim Adjuster's recovered description text or its documented
  numeric modifiers (`race-traits.md` §2's table) — that half of the exclusion remains unexplained by
  any source consulted, code or text. This is a previously-undocumented,
  code-level-only detail — no community source consulted for this document mentions PRT-conditional
  exclusions from the field-selection list — and the exact gameplay meaning of "excluded from
  consideration" (hidden entirely vs. simply deprioritized) was not fully traced. Note: the builder
  function itself (`FUN_10d0_010c`) lives in segment 27, not segment 28 alongside the other
  Research-dialog `FUN_10d8_*` helpers — it is a shared candidate-list builder also used by the
  production-queue/ship-design dialogs (segments 18-24), consistent with
  `ship-design-and-components.md` §5's description of one packed-item-list builder feeding both
  dialog families.
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
  this pass. **Trait identity now cross-referenced, this pass, with a strong (though not
  string-label-proven) mechanical fit: the code-level condition is bit 3 of the same 32-bit per-race
  trait/flag word (offset 0x4e) already hosting Generalized Research (bit 4) and Ultimate Recycling
  (bit 5), read via the identical bit-test accessor `FUN_10e0_226e` — found in the
  cost-computation routine (`FUN_10d0_221a`, segment 27, the same function documented in
  `production-queue.md` §5-§6): when that bit is set, or the race's PRT index is 8, one fifth of the
  cost is subtracted.** This is an exact structural match for `race-traits.md` §3's documented pair:
  **Improved Starbases (ISB)**, which makes starbases 20% cheaper, and **Alternate Reality (AR)**,
  which gives the same 20% starbase discount without stacking with Improved Starbases — the code's
  either-or condition (one LRT bit, or the AR PRT,
  either alone triggering the identical -20%, with no path to apply it twice) is precisely the
  "does not stack" rule already documented from the trait tables, applied to the identical PRT index
  (8) already confirmed elsewhere in this project to be Alternate Reality. No direct string-label
  confirmation (of the kind found for Generalized Research's bit 4 via its on-screen paint-gate
  label) was located for bit 3 specifically, and no positional/ordinal proof was attempted (the two
  other known bit assignments, GR=4 and UR=5, are adjacent to each other but not adjacent to their
  order in `race-traits.md`'s own LRT table, so wizard-checkbox order cannot safely be used to
  predict bit numbers). Treat "bit 3 = Improved Starbases" as strongly evidenced by mechanical fit
  rather than proven by a label the way GR's bit was. **Now proven (lesser-trait re-check pass).**
  The GR/UR "mismatch" came from a mis-ordered LRT table in `race-traits.md`, since corrected. The
  wizard's lesser-trait page binds checkbox *i* directly to bit *i* and to name string `306 + i`
  (`RACEWIZARDDLG5`, `stars.exe.export.c:93855`-`93871`), so wizard order *is* bit order and bit 3 is
  Improved Starbases. The component resolver independently makes the Space Dock and Ultra Station
  require bit 3 (`:3459`-`3463`).
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

### 7a. The Technology Browser (`BROWSERDLG` `:89482`-`89764`, card window `BROWSERWNDPROC` `:89768`-`89856`, card painter `FUN_10d8_1e40`)

A modeless window (dialog template 128; Help menu or F2) showing one item at a time on the shared detail card, with Prev and Next buttons, a category drop-down, a show-only-available checkbox and Close.
- *Categories.* The drop-down holds 17 entries, dynamic strings 1087-1103, in this order: All, Armor, Beam Weapons, Bombs, Electrical, Engines, Mechanical, Mine Layers, Mining Robots, Orbital, Planetary, Scanners, Shields, Ship Hulls, Starbase Hulls, Terraforming, Torpedoes (alphabetical after All). Entries 1-16 map, through a table in the browser's own code segment (segment 28, offset `0x69c`), to the component categories `0x0008`, `0x0010`, `0x0040`, `0x0800`, `0x0001`, `0x1000`, `0x0100`, `0x0080`, `0x0200`, `0x8000`, `0x0002`, `0x0004`, `0x4000`, `0x0400`, `0x2000`, `0x0020`. Choosing a category shows its item 0 when the checkbox is clear, or its first currently available item when it is set; choosing All does the same starting from Armor.
- *Opening state.* The drop-down is set to All, the checkbox is clear (the code never sets it), and the card shows Armor item 0 (Tritanium), unless another surface opened the browser on a specific item, in which case that item is shown. The live screenshot `docs/ui-reference/live-game-command-0x88-technology-browser.png` shows exactly this state.
- *Entry order and paging.* Items are visited in subtype order within a category. Next and Prev step one subtype at a time and wrap: with a specific category selected they wrap within it; with All selected they run on into the next or previous category of the table (wrapping from Torpedoes to Armor and back), so All pages through every category in the list order. An item is shown when the availability resolver reports it available, or, with the checkbox clear, when it exists at all, except that a part from the one-time gift set (Mystery Trader and similar parts, `ship-design-and-components.md` §14a) is skipped while the race has not received it. So with the checkbox clear the browser also shows parts the race cannot build yet or can never build (other races' exclusive parts); with it set, only what the race can build now.
- *Status line.* Under the requirement list (dynamic string 847, a "none" placeholder, when the item has no requirement) the card prints one of four lines: 851 (available) when no research is missing; 849 (a cost in resources) when the research still needed is 1 to 99,999 resources; 848 (the same cost in thousands, with a k suffix, rounded to the nearest thousand) from 100,000 up; 850 (unavailable, drawn in red) when the item is forbidden for the race, is not a valid item, or needs a level above 26. The resource figure is the research cost of every missing level, field by field, less what is already banked (§4, "the four variants").

### Worked Example A — single field, early game, normal cost

Assume a colony with population 500,000, A = 1,000 colonists/resource,
100 operating factories at B = 10 (10 resources per 10 factories),
producing 500,000 / 1,000 = 500 resources from population plus
(100 / 10) × 10 = 100 from factories, for 600 resources per year.

The empire has all six fields at level 0 (total tech levels 0) and is
researching **Energy** (normal cost, cost factor 1.0), with the
research percentage set high enough that 150 resources/year reach
research.

- **Year 1**: 150 available.
  - Level 1 cost = (50 + 0×10) × 1.0 = **50**. Spend 50, 100 remain.
  - the total tech level count is now 1 (Energy is 1, everything else 0).
  - Level 2 cost = (80 + 1×10) × 1.0 = **90**. Spend 90, 10 remain.
  - the total tech level count is now 2.
  - Level 3 cost = (130 + 2×10) × 1.0 = **150**. Only 10 available —
    not enough; the 10 are banked toward level 3.
  - End of Year 1: **Energy 2**, with 10/150 banked toward Energy 3.
- **Year 2**: another 150 arrives, plus the 10 banked = 160 available
  toward the 150 needed for level 3.
  - Level 3 completes, spending 150; 10 resources are left over.
  - the total tech level count is now 3.
  - Level 4 cost = (210 + 3×10) × 1.0 = **240**. The remaining 10 bank
    toward it.
  - End of Year 2: **Energy 3**, with 10/240 banked toward Energy 4.

### Worked Example B — cost-factor and Generalized Research interaction

An empire has taken **Generalized Research**, made **Weapons** "cheap"
(cost factor 0.5), and made **Biotechnology** "expensive" (cost factor
1.75); Energy, Propulsion, Construction, and Electronics are all
normal cost. Current levels: Energy 5, Propulsion 5, Construction 5,
Electronics 5, Biotechnology 5, Weapons 8 — so the total tech level
count is 5×5 + 8 = 33. The player has selected **Weapons** as the primary research
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

Two colonies both have the leftover-only research box ticked,
per the community strategy of leaving the global research
percentage irrelevant and letting fully-developed planets fund research
automatically [starsfaq.com/articles/sru/art82.htm]. The empire is
researching **Construction** (normal cost), currently at level 3, with
all other fields at 0, so the total tech level count is 3.

- **Colony A**: generates 300 resources this year; its production queue
  (more factories, mines) still has 250 resources' worth of useful work
  queued, so only 300 − 250 = **50** resources are leftover for
  research.
- **Colony B**: generates 150 resources; it is already fully built out
  (queue empty), so all **150** resources are leftover for research.
- **Empire research income, Year 1**: 50 + 150 = 200.

Construction level 4 costs (210 + 3×10) × 1.0 = **240**. The 200
available is short by 40; all 200 are banked toward level 4.

- **Year 2**: Colony A's production queue has advanced further and now
  only needs 180 resources of the same 300 it produces, leaving 120
  for research; Colony B still contributes its full 150 (still nothing
  else to build). Empire research income = 120 + 150 = 270.
  Combined with the 200 banked from Year 1: 470 available against the
  240 needed.
  - Construction reaches **level 4**, consuming 240; 230 resources are
    left over.
  - the total tech level count becomes 4.
  - Construction 5 costs (340 + 4×10) × 1.0 = **380**. The 230 leftover
    resources bank toward it.
- End of Year 2: **Construction 4**, with 230/380 banked toward
  Construction 5, achieved without the player ever manually touching
  the research percentage slider on either colony.

## Open Questions / Uncertainties

- ~~**Exact overflow/carryover rule.**~~ **RESOLVED by inspection of
  the exported client, this pass.** No source consulted states in
  precise terms whether resources spent on research in excess of the
  exact amount needed to complete a level are carried forward to the
  next level in that field, or are instead lost for that turn. The
  worked examples above assume carryover, consistent with general
  community descriptions of gradual, turn-by-turn accumulation, but
  this specific edge case is an inference, not a confirmed rule.
  **The actual per-turn, per-race, per-field research-spending routine
  was traced directly: `FUN_10b8_4ce4` (segment 24 — the same segment
  hosting the production-queue turn-generation loop documented in
  `production-queue.md` §8).** For each race and each field with an
  accumulated resource pool, it runs a tight buy loop: price the next
  level via the section-3 cost formula (`FUN_10d8_1580`), and if the
  pool covers that price, subtract the price from the pool, increment
  the field's stored tech-level byte in place, and **loop back and
  price the level after that with whatever remains in the pool** —
  continuing to buy consecutive levels in the same field, in the same
  turn, for as long as the leftover pool keeps covering the next
  price. Only when the remaining pool can no longer afford the next
  level does the loop stop and write the leftover amount back into the
  field's stored resource-pool field, to be added to next turn's
  income — this is the literal code-level banked-progress mechanism the
  worked examples already assumed. This confirms carryover exactly:
  surplus is never discarded mid-turn, and can buy more than one level
  of the same field in a single turn if the pool is large enough.
  **Level ceiling in the buy loop (gap-report pass, `:80137`-`80139`).** The loop never raises a
  field above level 26. It never raises a field above level **10** for a race that has per-race
  status bit `0x2` or bit `0x4` set (race record byte `0x54`). Bit `0x4` is the penalty bit of
  `turn-generation-engine.md` §1 step 7. Bit `0x2` comes only from an orders-file header flag this
  build never writes (`turn-generation-engine.md` §5a). The Mystery Trader uses the same 26-or-10
  cap, but keyed on bit `0x2` alone.
- ~~**Point in a turn at which the total tech level count is evaluated.**~~
  **RESOLVED by inspection of the exported client, this pass.** The "Guts of
  research costs" formula defines the total tech level count as the empire's current
  total across all fields, but does not say whether, within a single
  turn, a field that levels up partway through resource allocation
  immediately raises the total tech level count for the *next* field's cost
  calculation that same turn, or whether the total tech level count is fixed for the
  whole turn and only updates at turn-end. The worked examples in this
  document assume immediate updates (consistent with how the original
  FAQ's own weapons-cost examples reconcile against the base-cost
  table), but this was not independently confirmed by a second source.
  **`FUN_10d8_1580` (the cost formula, see above) re-sums the race's
  six current tech-level bytes fresh on every single call**, rather
  than taking a cached or turn-start snapshot of the total tech level count. Because
  the buy loop in `FUN_10b8_4ce4` increments a field's level byte in
  place *before* looping back to price the next level — and because
  that next pricing call re-reads all six level bytes live — a level
  bought earlier in the same turn (in the same field, or, since the sum
  is over all six fields, in any field processed earlier that turn) is
  already reflected in the total tech level count for every subsequent price lookup
  within that turn. Updates are immediate, not deferred to turn-end,
  confirmed directly from the calculation rather than inferred from
  worked-example arithmetic.
- **Refinement to the "second pass" framing in `turn-generation-engine.md` §7.** That document
  earlier described a general second redistribution pass for leftover research resources (it has
  since been updated to match this refinement). Tracing `FUN_10b8_4ce4` fully shows this "second pass" is not a
  generic leftover-resource sweep: after the buy loop above runs once for every race, the function
  checks (via a race count captured earlier) whether more than one race is in the game, and if so
  runs one further block that matches Super Stealth's documented passive research bonus exactly —
  for any race with PRT 1, it computes, per field, half of the *average* amount every race
  (including itself) spent in that field this turn (the total spent in that field divided by the number of races, then halved) and
  adds that to the race's resource pool for the field. Only if any such bonus was actually granted
  does it then run the same research pass once more, so the newly-added
  Super Stealth bonus resources immediately re-enter the same per-field buy loop and can themselves
  purchase levels that same turn. So the "second pass" is specifically Super Stealth's passive
  bonus re-triggering the ordinary buy loop, not a second, unconditional sweep applied to every
  race's leftover resources regardless of trait — `turn-generation-engine.md` §7 now states
  that narrower mechanism.
- ~~**115% vs. 125% for Generalized Research.**~~ **Resolved** — see
  section 4. The original game's own help text confirms 125% and jokes
  about the odd total itself; "115%" was the GameFAQs guide author's
  own misremembering, not a real discrepancy.
- ~~**Precise default values for the resource-generation constants.**~~
  **RESOLVED from the binary (this pass), by raw NE-segment-table
  parsing of `stars.exe`.** No Ghidra database access was needed. The
  wizard-neutral defaults are **A = 1,000, B = 10, C = 10, D = 10,
  G = 5**, with mine output (F) = 10 and mines operable per 10,000
  colonists (H) = 10. The ranges are A 700–2,500, B 5–15, C 5–25,
  D 5–25, G 2–15, F 5–25 and H 5–25. How they were found:
  - The shared clamp routine reads the two bound tables relative to its
    own code segment, not the shared data segment, which is all zeros
    at the rendered offsets. So the tables live in segment 29 at
    segment offsets `0x2f8` (max) and `0x308` (min).
  - They are contiguous with the point-cost tables `race-traits.md`
    §1a already recovered from that same segment. The per-LRT and
    per-PRT tables were re-read and reproduced exactly as a
    calibration check.
  - The defaults are the first built-in preset record (Humanoid). Every
    new-race entry point copies it into the wizard draft. They are also
    confirmed by a separate code path that resets the six factory/mine
    values to exactly 10/10/10/10/5/10 when Alternate Reality is
    selected.

  The "1,000/10/10/10" baseline this document already used for worked
  examples is therefore the real default, not just an illustration.
  Some of the inconsistent figures in other sources are explained by
  the other presets. For example, "9 resources to build a factory"
  matches Rabbitoid's stored factory cost of 9. No built-in preset
  stores a factory output of 15 (the highest is Silicanoid's 12), so
  the "15" figure is presumably a custom-race example. The full preset
  table is in `race-designer-ui-and-availability.md`. The original note is kept
  below for provenance. Sources give a valid *range* for each race-design economic setting
  (e.g., colonists-per-resource 700–2500) and cite various example
  "defaults" (1,000 colonists/resource; 10 or 15 resources per 10
  factories; 9 or 10 resources to build a factory) that are not fully
  consistent with one another across sources — likely because different
  authors are describing different predefined races (e.g. the
  "Humanoid" starting race) rather than a single wizard-neutral
  default. This document treats 1,000/10/10/10 as a reasonable
  illustrative baseline for worked examples, not a verified universal
  default. **Attempted at the code level this pass, not completable —
  same underlying blocker as `production-queue.md`'s identical open
  item, resolved once here.** The Custom Race Wizard's economic-slider
  page (`RACEWIZARDDLG3`, segment 29) validates each of the seven A/B/C/D/G/F/H
  sliders through a shared mechanism: a per-row lookup table
  (in the page's paint routine, `FUN_10e0_1d9e`) resolves each slider
  row to a generic per-race field index, and a shared clamp routine
  (`FUN_10e0_223e`) bounds any candidate value against two further
  tables addressed directly by that field index — the same generic
  `race_base + field_index + 0x3e` field-accessor pattern already used
  throughout this project for PRT/LRT bits. This confirms the *range*
  lookup is one uniform, field-index-keyed mechanism, not bespoke
  per-slider logic — but the literal numeric bytes stored in those
  tables (min, max, and any separate default-value table) are binary
  data-segment content that the exported decompiled source does not
  carry as text, and this pass could not read them directly (the
  Ghidra project's own database files returned a permission error, and
  scripting a headless byte-dump was judged out of scope this pass).
  So the mechanism is now identified precisely; the actual default
  numbers remain unverified, as before.
- ~~**Full hull/component prerequisite table.**~~ **RESOLVED (2026-10-01)
  from the executable's own data.** An earlier note here called this a
  hard blocker, because the outside material used in a 2026-09-05
  cross-check (a fan-made per-level unlock document and a component list)
  is no longer in the project. That was the wrong framing. Every component
  and hull record built into `stars.exe` stores its own six tech-level
  requirements, so the complete table comes from the game itself and
  no outside material is needed.

  *Where the data lives.* All 239 records sit in one data region at the
  start of NE segment 2. The segment starts at file offset `0x3240`, and
  the region runs from segment offset `0x000f` to `0x5117`, just before
  the segment's accessor code. The 16 category tables fill most of it,
  and a few other small tables sit in the gaps between them. Every record keeps its six required levels at
  record offsets +2 to +7, one byte per field, in this document's field
  order: Energy, Weapons, Propulsion, Construction, Electronics,
  Biotechnology. Hull and starbase-hull records use the same layout. The
  per-category base, stride and count are in `ship-design-and-components.md`
  §4/§15 and in the header of `extracted-game-data/component-stats.tsv`,
  which holds the full 239-row dump. The category/subtype resolver
  `FUN_1008_5194` (`stars.exe.export.c:3251`) points at the record for
  any of the 16 categories. Hulls and starbase hulls are also reachable
  by a single 0-36 index through `FUN_1008_5118` and `FUN_1008_0000`
  (`:3185`, `:3167`). The resolver's last step, `FUN_1008_5916`
  (`:3660`), does the tech comparison. The raw instructions store the
  resolver's own code segment as the record pointer's segment, so the
  table really is segment-2 data.

  *Verified this pass.* The six requirement bytes of all 239 records were
  re-read from the raw executable and compared with the TSV. There were
  no mismatches. Spot checks (file offset, then bytes +0 to +7, where +0
  to +1 is the record's own index word):

  | Record | File offset | Bytes +0..+7 | Requirement |
  |---|---|---|---|
  | Smart Bomb | `0x641c` | `0b 00 00 05 00 00 00 07` | Weapons 5, Biotechnology 7 |
  | Neutron Bomb | `0x6456` | `0c 00 00 0a 00 00 00 0a` | Weapons 10, Biotechnology 10 |
  | Enriched Neutron Bomb | `0x6490` | `0d 00 00 0f 00 00 00 0c` | Weapons 15, Biotechnology 12 |
  | Peerless Bomb | `0x64ca` | `0e 00 00 16 00 00 00 0f` | Weapons 22, Biotechnology 15 |
  | Annihilator Bomb | `0x6504` | `0f 00 00 1a 00 00 00 11` | Weapons 26, Biotechnology 17 |
  | Energy Dampener | `0x75b2` | `0f 00 0e 00 08 00 00 00` | Energy 14, Propulsion 8 |
  | LBU-17 Bomb | `0x62fa` | `06 00 00 05 00 00 08 00` | Weapons 5, Electronics 8 |
  | Ultra Driver 12 | `0x355f` | `0f 00 14 00 00 00 00 00` | Energy 20 |
  | Ultra Driver 13 | `0x3597` | `10 00 18 00 00 00 00 00` | Energy 24 |
  | Orbital Adjuster | `0x6e1a` | `08 00 00 00 00 00 00 06` | Biotechnology 6 |
  | Space Dock (starbase) | `0x38aa` | `21 00 00 00 00 04 00 00` | Construction 4 |
  | Death Star (starbase) | `0x3a57` | `24 00 00 00 00 11 00 00` | Construction 17 |
  | Mega Poly Shell | `0x7dd6` | `0a 00 0e 00 00 0e 0e 06` | Energy 14, Construction 14, Electronics 14, Biotechnology 6 |
  | Mini Morph (hull) | `0x584a` | `1e 00 00 00 00 08 00 00` | Construction 8 |
  | Nubian (hull) | `0x57bb` | `1d 00 00 00 00 1a 00 00` | Construction 26 |
  | Anti-Matter Pulverizer | `0x5ecc` | `18 00 00 1a 00 00 00 00` | Weapons 26 |
  | Genesis Device | `0x6c6a` | `0f 00 14 0a 0a 14 0a 14` | Energy 20, Weapons 10, Propulsion 10, Construction 20, Electronics 10, Biotechnology 20 |

  The field order is also confirmed from the race side. Race creation
  (`:50852`-`50888`) gives Packet Physics level 4 in the first of the
  race's six level bytes and Claim Adjuster level 6 in the sixth. Those
  match those PRTs' documented starting Energy and Biotechnology. The
  data agrees too: every mass driver needs only Energy, and every ship or
  starbase hull needs only Construction.

  *How the game checks the bytes.*
  - **Plain per-field minimum.** An item's requirements are met only when
    the race's current level in each of the six fields is at least the
    item's stored level. The race's levels are the six bytes at offset
    +0x1a of its 192-byte player record. A requirement of 0 is always
    met. No special values are used: no stored requirement is above 26,
    the research cap, so every item can be reached by research. No item
    uses a "never" or "starting tech only" marker.
  - **Graded result.** When the requirements are met, the check reports
    "available". When exactly one field is short and it is the field
    currently selected for research, it reports "one level away" if the
    gap is one level, and otherwise the gap plus one. Every other case
    gets one fixed "far away" value. That covers two or more short
    fields, and a single short field that is not the one being
    researched. This sharpens `ship-design-and-components.md` §11's
    three-way summary: the near-miss grades only apply to the field
    under research. The item detail card lists only an item's nonzero
    fields, and it draws the ones the race has not reached in a
    different colour (`:90020`-`90036`).
  - **Trait and gift gates come first and never replace the tech
    check.** Before comparing levels, the resolver rejects an
    out-of-range subtype. It also marks an item unavailable for the race
    when a PRT or lesser-trait rule bars it
    (`ship-design-and-components.md` §14), or when the item is one of
    the 12 gift-only parts the race has not been given (§14a). An item
    that passes those gates still has to pass the tech comparison: a
    gifted Mega Poly Shell, Jump Gate or Genesis Device still needs its
    listed levels. For most categories, the trait and gift gates are
    skipped when no current player is selected.
  - **No race trait changes a requirement.** Traits only change the
    race's own levels. Race creation (`:50846`-`50915`) sets the
    starting levels: the per-PRT starting tech, the race-wizard option
    that raises every "Costs 75% extra" field to 3 (4 for Jack of All
    Trades), and two lesser traits that each add a Propulsion level
    (Cheap Engines at `:50906`; Improved Fuel Efficiency at `:50911`,
    skipped in a game with option bit `0x08`, the tutorial-game marker,
    matching `race-traits.md` §3's bit list). So these
    traits decide what is available on turn 1, not what each item needs.
    Improved Starbases, Advanced Remote Mining, Only Basic Remote Mining,
    No Ram Scoop Engines, No Advanced Scanners, Total Terraforming and
    the PRT exclusives are yes/no availability gates, not changes to the
    levels. Bleeding Edge Technology reads the same six bytes only in the
    component-cost routine (`:36956`-`36996`), where the race's smallest
    margin over any nonzero requirement sets the miniaturization
    discount. That discount is 4% per level up to 75%, or 5% per level
    up to 80% with Bleeding Edge Technology, and it does not apply to
    terraforming or to planetary scanners and defenses. It does apply to
    ship hulls and starbase chassis, so a design's hull cost falls with
    tech like its parts, and to the Genesis Device (exact scope and
    rounding: `race-traits.md` §7, step 2). It never moves
    the point at which an item becomes available. Slow Tech Advance and
    Generalized Research change only research cost and how the budget is
    split.

  *The six bombs answer.* Smart Bomb, Neutron Bomb, Enriched Neutron
  Bomb, Peerless Bomb and Annihilator Bomb need **Weapons plus
  Biotechnology** (5/7, 10/10, 15/12, 22/15, 26/17). Their Electronics
  byte is 0. Energy Dampener (Electrical subtype 14) needs **Energy 14
  plus Propulsion 8**, and its Electronics byte is also 0. So the
  2026-09-05 pass was right that "Electronics" was wrong for all six.
  For the five bombs it was right that the second field is
  Biotechnology. For the Energy Dampener its replacement, "Energy", was
  only half right: Energy is that item's main field, and its second
  field is Propulsion. The three LBU bombs really do need Weapons plus
  Electronics (5/8, 10/10, 15/12). Note that LBU-17 needs Electronics 8,
  not 6. Hush-a-Boom needs Weapons, Electronics and Biotechnology 12
  each. Retro Bomb needs Weapons 10 and Biotechnology 12.

  *Highest requirement per field, by category.* A 0 means no item in
  that category needs the field.

  | Category | Records | Energy | Weapons | Propulsion | Construction | Electronics | Biotech |
  |---|---|---|---|---|---|---|---|
  | `0x0001` Engines | 16 | 7 | 0 | 23 | 5 | 9 | 0 |
  | `0x0002` Ship scanners | 16 | 10 | 0 | 5 | 0 | 24 | 10 |
  | `0x0004` Shields | 10 | 22 | 0 | 9 | 4 | 9 | 0 |
  | `0x0008` Armor | 12 | 14 | 0 | 0 | 24 | 14 | 7 |
  | `0x0010` Beam weapons | 24 | 21 | 26 | 0 | 0 | 16 | 12 |
  | `0x0020` Torpedoes / missiles | 12 | 0 | 26 | 12 | 0 | 0 | 21 |
  | `0x0040` Bombs | 15 | 0 | 26 | 0 | 0 | 12 | 17 |
  | `0x0080` Mining robots | 8 | 5 | 0 | 0 | 15 | 8 | 6 |
  | `0x0100` Mine layers | 10 | 14 | 0 | 5 | 0 | 0 | 12 |
  | `0x0200` Stargates / mass drivers | 16 | 24 | 0 | 19 | 24 | 0 | 0 |
  | `0x0400` Starbase hulls | 5 | 0 | 0 | 0 | 17 | 0 | 0 |
  | `0x0800` Electrical | 17 | 16 | 12 | 11 | 0 | 22 | 7 |
  | `0x1000` Mechanical | 11 | 16 | 6 | 20 | 20 | 16 | 0 |
  | `0x2000` Terraforming | 20 | 16 | 16 | 16 | 0 | 0 | 25 |
  | `0x4000` Ship hulls | 32 | 0 | 0 | 0 | 26 | 0 | 0 |
  | `0x8000` Planetary scanners / defenses | 15 | 23 | 10 | 10 | 20 | 23 | 20 |

  *Items that need three or more fields* (22 of 239):

  | Item | Category | Requirement |
  |---|---|---|
  | Enigma Pulsar | Engine | Energy 7, Propulsion 13, Construction 5, Electronics 9 |
  | Pick Pocket Scanner | Ship scanner | Energy 4, Electronics 4, Biotech 4 |
  | Ferret Scanner | Ship scanner | Energy 3, Electronics 7, Biotech 2 |
  | Dolphin Scanner | Ship scanner | Energy 5, Electronics 10, Biotech 4 |
  | Elephant Scanner | Ship scanner | Energy 6, Electronics 16, Biotech 7 |
  | Robber Baron Scanner | Ship scanner | Energy 10, Electronics 15, Biotech 10 |
  | Langston Shell | Shield | Energy 12, Propulsion 9, Electronics 9 |
  | Mega Poly Shell | Armor | Energy 14, Construction 14, Electronics 14, Biotech 6 |
  | Multi Contained Munition | Beam | Energy 21, Weapons 21, Electronics 16, Biotech 12 |
  | Anti Matter Torpedo | Torpedo | Weapons 11, Propulsion 12, Biotech 21 |
  | Hush-a-Boom | Bomb | Weapons 12, Electronics 12, Biotech 12 |
  | Alien Miner | Mining robot | Energy 5, Construction 10, Electronics 5, Biotech 5 |
  | Multi Function Pod | Electrical | Energy 11, Propulsion 11, Electronics 11 |
  | Multi Cargo Pod | Mechanical | Energy 5, Construction 11, Electronics 5 |
  | Super Fuel Tank | Mechanical | Energy 6, Propulsion 4, Construction 14 |
  | Jump Gate | Mechanical | Energy 16, Propulsion 20, Construction 20, Electronics 16 |
  | Beam Deflector | Mechanical | Energy 6, Weapons 6, Construction 6, Electronics 6 |
  | Snooper 320X / 400X / 500X / 620X | Planetary scanner | Energy 3/4/5/7, Electronics 10/13/16/23, Biotech 3/6/7/9 |
  | Genesis Device | Planetary | all six fields (20, 10, 10, 20, 10, 20) |

  *Items that need exactly two fields* (89 items), by pattern. The
  full levels are in the TSV.

  | Category | Two-field items |
  |---|---|
  | Engines | the six ram scoops (Radiating Hydro-Ram Scoop to Galaxy Scoop): Energy + Propulsion |
  | Ship scanners | DNA, RNA: Propulsion + Biotech; Chameleon, Gazelle, Cheetah, Eagle Eye, Peerless: Energy + Electronics |
  | Shields | Croby Sharmor: Energy + Construction; Shadow Shield: Energy + Electronics |
  | Armor | Fielded Kelarium: Energy + Construction; Depleted Neutronium: Construction + Electronics |
  | Beam weapons | Pulsed, Phased and Syncro Sapper: Energy + Weapons |
  | Torpedoes / missiles | every torpedo and missile except Alpha Torpedo (none) and Anti Matter Torpedo (three fields): Weapons + Propulsion |
  | Bombs | LBU-17/32/74: Weapons + Electronics; Retro Bomb and the five smart bombs: Weapons + Biotech |
  | Mining robots | Robo-Mini-Miner to Robo-Ultra-Miner: Construction + Electronics |
  | Mine layers | Mine Dispenser 50/80/130 and Heavy Dispenser 50/110/200: Energy + Biotech; the three Speed Traps: Propulsion + Biotech |
  | Stargates / mass drivers | all seven stargates: Propulsion + Construction (the mass drivers need Energy only) |
  | Electrical | Stealth, Super-Stealth and Ultra-Stealth Cloak, Battle Super Computer, Battle Nexus, the four Jammers, both capacitors, Tachyon Detector: Energy + Electronics; Energy Dampener: Energy + Propulsion; Anti-matter Generator: Weapons + Biotech |
  | Mechanical | Super Cargo Pod: Energy + Construction; Maneuvering Jet, Overthruster: Energy + Propulsion |
  | Terraforming | Gravity: Propulsion + Biotech; Temperature: Energy + Biotech; Radiation: Weapons + Biotech (Total Terraform needs Biotech only) |
  | Ship and starbase hulls | none: every hull needs Construction only |

  *Unusual items.*
  - **No requirement at all (25 items):** Settler's Delight, Quick Jump
    5, Bat Scanner, Mole-skin Shield, Tritanium, Laser, Alpha Torpedo,
    Robo-Midget Miner, Mine Dispenser 40, Transport Cloaking, Battle
    Computer, Colonization Module, Orbital Construction Module, Fuel
    Tank, Total Terraform ±3, Viewer 50, SDI, and the Small Freighter,
    Scout, Mini-Colony Ship, Colony Ship, Midget Miner and Mini Mine
    Layer hulls, plus the Orbital Fort and Space Station starbases.
    Several of these are still trait-gated, for example Settler's
    Delight and Mini-Colony Ship (Hyper Expansion), Mine Dispenser 40
    and Mini Mine Layer (Space Demolition), Robo-Midget Miner and Midget
    Miner (Advanced Remote Mining), and Total Terraform ±3 (Total
    Terraforming).
  - **Requirement at the level-26 cap:** Anti-Matter Pulverizer
    (Weapons 26), Omega Torpedo (Weapons 26), Annihilator Bomb (Weapons
    26) and the Nubian hull (Construction 26). The only level-25
    requirement is Total Terraform ±30 (Biotech 25). Nothing needs a
    level the cap cannot give.
  - **Genesis Device** is the only item that needs all six fields.
  - **Gift-only parts** (the 12 items in `ship-design-and-components.md`
    §14a) carry ordinary tech requirements and need both the gift and
    the levels.

  *About the old cross-check.* The outside material from that pass is
  superseded and no longer needed. The executable holds 239 records:
  202 components in the 14 component categories, plus one 37-entry hull
  table split into 32 ship hulls (`0x4000`) and 5 starbase hulls
  (`0x0400`). Names that the old pass could not match cannot be audited
  now, and they are moot, since the per-item answer is read from the
  game's own records rather than matched by name. The ~25 "Biotechnology"
  discrepancies the old script reported came from its own parsing, as
  that pass suspected. The exe shows Ultra Driver 12 needing Energy 20
  only.
- **Cross-reference: the runtime component category/subtype/race-trait gate.** A separate pass
  (`ship-design-and-components.md`) traced the actual code-level function consulted when the
  ship/starbase design editor decides whether a specific component is legal for the current race —
  this is the runtime code that applies the per-record prerequisite bytes described in the entry
  above, which are read directly from the executable. It confirms components are grouped into exactly 16 fixed categories (one per bit of a
  16-bit word), each with its own subtype-count ceiling, and that PRT-exclusive/LRT-exclusive
  components are gated by two mechanisms: hard-coded per-subtype PRT-id or boolean-flag checks, and
  a generic per-race "special ability" bitmask (later shown to be the 16-bit one-time-gift word of
  that document's §14a, not 32 bits). That document also independently confirms
  this project's PRT-index convention (Hyper Expansion=0 ... Alternate Reality=8, JOAT=9) against
  two exact name/count matches (Engine-family and Bomb-family exclusive components) — potentially
  useful for the "second PRT code" noted below, which has since been identified as PRT index 3,
  Claim Adjuster (section 7), consistent with this convention.
- ~~**No "percent of resources devoted to research" control found.**~~ **Resolved** — see section 4's
  new "Gap resolved by a later pass" note. The control exists in the Research dialog; it was missed
  earlier because it is a custom-painted increment/decrement readout rather than a standard
  slider/scrollbar. That same pass also found a previously-undocumented live "turns until next level"
  forecast in the same dialog, and a candidate for the tech-level-distance grading mechanism referenced
  from `client-ui-dialog-catalog.md`'s "Slot/equipment editor" entry. **A later pass individually read
  all 7 of the dialog's numbered helper functions** (see section 4's "Individual accounting" note) —
  the tech-distance-grading candidate's own mechanism is now confirmed by direct inspection, not just
  inferred, and no additional undocumented functions were found among the 7. ~~Still open: which
  specific race trait halves the forecast's effective per-turn rate~~ — **resolved**: section 4 now
  identifies this as bit 4 of the per-race trait word, i.e. **Generalized Research**, confirmed both by
  mechanical fit and by a direct code-level corroboration (the same bit test also gates painting the
  real recovered "Generalized Research" on-screen label). ~~Whether the distance-grading candidate
  function's output is actually what feeds the slot/equipment editor's on-screen grading text~~ — also
  **resolved** (section 4: the grading function's return value is consumed directly by the shared
  detail-card renderer's text-drawing code). ~~Still open: the exact real-world identity of the
  newly-found category/context lockout-bit gate's context-code parameter.~~ **Resolved earlier in
  this same pass** (section 4): the "context code" is simply the item's own subtype-index byte, not
  a separate UI-context enum — confirmed both by `FUN_10d8_4b8e`'s three call sites inside the
  segment-2 category/subtype resolver `FUN_1008_5194` (each passing that resolver's own packed
  category+subtype word straight through) and, this pass, by a fourth, independent call site inside
  `BROWSERDLG` (`stars.exe.export.c:89738`) that calls the gate directly against the browser
  dialog's own highlighted-category/highlighted-subtype cursor pair with no resolver in between —
  the same packed convention, confirmed from a second, unrelated caller.
- *(The PRT-conditional field exclusion in this entry is withdrawn: it is the production catalog's auto-build exclusion; see §7's corrected bullet. Research has no PRT-based field exclusion.)* **New from this pass: PRT-conditional exclusion from the Research dialog's field list, and two
  previously-undocumented starbase cost rules.** Inspection of the exported client (see section 7's
  new "Confirmed by inspection" note) found that building the Research dialog's 7-entry field list
  (6 fields + "lowest field") explicitly skips certain fields for two specific PRT codes (one
  confirmed to be Alternate Reality; ~~the other not yet identified by name~~ **now identified: PRT
  index 3, i.e. Claim Adjuster** — see section 7 — **and a partial (Biotechnology-only) gameplay
  rationale for that exclusion recovered this pass from the race's own dynamic-string description**),
  and that upgrading an
  existing starbase design in place costs exactly half of a fresh build, with a further ~20% discount
  for a race-trait condition likely tied to a named racial trait ("Improved Starbases" or similar).
  None of this was previously documented from community sources; the PRT-code cross-reference against
  this project's own PRT enum has now been done (section 7), ~~though the ~20% starbase-discount
  trait condition's exact name is still unconfirmed~~ — **now cross-referenced this pass: bit 3 of
  the per-race trait word, matching Improved Starbases' documented "-20%, doesn't stack with
  Alternate Reality" effect by exact mechanical fit (see section 7), though without the direct
  string-label proof available for Generalized Research's bit.**
- ~~**"Slow Tech Advance" and other game-parameter interactions.**~~
  **RESOLVED by inspection of the exported client, this pass.** The
  source formula states this parameter doubles the total cost, but no
  source consulted described how it interacts with cost factor or
  the total tech level count beyond a flat doubling of the final result — assumed
  here to apply after the cost factor multiplication, consistent with
  the formula's own ordering, but not independently verified. The
  section 3/4 cost-lookup formula (`FUN_10d8_1580`, segment 28)
  confirms this assumption exactly: it sums the total tech level count, adds the
  level-indexed base cost, applies whichever of the three cost factor
  branches the field's cost-class selects, and only as its final step
  — after that multiplication, unconditionally, regardless of which
  cost factor branch ran — checks a single global game-options bit
  (the Slow Tech Advance option) and doubles the already-computed result if set.
  This is a literal, unhedged confirmation of "doubled last, after
  cost factor," not an inference from formula ordering.

## Sources

- [stars.hlp — the original Stars! Player's Guide, converted to HTML by the Stars!AutoHost wiki community](https://wiki.starsautohost.org/wiki/Downloads) (file `stars.hlp.html.rar`, under References; used here only to verify mechanics against this project's own clean-room documentation, not copied into it)
- `TECHITEM.DOC` (1997, from `techitem.zip` on the same Stars!AutoHost wiki downloads page, References section) — a per-tech-level table of every hull/component/weapon/armor/shield/scanner unlock across all six fields. Used only in a 2026-09-05 cross-check pass; not copied into this project. **Superseded (2026-10-01):** the prerequisite data is now read directly from the executable's own component and hull records (see the resolved "Full hull/component prerequisite table" entry above), so neither file is needed.
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
