# Ship and Starbase Design: Slots, Components, and Race-Trait Gating

Behavior specification for how a *Stars!* ship or starbase design stores its installed
components, how a component's category/subtype is checked against a hull slot and against the
owning race's traits, and how a finished design's aggregate stats (armor/mass/fuel-type
totals) are computed — reconstructed from the exported client, for a clean-room
reimplementation.

## Scope note and correction to this investigation's own starting premise

This document was commissioned to deep-dive two code segments (Ghidra address prefixes
`FUN_1048_*`, 66 functions, and `FUN_10f0_*`, 40 functions) on the assumption that both were
primarily "ship-design hull/component slot-type compatibility validation." **Confirmed by
inspection of the exported client:** that assumption does not hold for either segment's bulk
content.

- `FUN_1048_*` is overwhelmingly a per-race **habitability/growth-rate/terraforming math
  cluster** (already the subject of `population-growth.md` and `production-queue.md`) plus a
  **turn-order delta-record encode/decode protocol** (a candidate future
  `save-turn-file-format.md` topic, not covered here) and a planet/fleet summary-bar UI control.
- `FUN_10f0_*` is overwhelmingly the **battle-resolution engine**, **AI production planning**,
  and **end-of-battle orbital bombardment of foreign planets** (relevant to `combat-resolution.md`, not
  this document).

However, both segments do genuinely own a real slice of the ship/starbase design subsystem: the
**on-disk/in-memory design record layout**, the **component-to-slot write path** (including a
cross-segment call from segment `1048`'s turn-order decoder into segment `10f0`), and a
**per-design aggregate-stats calculator**. This document covers that slice, and — because the
actual category-vs-slot compatibility dispatch and the race-trait exclusivity gate turned out to
live in one shared helper function outside both assigned segments — also covers that helper,
since without it the story is incomplete. Its segment is noted explicitly wherever cited.

## Overview

A design (ship hull or starbase) is a fixed-size **147-byte (0x93)** record. Each race has
**16 hull-design slots plus 10 starbase-design slots (26 total)**, addressed by a single index
0–25 that splits exactly at index 16: indices 0–15 are hull designs, 16–25 are starbase designs,
each in its own separately-allocated per-race array. A design record holds a **hull-dependent
number of installed component slots — 2 to 16, matching whichever hull or starbase chassis the
design uses** (a per-design "how many are filled" counter plus a small fixed array, both stored at
the same byte offsets, `+0x7a` and `+0x3a`, that the assigned hull's own template uses for its
slot count and slot array — see §2 and §15e; **corrects this document's own earlier "up to 9
slots" characterization**, which conflated three unrelated 0–6/0–8-range wire-protocol fields with
the actual slot-count/slot-index limit), where each installed entry packs a component's
**category** (one of 16 fixed values, functionally a single set bit of a 16-bit word), a **subtype
index** within that category, and a **quantity** (itself capped per slot at 0–8, matching the
largest observed per-slot capacities in the real hull data, e.g. Meta Morph's and Battleship's
capacity-8 slots — see §15e).
Component category/subtype/race-trait legality is resolved through one shared, heavily-reused
function that both segments in scope call constantly; a separate generic race-trait-bitmask
check backstops it for "this component belongs to a specific Lesser Racial Trait" cases. A
distinct aggregate-stats function walks a finished design's installed components and reduces
them to a handful of packed multipliers (armor/defense-percentage-like, mass-scaling,
fuel-efficiency-scaling) plus paired min/max trackers, feeding both the ship-design UI and the
battle engine's combatant-roster builder.

## Mechanics

### 1. Design slot addressing (16 hull + 10 starbase, 147-byte records)

Confirmed by inspection of the exported client: the turn-order delta-record decoder's design
create/delete case (opcode `0x1b`, in segment `1048`) computes a design index from a packed input
word (bits 8-12, a 5-bit index, 0–31) and a race index (bits 4-7, 0–15). If the design index is
**below 16**, it addresses entry *index* of the race's **hull-design array** (147-byte stride). If
it is **16 or above**, it is rejected outright once it exceeds **25**; otherwise it addresses entry
*index* − 16 of a **separate starbase-design array** (renormalizing the starbase index back to
0–9 within its own array). Each race therefore has exactly 16 hull-design slots and 10
starbase-design slots, and every design record — regardless of hull or starbase — is exactly
147 bytes. A per-race in-use counter is adjusted by ±1 on create/delete, tracked in a separate
small per-race table.

### 2. Per-design installed-component list (hull-dependent, 2–16 slots)

**Correction to this section's own original "up to 9 slots" heading and opening claim, made while
reconciling this document against §15e's direct extraction of the real per-hull data (which shows
slot counts of 2–16 across the 37 hulls and chassis, including 13 for the real, published Nubian
hull — see https://wiki.starsautohost.org/wiki/Nubian).** Re-reading opcode `0x1e`'s decoder
(`stars.exe.export.c:30458`-`30492`) line by line shows no field anywhere in it that is "the number
of occupied component slots, 0–9." That description conflated three separate, narrower wire-payload
fields — none of which is a slot count — with the one field that genuinely *is* checked against a
hard limit, which is the **slot index**, capped at 15 (16 slots), already correctly stated in this
section's last bullet below and consistent with the largest real hulls. The design record's own
"how many slots are filled" byte lives at design offset `+0x7a` (122) — the identical numeric
offset as the hull template's own slot-count field (`+122`, §15e), which this document's own §7
follow-up pass independently cross-checked (`stars.exe.export.c:19607`-`19609`, `100160`: the
aggregate-stats walk `FUN_10f0_27da` loops over slot indices while they stay below the design's
own count byte at `+0x7a`, i.e. until it exhausts however many slots that byte declares — not a
fixed 9). The
design's compact installed-component array sits at the matching offset `+0x3a` (58), the same
offset the hull template's own 4-byte-per-slot array starts at (§15e) — a 64-byte span (`+0x3a`
through `+0x79`) that holds exactly 16 four-byte entries, matching the slot-index cap of 15 exactly.
**A Nubian-hulled design can therefore genuinely have all 13 of its slots filled during normal
gameplay; nothing in the turn-file format, the design record, or the removal/shift logic in §3
caps usable slots at 9 or below.**

With that corrected, here is what opcode `0x1e`'s decoder (segment `1048`) actually validates when
writing one occupied entry:

- A **mount/category-group nibble** (bits 0–3 of one input byte) must be **6 or less** (7 legal
  values) or the write is rejected. This is a coarse UI mount-group selector distinct from the
  16-bit category bitmask of §4 (7 values cannot address 16 category bits); it is not a slot count.
- A second nibble in a following byte must be **8 or less** (9 legal values) or the write is
  rejected. Also not a slot count — most plausibly a subtype-detail or UI list-position field
  local to the mount group above; this pass did not trace its consumer further.
- A third value (the high nibble of that same byte) must be **0x80 or less**, i.e. one of the 9
  values `0x00, 0x10, 0x20, ... 0x80` — a **quantity** field expressed in steps of 16, capping the
  effective per-slot quantity at 0–8. This matches, rather than contradicts, §15e's real data: the
  largest per-slot capacities actually observed in the extracted hull records (e.g. Meta Morph's
  capacity-8 general-purpose slots, the Battleship's capacity-8 shield slots) top out at exactly 8,
  so this field's 0–8 range is a genuine, correctly-sized cap on **how many copies of one
  component occupy a single slot**, not on how many slots a design has.
- Slot indices are filled **sequentially, not arbitrarily**: the write only proceeds if the
  supplied slot index equals a per-race "next free slot" counter (tracked in a small per-race
  table, `DAT_1128_4850`, one byte per race), which is then incremented; the index itself must not
  exceed 15 (`stars.exe.export.c:30474`-`30476`). **This 0–15 check is the real,
  and only, hard structural cap on slot count/index in this opcode — it allows exactly the 16
  slots the largest hulls and starbase chassis declare (§15e), with no separate 9-slot ceiling
  anywhere in the wire format.**
- A separate bit in the input (`0x40`) distinguishes **assigning** a component to a slot (which
  calls into a per-race/per-slot cache writer in a different segment, `FUN_1070_2808` — **its
  actual behavior is decoded in §12 below, correcting this document's original characterization of
  it as a "slot template resolver"**) from **removing** one, which is handed off entirely to a
  function in the *other* segment in scope for this document — `FUN_10f0_0ff0` (segment `10f0`,
  i.e. Ghidra's "segment 31"). This is the one concrete, direct code-level link between the two
  segments this document was assigned to cover, confirming they are two parts of the same
  subsystem even though neither is individually "the" ship-design validator.

### 3. Removing a slot from an already-in-use design (fleet-consistency check)

Confirmed by inspection of the exported client (`FUN_10f0_0ff0`, segment `10f0`): before a slot
is actually removed from a design, this function walks **every existing fleet in the game**,
filters to fleets using this exact (race, design) pair, and compares the slot index being removed
against that fleet's own cached "components in this slot" count. If any matching fleet already
has that slot filled (i.e., ships already built with the design as it stands would be affected by
shrinking it), the function raises a confirmation prompt (a yes/no dialog, looked up by a string
ID and shown via a message-box helper) before proceeding; answering "no" aborts the whole
edit. If no existing fleet is affected, the removal proceeds silently. After removal, the
remaining installed-component entries are shifted down to close the gap: for each slot position
from the removed index up to the per-race "next free slot" counter, the next entry's full 36-byte,
36 = 9-dword record is copied down one slot (`stars.exe.export.c:98671`-`98693`, an
inner loop copying one entry's 9 dwords). **Correction: the "9" here is the
per-entry dword count (9 × 4 = 36 bytes = one slot's record), not the array's entry count** — this
document's own original phrasing ("a 9-entry, 36-byte-stride array") conflated the two. The array
itself is the same **16-entry**, 36-byte-stride per-race cache §12 documents (`&DAT_1128_5958`/
`&DAT_1128_595a`, the identical base symbols this shift code addresses), and the per-race "next
free slot" counter (§2) is decremented.

This is a genuinely new, previously-undocumented mechanic: **editing a saved design's component
list checks for retroactive impact on already-built ships of that design**, rather than silently
allowing edits that would leave existing ships in an inconsistent state.

### 4. The component category/subtype resolver (shared dependency, not in segments 10/31)

The single function both segments in scope call to determine whether a given (category, subtype)
pair is a legal, race-available component is `FUN_1008_5194`. It lives in a different segment
(Ghidra prefix `FUN_1008_`, this project's segment 2) and is included here because it is the
actual mechanism referenced by the assignment's premise — segments 10 and 31 are its heaviest
callers, not its home.

**Confirmed by inspection of the exported client:** the "category" value is not an arbitrary
integer — it is always exactly one of **16 fixed values, one per bit position of a 16-bit word**
(`0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0020, 0x0040, 0x0080, 0x0100, 0x0200, 0x0400, 0x0800,
0x1000, 0x2000, 0x4000, 0x8000`). Each of the 16 categories has its own fixed **subtype-count
ceiling** and its own fixed-stride, fixed-base lookup table (all in one shared static data area).

**Subtype numbering convention (used throughout these specs).** A component "subtype" is the
0-based position of the record in its category table, i.e. the `idx` column of
`extracted-game-data/component-stats.tsv`; the resolver addresses the record as table base plus
subtype times stride (for category `0x8000`, base `0x3736`, stride 54, `stars.exe.export.c:3557`-`3561`).
The 1-based ordinal stored inside each record (idx + 1) is never used as a subtype. So category
`0x8000` subtype 13 is Neutron Shield and subtype 14 is Genesis Device, for every caller alike.

**All 16 categories are now identified by name and their raw table content recovered** — see §15
for the addressing fix that made this possible, and `extracted-game-data/component-stats.tsv`
for the complete extracted dataset. The identities below are no longer inferences: each was read
directly from a name field inside the corresponding record.

| Category (bit) | Identity (confirmed) | Subtype count | Record stride | Table base |
|---|---|---|---|---|
| `0x0001` | Engines | 16 | 78 bytes | `0x4c38` |
| `0x0002` | Ship scanners | 16 | 56 bytes | `0x4630` |
| `0x0004` | Shields | 10 | 54 bytes | `0x4414` |
| `0x0008` | Armor | 12 | 54 bytes | `0x49b0` |
| `0x0010` | Beam weapons | 24 | 60 bytes | `0x2728` |
| `0x0020` | Torpedoes / capital missiles | 12 | 60 bytes | `0x2cc8` |
| `0x0040` | Bombs | 15 | 58 bytes | `0x2f98` |
| `0x0080` | Mining robots | 8 | 54 bytes | `0x3a60` |
| `0x0100` | Mine layers | 10 | 54 bytes | `0x3c10` |
| `0x0200` | Stargates and mass drivers | 16 | 56 bytes | `0x000f` |
| `0x0400` | Starbase chassis | 5 | 143 bytes| `0x05db` |
| `0x0800` | Electrical (cloaks, battle computers, jammers, capacitors) | 17 | 54 bytes | `0x407e` |
| `0x1000` | Mechanical (colonisation, cargo, fuel, thrusters, gates) | 11 | 54 bytes | `0x3e2c` |
| `0x2000` | Terraforming | 20 | 54 bytes | `0x32fe` |
| `0x4000` | Ship hulls | 32 | 143 bytes| `0x1548` |
| `0x8000` | Planetary scanners and planetary defenses | 15 | 54 bytes | `0x3736` |

A "category" value outside this list, or a subtype index at or beyond its category's ceiling,
resolves to a plain "does not exist" result. This is the concrete structure underlying the
assignment's hypothesized "nibble-masked category checks" — the check is real, but it lives one
segment away from where it was expected, and the mask is 16 bits wide (one full word), not a
4-bit nibble.

**A slot's "allowed types" field is therefore almost certainly a bitwise-OR of a subset of these
16 category bits** (letting a hull slot accept, e.g., both a Weapon-family and a Shield-family
component), while an installed *component itself* always carries exactly one of the 16 values.

**Correction, confirmed by inspection of the exported client in a follow-up pass (see §12):** the
36-byte, per-race, 16-entry array reached via the `FUN_1070_2808` slot writer is **not** a
hull-defined "allowed category mask" template. It is a UI-side cache of the components a player
has already placed while a design is open for editing (populated only after every legality check
has already passed), not a legality record consulted before or during that check. §13 identifies the location of the genuine per-hull data — inside the hull-type definition
table itself — and §15e later recovered that table's per-slot layout (allowed-category mask and
capacity per slot) directly from the executable.

### 5. Per-subtype race-trait/PRT gating, embedded directly in the resolver

Beyond the category/subtype-count structure, `FUN_1008_5194` hard-codes specific
**PRT-exclusivity and boolean-trait exclusions per (category, subtype) pair**, returning a
distinct "not available to this race" result (as opposed to "does not exist"). Two fully-traced
examples, cross-checked against `race-traits.md`'s existing PRT/LRT tables and found to match
exactly:

- **Category `0x0001` (16 subtypes) reads as the Engine family.** Subtype 0 requires PRT id 0
  (Hyper Expansion) — matching race-traits.md's HE-exclusive "Settler's Delight" free Warp-6
  engine. Subtypes 10–15 (6 engines) are blocked outright if a specific boolean race-trait flag is
  set — matching the "No Ram Scoop Engines" LRT's documented loss of the ram-scoop engines that
  burn no fuel above Warp 4 (exactly the ram-scoop-family engine range). Subtypes 2 and 15 (2 engines)
  additionally require a *different* boolean race-trait flag — matching Improved Fuel Efficiency's
  documented grant of the Fuel Mizer and Galaxy Scoop engines (exactly 2 named exclusive engines); engine 15
  sitting in both ranges is consistent with Galaxy Scoop being IFE's ram-scoop-type engine.
  **(Trait bits pinned, lesser-trait re-check pass.)** Both "boolean race-trait flags" are bits of
  the race's lesser-trait word (bits 0-13 of the 32-bit field at race record `+0x4e`, tested through `FUN_10e0_226e`; bit
  numbering in "Lesser-trait bit numbering" below): the ram-scoop block (subtypes 10-15) is bit 7,
  No Ram Scoop Engines, and the Fuel Mizer / Galaxy Scoop requirement (subtypes 2 and 15) is bit 0,
  Improved Fuel Efficiency (`stars.exe.export.c:3344`-`3353`). A third test on the same bit 7 makes
  subtype 7, the Interspace-10, available **only** to No Ram Scoop Engines races (`:3354`-`3359`).
- ~~**Category `0x0080` (8 subtypes) reads as the Bomb family.**~~ **Corrected (lesser-trait re-check
  pass): category `0x0080` is the Mining-robot family, and the Bomb family is `0x0040` (§4, §15).**
  In `0x0080`, subtypes 0, 2, 3, 4 and 5 (Robo-Midget, Robo-Miner, Robo-Maxi, Robo-Super and
  Robo-Ultra Miner) are unavailable when lesser-trait bit 9, **Only Basic Remote Mining**, is set,
  and subtypes 0 and 5 (Robo-Midget and Robo-Ultra) additionally require lesser-trait bit 2,
  **Advanced Remote Mining** (`:3274`-`3283`). That leaves the Robo-Mini-Miner as the one robot an
  Only Basic Remote Mining race keeps, and gives Advanced Remote Mining exactly its two extra
  robots. Subtype 7, the Orbital Adjuster, requires PRT 3 (Claim Adjuster). The earlier reading,
  which tied this bit to Inner Strength's bomb exclusion, matched on a count of five and nothing
  else. It was wrong: Inner Strength is a primary trait and is never a bit of this word.
- **Category `0x0040` (15 subtypes) is the Bomb family.** Its exclusions are PRT comparisons, not
  trait bits (`:3299`-`3311`). Subtypes 10-14, the Smart, Neutron, Enriched Neutron, Peerless and
  Annihilator bombs, are unavailable when the PRT is 4 (**Inner Strength**). This is Inner
  Strength's documented smart-bomb exclusion. Subtype 9, the Retro Bomb, requires PRT 3 (Claim
  Adjuster). The ordinary and cluster bombs (subtypes 0-7) have no trait gate. Subtype 8 (Hush-a-Boom)
  is gated only by the one-time-gift mask of §14a. No lesser trait touches any bomb.

**Lesser-trait bit numbering (authoritative, lesser-trait re-check pass).** The race wizard's
lesser-trait page binds checkbox *i* (control `0x123 + i`) to bit *i* of the lesser-trait word, and
labels it with dynamic string 306 + *i* (the wizard's stage-5 dialog procedure, `stars.exe.export.c:93855`-`93871`
labels and initial states, `:93896`-`93899` toggles). The checkbox position, the bit index and the
selector passed to `FUN_10e0_226e` are therefore one and the same 0-based number, with no 1-based
offset anywhere: 0 Improved Fuel Efficiency, 1 Total Terraforming, 2 Advanced Remote Mining,
3 Improved Starbases, 4 Generalized Research, 5 Ultimate Recycling, 6 Mineral Alchemy, 7 No Ram
Scoop Engines, 8 Cheap Engines, 9 Only Basic Remote Mining, 10 No Advanced Scanners, 11 Low Starting
Population, 12 Bleeding Edge Technology, 13 Regenerating Shields. Bits 29 and 31 of the same 32-bit
field hold the two race-wizard checkboxes ("start at tech 3" and "factories cost 1 kT less
germanium"). Bit 30, when set at game creation, runs the random-race generator on that race
(`:50838`-`50841`). None of these is a lesser trait. The PRT is a
separate byte, setting 14 of the race-settings array at `+0x3e` (record `+0x4c`), read through
`FUN_10e0_222c` with selector `0xe`.

**Also confirmed:** the PRT-id values checked throughout this resolver (0, 2, 3, 4, 5, 6, 7, 8) are
internally consistent with `race-traits.md`'s existing PRT ordering (Hyper Expansion=0, War
Monger=2, Claim Adjuster=3, Inner Strength=4, Space Demolition=5, Packet Physics=6, Interstellar
Traveller=7, Alternate Reality=8), independently corroborated here by the two exact
name/count matches above and by this same PRT-index convention recurring throughout the segment
`1048` habitability/growth cluster (where PRT 8/Alternate Reality and PRT 9/JOAT already receive
distinct code paths, per `population-growth.md`'s and `race-traits.md`'s existing material).

Every other category follows the identical pattern (a handful of hard-coded subtype-vs-PRT-id or
subtype-vs-boolean-flag checks); the §14 table lists every category's gates, and §15 (with
`extracted-game-data/component-stats.tsv`) gives every subtype's real component name.

### 6. The generic race-trait "special ability" bitmask gate

Separately from the per-subtype checks embedded in §5, a second, generic gate
(`FUN_10d8_4b8e`, segment 28 — again outside segments 10/31, included because both segments'
component-handling code paths through it) maps roughly a dozen more specific (category, subtype)
pairs to a fixed small integer "ability bit," then tests that bit against a **32-bit per-race
bitmask** stored in the race record. If the race's bitmask does not have that bit set, the
component is unavailable. This reads as the mechanism for components whose availability is driven
by a *precomputed* per-race flag (most plausibly assembled once, at race-load time, from the
race's full PRT+LRT selection) rather than by a literal "is PRT == N" comparison hard-coded per
component — a natural way to implement Lesser-Racial-Trait-exclusive unlocks (e.g. Improved
Starbases' exclusive hull(s), Advanced Remote Mining's exclusive mining hulls/robots) without
touching the resolver's per-subtype logic for every LRT combination. *(Superseded: §14a shows this
mask, at record `+0x52`, is the one-time special-component gift record and is set by no race-design
choice. Every lesser-trait unlock and exclusion, including Improved Starbases' two starbases and
Advanced Remote Mining's hulls and robots, is a direct test of the lesser-trait word at `+0x4e`
inside the resolver itself. See §5 and the §14 table.)* Both this gate and the §5
per-subtype checks feed into the same single "is this component legal for this race" boolean
consumed by the slot editor.

**Cross-reference to `client-ui-dialog-catalog.md`'s Slot/equipment editor entry:** that entry's
existing note that "compatibility checking is graded, not a simple yes/no" (fully available / one
tech level away / needs multiple fields raised / unavailable for other reasons) is **not
explained by anything found in segments 10 or 31**. Both gates described here (§5, §6) are binary
(available/unavailable) with no partial state. The tech-level-distance grading lives outside these
segments and is now traced (`research-tech-tree.md` §4): a Research-segment function
(`FUN_10d8_4a86`) totals the cost of raising every deficient field to a component's required
levels, and the shared detail-card renderer uses that result to choose one of four caption
variants. The resolver's own final step, `FUN_1008_5916` (§11), separately returns a graded
"met / one level short / further" prerequisite result.

### 7. Per-design aggregate-stats computation

Confirmed by inspection of the exported client (`FUN_10f0_27da`, segment `10f0`): given a
resolved design record, this function walks its occupied component slots — **the loop bound is the
design's own slot-count byte at offset `+0x7a` (`stars.exe.export.c:100160`), not a fixed 9; correcting this section's own
earlier "up to 9" phrasing, written before §2's fix and §15e's extraction established that this
same byte offset is the design/hull slot-count field and that it ranges 2–16 per hull (see §2)** —
resolves each via the same shared component lookup as §4–§6 (with the owning race set as lookup
context), and accumulates several running totals depending on the resolved category/subtype:

- A **base-10000 "value" multiplier**, progressively divided down by a fixed percentage
  contributed by specific subtypes of category `0x0800` (the Electrical family of §4/§15, whose
  jammer and deflector sub-families carry a percentage stat). At the end it is converted into a **0–95-range
  byte** (roughly `100 − (10000 − total)/100`, clamped at 95) — the shape of a percentage-style
  stat that only ever gets *worse* as more of a specific component family is installed, most
  plausibly a defense/shielding effectiveness percentage. If a separate per-design flag byte is
  set, the stored value is further reduced by one quarter.
- A **base-1000 "mass-like" multiplier**, compounded *upward* (`× (100 + pct) / 100` per
  installed unit) by two more subtypes of category `0x0800`, capped at 2550 (`0x9f6`) and stored
  divided by 10 — the shape of a stat that compounds with quantity, consistent with cumulative
  mass or cost scaling.
- A **second base-1000 multiplier**, compounded *downward* (`× (100 − pct) / 100` per installed
  unit) by one subtype of category `0x1000`, stored divided by 10 — the shape of a fuel-efficiency
  or similar consumption-reducing stat.
- Two **paired min/max trackers**: one across categories `0x0010`/`0x0020` (packed into a nibble
  pair in one output byte, with a race-trait-flagged +1 adjustment applied before tracking), and
  one across a generically-computed "level" value (a component's own data-table level field, added
  to a base value from the design/hull record, clamped to 0–63) stored as two separate bytes. Both
  pairs read as "smallest and largest capability found among these categories in this design" —
  plausibly weapon-range brackets and tech/mass tiers respectively, feeding the battle engine's
  per-round targeting logic (see `combat-resolution.md`).
- A final 16-bit output field is either forced to the sentinel `0xFFFF` (when an input flag
  indicates the design/context is invalid) or set from an earlier per-design base value computed
  before the per-slot walk.

**Both min/max trackers resolved, closing this section's own Open Question.** A full line-by-line
read of `FUN_10f0_27da` (`stars.exe.export.c:99996`-`100196`) identifies both pairs concretely, by
tracing exactly which record offsets feed them (offsets per §15c's now-recovered beam/torpedo
layout):

- The **nibble-pair tracker** (packed into the design record's byte at offset `0x19`)
  is set only for categories `0x0010` (Beam) and `0x0020` (Torpedo/Missile) — from the resolved
  component's own **`+52` range field**, with
  the documented race-trait +1 folded in before the min/max compare
  (`stars.exe.export.c:100066`,`100140`-`100142`). **This is confirmed to be a min/max weapon-*range*
  bracket**, not a placeholder guess — the earlier "plausibly weapon-range brackets" hedge is now a
  confirmed reading.
- The **0–63-clamped "level" pair** (stored to design record offsets `+7`/`+8`) is
  computed once per slot as the smaller of 63 and (component `+56` field plus design byte `+6`)
  (`stars.exe.export.c:100058`-`100061`) — i.e. the resolved component's own **`+56` initiative
  field** (per §15c's beam/torpedo/hull-adjacent layout) plus a per-design base byte at design-record
  offset `+6`. **That base byte's own origin is now traced too, resolving the earlier hedge:** it is
  computed by a separate helper as the low 6 bits of the hull record's byte at offset `+123` (`+0x7b`,
  the byte immediately following the hull's declared slot count at `+122`, previously characterized
  only as the start of cosmetic designer-diagram display metadata per §15e) plus a weighted sum over
  the design's installed Electrical (`0x0800`) components at subtypes 5-7 (`(subtypeIndex-4) x
  quantity`), clamped 0-63 before use — hull offset `+123`'s low bits double as a real per-hull
  initiative-baseline value, not purely UI grid-coordinate data as originally assumed. This mirrors
  `combat-resolution.md` §5's "hull base initiative + component bonuses" description at the
  per-slot level rather than the per-token level). **This "level" value is therefore weapon
  initiative, not a tech or mass tier as originally guessed.** The min/max pair this produces
  (design offsets `+7`/`+8`) is read directly by the battle engine's firing dispatcher
  (`FUN_10f0_5950`, `stars.exe.export.c:102896`-`102899`) to decide, for each of the 64 possible
  initiative brackets processed during a round's firing phase, whether a given token has any weapon
  at all in that bracket before bothering to invoke the per-slot firing function — a concrete,
  traced consumer for this exact field, corroborating the reading. See `combat-resolution.md`'s §5
  cross-reference for how this feeds the firing loop.

This is the concrete "armor/mass/cost aggregation across installed components" mechanism the
original assignment asked about. Its general shape (accumulate percentage-style multipliers per
category, track min/max brackets, cap and rescale at the end) is confirmed with high confidence;
which specific named stat (armor vs. shield vs. cloak vs. jamming vs. fuel tank) each of the two
*percentage-multiplier* accumulators (the base-10000 "value" percentage and the base-1000
mass-like/fuel-like multipliers) ultimately represents was **not fully** confirmed at the time; the subtypes' real component names are now known from
§15's extraction (`extracted-game-data/component-stats.tsv`), and the next paragraphs resolve the
combined armor/shield cases, but the per-subtype mapping of these two accumulators was not
re-walked against those names. This matches and extends the existing, less-detailed characterization of this same function
noted for `combat-resolution.md`'s battle-roster construction.

**Partial resolution for the combined armor+shield components' secondary stat (§10c's Open
Question, shared with `combat-resolution.md`).** The base-10000 "value" percentage accumulator
described above is not driven solely by category `0x0800` as first characterized — reading the
function's full `if`/`else if` chain shows it is *also* special-cased directly by component
identity for two of the four combined armor-and-shield items: category `0x0004` (Shield) subtype
`6` — **Langston Shell** — sets the per-unit percentage to **95%** (`stars.exe.export.c:100071`-
`100074`, matching category byte 4 and subtype byte 6), and category `0x0008` (Armor) subtype
`9` — **Mega Poly Shell** — sets it to **80%** (`stars.exe.export.c:100076`-`100078`, matching
category byte 8 and subtype byte 9). Both subtype
indices match the two components' documented positions in §10c's shield/armor tables exactly. This
is a concrete, quantified application site for a "secondary" stat contribution from these two
specific components, distinct from their primary shield/armor point value, feeding into the
design's cached defense-percentage byte at design offset `+0xb` (0–95 range, `stars.exe.export.c:
100162`-`100171`). **Croby Sharmor (Shield subtype 3) and Fielded Kelarium (Armor subtype 6) are
not given any equivalent special-case anywhere in this function** — the `if`/`else if` chain checks
only subtypes 6 and 9 of their respective categories, nothing else in the Shield/Armor branches.

**Fully resolved by a further follow-up pass: Croby Sharmor's and Fielded Kelarium's own secondary
contributions live in two *other* design-stat functions, not `FUN_10f0_27da`.** Both were found by
following this open item's own suggestion to check "the per-design cost/value cache populator and
any other design-stat-consuming function":

- **`FUN_1038_2df8` (`stars.exe.export.c:19506`-`19642`)** — the §8/§9 cache-populator already
  documented above — special-cases Shield category **subtypes 3 *and* 6 identically**
  (`stars.exe.export.c:19607`-`19609`: for a Shield-category component, only subtypes 3 and 6
  contribute, each adding 65 per installed unit). Both **Croby Sharmor (subtype 3)** and **Langston Shell
  (subtype 6)** add a flat 65 points per installed unit into a design-cached field the same function
  seeds from the hull's own base-armor value (hull record `+0x38`, `stars.exe.export.c:19565`) —
  i.e. an effective-total-armor accumulator, at design byte offset **`+0x38`** (the decompiler shows
  it as word index `0x1c`; the same function's slot-count read at word index `0x3d` lands on the
  independently-known byte offset `+0x7a`, confirming 2-byte words). This is Croby Sharmor's secondary (armor-flavored) stat, and a
  *second*, independent secondary-stat site for Langston Shell beyond its already-documented 95%
  defense-percentage multiplier above.
- **`FUN_1038_0a0e` (`stars.exe.export.c:17092`-`17148`)**, a third, previously-uncharacterized
  design-stat function, called at the very start of `FUN_10f0_27da` itself
  (`stars.exe.export.c:100040`) — this is precisely the "per-design base value computed before the
  per-slot walk" that this section's own text above flagged as feeding `FUN_10f0_27da`'s final 16-bit
  output field without having traced its source; that field is now confirmed to be **design offset
  `+0x11`** (`stars.exe.export.c:100194`). `FUN_1038_0a0e` sums every installed Shield's own real
  shield-point value into this field (i.e. it is the design's **total shield points**), then
  special-cases exactly two Armor subtypes by flat literal constant, with every other Armor subtype
  contributing nothing at all (`stars.exe.export.c:17123`-`17133`): **Armor subtype 6 (Fielded
  Kelarium) adds a flat 50 points per unit**, and Armor subtype 9 (Mega Poly Shell) adds a flat 100
  points per unit — both counted as pseudo-shield-points despite being Armor components, the mirror
  image of Croby Sharmor/Langston Shell being counted as pseudo-armor above. (A per-race trait-13
  check, `stars.exe.export.c:17140`-`17143`, then scales the total +40% if set, before a 16-bit
  clamp.) This is Fielded Kelarium's secondary (shield-flavored) stat, and a *second* independent
  secondary-stat site for Mega Poly Shell beyond its already-documented 80% multiplier.

**All four combined items now have at least one traced, quantified secondary-stat site** (Langston
Shell and Mega Poly Shell have two each, in two different functions feeding two different cached
design fields): Croby Sharmor +65 armor-flavored points/unit (`FUN_1038_2df8`, design `+0x38`);
Langston Shell 95% defense-percentage multiplier (`FUN_10f0_27da`, design `+0xb`) *and* +65
armor-flavored points/unit (`FUN_1038_2df8`, design `+0x38`); Mega Poly Shell 80% defense-percentage
multiplier (`FUN_10f0_27da`, design `+0xb`) *and* +100 shield-flavored points/unit (`FUN_1038_0a0e`,
design `+0x11`); Fielded Kelarium +50 shield-flavored points/unit (`FUN_1038_0a0e`, design `+0x11`).
This closes the item in full — see `combat-resolution.md` §10c for the mirrored write-up there.

**Category `0x0800` — superseded by direct extraction, see §4/§15.** An earlier candidate guess (Mining Robots, based on a literal "Ironium" caption string in the shared detail-card renderer) is now superseded: the full component data block was located and extracted directly from the binary (§15), giving category `0x0800`'s real, confirmed name and every subtype's actual content — it is **Electrical** (cloaks, battle computers, jammers, capacitors), not a mining-robot family. The "Ironium" caption belonged to a different category's subtype rendered through the same shared renderer, not to `0x0800` itself.

### 8. AI/production-side design costing

Two further design-related helpers were found in segment `10f0`, both used by the AI production
planner (see `combat-resolution.md` and the AI-behavior material there for how they're consumed,
not repeated here):

- A **design cost/tier comparator**, used purely to establish a sort order between two designs
  (e.g. "which of these two designs is more advanced/expensive"), with no gating behavior of its
  own.
- A **cost estimator gated by a race-trait boolean flag** (trait index 12 in the per-race trait
  table this document observed elsewhere; lesser-trait bit 12 is Bleeding Edge Technology, §5): when set, a copy of the design's per-slot cost data is
  passed through an additional transform (in a different segment) before being used to estimate
  build cost; when clear, the raw per-slot costs are used directly. This is consistent in shape
  with `race-traits.md`'s independently-documented §7 finding of "a separate general-purpose
  compute-adjusted-build-cost routine... flat roughly-±25% adjustments keyed to specific hull-type
  categories crossed with specific race-trait checks" — **plausibly the same underlying mechanism
  approached from the AI-costing side here and from the race-wizard side there, but this was not
  confirmed to be literally the same function** (resolved in the next paragraph and in
  `race-traits.md` §7).

  **Follow-up pass: both halves of this mechanism are now precisely identified, and the "same
  function?" question is resolved as "no, but connected."** The estimator itself is `FUN_10f0_50e0`
  (segment `10f0`) — confirmed by locating its exact trait-12 read through `FUN_10e0_226e` (the same generic per-race
  boolean-trait accessor used throughout this project, e.g. in `race-traits.md` §1a), asked for bit 12
  on the owning race's 192-byte player record. When the
  trait-12 flag is set, `FUN_10f0_50e0` copies roughly 146 bytes of the design's per-slot data into a
  local buffer and calls `FUN_1038_2df8` (segment 8) — this is the "additional transform (in a
  different segment)" referred to above. `FUN_1038_2df8` is called from more than a dozen other sites
  across the client and turns out to be the **same routine already described in §9 below** as
  populating each design's cached per-design value/combat-power fields — i.e., this section's
  trait-12 gate and §9's cache-populator are not two separate mechanisms, they are the *same*
  function reached two different ways (recomputed fresh into a scratch copy here, versus recomputed
  in place for the live design elsewhere). Reading `FUN_1038_2df8` directly shows it accumulating
  per-slot cost, mass and armour totals with a handful of category-specific rules. One of them is
  Regenerating Shields (lesser-trait bit 13): when the design's knowledge/detail-level byte `+0x7b`
  is 7 (fully specified, which every design of the player's own has; it is not a hull type) and the
  race has bit 13, the design's **armour total** (`+0x38`) takes only half of the armour contributed
  by **Armor-category** (`0x0008`) parts, rounded down per slot (slot count times the part's armour,
  halved). The hull's base armour and the armour of Croby Sharmor, Langston Shell and the Multi Cargo
  Pod are not halved, and cost and mass are unaffected (`stars.exe.export.c:19538`-`19540`,
  `:19611`-`19618`; this agrees with `race-traits.md` §3 and `combat-resolution.md` "Shields before
  armor") — a materially different shape from `race-traits.md`
  §7's described "habitability multiplier, then flat ±25% hull-category-crossed-with-trait
  adjustments, then non-positive-habitability doubling" routine. **Conclusion: `FUN_10f0_50e0`/
  `FUN_1038_2df8` (this section's mechanism) and `race-traits.md` §7's own "adjusted build cost for a
  hull design" routine are two genuinely different pieces of code**, not two views of the same
  function as originally hypothesized. `race-traits.md` §7 has since resolved its side: no separate
  "adjusted build cost" routine exists; that description was an early, garbled summary of the shared
  build-cost routine `FUN_1050_7d44` (component-category-by-PRT ±25% adjustments, no habitability
  step).

### 9. Cached per-design fields in the record tail, `+0x87`-`+0x92` (segment 8 and turn-generation step 36)

Settled by the step-36 reconciliation (`turn-generation-engine.md` §1 step 36; this section replaces its earlier description, which treated the tail as one "value" cache recomputed for every design of every race). The last 12 bytes of the 147-byte record hold cached summary fields, **and the same offset means different things in the ship table (16 slots, per-race pointer at DS `0xbe`) and the starbase table (10 slots, pointer at DS `0x10c`)**:

| Offset | Size | Ship designs | Starbase designs |
|---|---|---|---|
| `+0x7b` | byte | knowledge/detail level (7 = fully specified, the level the Regenerating Shields armour rule of §8 tests); written by the step-39 visibility pass `FUN_1070_7360` (7 for a design whose `+0x8b` bit is set for the observer, else 7 or 3 for an already-revealed one; below) | seen-level, raised to 3 when an observer's scan reveals the design (`FUN_1070_537e`, `stars.exe.export.c:48627`-`48640`); also written by `FUN_1070_7360` as for ships |
| `+0x7c` | flags | `0x02` unused/deleted slot; `0x80` design failed the legality check (set by step 36, `:72323`-`72326`); `0x01` design revealed to an observer (set by `FUN_1070_7360`) | `0x02` unused/deleted slot; `0x01` design revealed to an observer |
| `+0x87` | 32-bit | **weapon value** from `FUN_1038_079e` (below). Never squared. | **squared cloak factor** `(100 - cloak%)²`, 4-10,000 (10,000 = uncloaked), written every turn by step 36 (`:72283`-`72290`) |
| `+0x8b` | word | **design-revealed-to-player mask**, one bit per player (below) | same meaning; set by the packet rule below |
| `+0x8d` | word | combined scanner range, from `FUN_1038_337e`, written by step 36 (`:72309`) | not written |
| `+0x8f` | word | second scanner range figure (`:72311`) | not written |
| `+0x91` | byte | counter-cloak percentage from the Tachyon table (`:72313`) | not written |
| `+0x92` | byte | scanner-kind flag bits (`:72315`) | not written |

**The ship-side weapon value (`+0x87`).** `FUN_1038_079e` (`:16964`-`17050`) sums, over a design's occupied slots, category `0x0010` (beam-type: `(range + 3) x damage x quantity / 4`, cut to a third for components carrying a per-component flag), category `0x0020` (`(range - 2) x damage x quantity / 2`) and category `0x0040` (`(two damage fields summed) x quantity x 2`); category `0x0800` subtypes 12 and 13 (the two capacitors) compound a percentage multiplier onto the first sum, and a final per-design adjustment from `FUN_10f0_2184` (not read) is added. In practice the result is positive exactly when the design mounts at least one weapon-category component; a design with none scores 0. `fleet-movement-scanning-cargo.md` calls this "Group A". Writers of the cached copy at `+0x87`: `FUN_1038_09ae` (`:17054`-`17088`), which walks every non-deleted ship design of every loaded race and whose **only call site is the AI dispatch preamble `FUN_1088_0000` (`:57992`)**, so it runs once per AI player per turn just before that player's personality driver; the design-detail panel (`:84912`-`84922`), which stores the freshly computed value for whichever design it describes; and `FUN_1038_2df8` (`:19640`), which sets the field to -1 whenever it recomputes the design's other cached statistics. The race-scoring tiering (`FUN_1038_384c`, `:20135`) calls `FUN_1038_079e` directly and does not read the cache. The cached copy is read by the AI's fleet-role predicates `FUN_1090_2db6` and `FUN_1090_2e94` (`ai-opponent-behavior.md` §11); the turn-generation run never refreshes it (step 36 leaves ship `+0x87` alone), which is harmless because nothing in that run reads it.

**The starbase-side cloak factor (`+0x87`).** Computed by `FUN_1048_57b6` as documented in `turn-generation-engine.md` §1 step 36 and consumed only by the planet sweeps of the visibility pass (`FUN_1070_5f00`, `FUN_1070_64fc`, `FUN_1070_6d7c`). It has no connection to the ship weapon value above, to build costs, or to the AI's production decisions.

The separate combat-power / defense-percentage estimate of `fleet-movement-scanning-cargo.md` ("Group B") is not one of the fields above; this pass did not pin its cache, if any, to record offsets.

**The `+0x8b` mask (resolved).** A raw-byte sweep of every code segment for the displacement, cross-checked against the export, finds exactly nine accesses: four clears, three sets and two tests. Bit *p* means "player *p* has learned this design during this turn generation".
- *Cleared* for every slot of both tables whenever a player file is loaded and its design tables are set up (`FUN_1070_0502`, `:44212`, `:44252`, `:44562`, `:44657`). It is therefore not carried from one turn to the next.
- *Set, Space Demolition minefields* (`FUN_10b0_312a`, `:74575`-`74607`). When a fleet strikes a minefield, or is caught by a detonating one, and the field's owner has primary trait 5 (Space Demolition), the owner's bit is set on the ship designs in that fleet.
- *Set, Packet Physics packets* (`FUN_10b0_0f9a`, `:72750`-`72758`). When a mineral packet reaches a planet whose starbase carries a mass driver, and the packet's owner has primary trait 6 (Packet Physics), the owner's bit is set on that planet's starbase design.
- *Read, end of the visibility pass* (`FUN_1070_7360`, `:50073`-`50150`, called last from `FUN_1070_5a2e`, turn-generation step 39). For an observing player *p*, every design of every other player with bit *p* set is marked revealed (`+0x7c` bit 0) and shown in full detail (`+0x7b` = 7). A design already marked revealed without the bit gets detail 7 if *p* is a War Monger (primary trait 2) and 3 otherwise. The observer's records of that player's known designs are updated to match.
- No other code reads or writes it: not the designer, not the AI and not battle setup.

So the field carries two trait perks from the movement and packet steps to the per-player reports: Space Demolition learns the designs that hit its minefields, and Packet Physics learns the starbase designs its packets strike. This reader also writes `+0x7b`/`+0x7c` on **ship** designs, as the table above now records.

### 10. Where this fits in the turn-order delta protocol

Not a topic of this document — now written up in full in `save-turn-file-format.md` §8 — but noted
for completeness: opcodes `0x1b` (design create/delete, §1) and `0x1e` (component slot write, §2) are
two cases inside a roughly 1000-line decoder switch living entirely in segment `1048`. That decoder
is the client's mechanism for applying a locally-queued design edit — the same coalescing,
opcode-tagged record log that carries every other player action (fleet orders, production-queue
edits, etc.) to the turn-order file. `save-turn-file-format.md` §8 documents the generic field-diff
builder, adaptive per-field byte-width encoding, and coalescing/rewind mechanism this decoder's
sibling encoder uses (traced there for a 6-field fleet record, not for these two design-specific
opcodes) and §5 documents the replay-suppression flag that stops applying a buffered record from
re-recording itself. Nothing about the compatibility or gating logic in §4–§6 depends on that
transport; it is invoked identically whether the edit was just made interactively or is being
replayed from a buffered record.

### 11. Hunting the per-component base-stat table itself (armor/damage/mass/cost numeric data)

This section documents a follow-up investigation, requested by `combat-resolution.md`'s worked
examples and this document's own §7/§9, aimed specifically at locating the *raw numeric* per-component
data (weapon damage, armor points, mass, cost, etc.) that both documents currently lack. It traces
`FUN_1008_5194` (§4's category/subtype resolver) and its sibling accessor one level deeper than §4
did, with a partial result: the lookup *structure* is now resolved further than before, but the raw
table content itself could not be recovered from the exported `.c`, and its on-disk location could
not be confirmed either.

**The resolver computes a pointer, not an inline value.** For every one of the 16 categories,
`FUN_1008_5194` computes a per-subtype record address as `base + subtype_index * stride` (the exact
`base`/`stride`/count values already tabulated in §4) and packs it into the caller's output structure
as a two-word **far pointer** — one word holding the computed offset, the other holding a segment
value. Critically, that segment word is the **identical literal constant across all 16 category
branches**, with no accompanying `DAT_`-style symbol the way this project's ordinary static data
references normally appear (compare, e.g., `DAT_1128_59c2` used two paragraphs below, which *is* a
resolved, named symbol). This is decompiler-observable evidence that Ghidra itself could not resolve
this particular pointer target to a concrete data symbol — consistent with case (b) from this
investigation's brief: the data is real and lives somewhere, but it is opaque to the decompile, not
inlined as a literal array.

**Category `0x0800`'s base/stride/count, left unresolved in §4, is now recovered.** Reading
`FUN_1008_5194`'s `0x800`-category branch directly (rather than inferring it from a different
function, as §4's writeup did) gives **base offset `0x407e`, record stride `54` bytes, `17` legal
subtype values (`0`–`0x10` inclusive)**. Laid out by base offset, all 16 categories' tables turn out
to be back-to-back with **zero gap** — each category's table ends exactly where the next one's base
begins (verified arithmetically for all 15 adjacent pairs) — strong internal evidence that this is
genuinely one contiguous ~19.3 KB data block (`0x05db` through `0x5118`, categories in the on-disk
order `0x0400, 0x4000, 0x0010, 0x0020, 0x0040, 0x2000, 0x8000, 0x0080, 0x0100, 0x1000, 0x0800, 0x0004,
0x0002, 0x0008, 0x0001`), not 16 independently-placed tables. This corroborates §4's "one shared
static data area" description with a concrete, self-consistent layout.

**Attempting to physically locate this block did not succeed on this pass, and the attempt itself is
worth recording** — but see **§15, which resolves it**; the reasoning below is retained only to
document the wrong turn. Ghidra's function-naming convention for this decompile assigns each NE
segment a synthetic base address of `0x1000 + (segmentIndex-1) * 8` (confirmed by two independent
cross-checks: `DAT_1128_*` symbols land exactly on the one segment the NE segment table itself flags
as data-type, segment 38 of 38; and the literal segment constant used throughout this document,
`0x10e0`, maps arithmetically onto segment 29). On that reading, segment 29 is only **17,556 bytes**
long on disk (its `minAlloc` matches its length exactly, so there is no hidden BSS extension), while
the component table's offsets require **up to ~20,760 bytes** — a contradiction of about 3.2 KB.
Directly inspecting the raw file bytes at segment 29's computed offset for several of the category
bases showed well-formed x86 function prologues/epilogues, not plausible small-integer stat records.
This pass concluded the `0x10e0` constant was an unresolved internal fixup placeholder and that the
table's real location could not be determined. **That conclusion was wrong in its diagnosis but right
in its suspicion of the constant**: `0x10e0` is not a relocated data selector at all — see §15.

**Resource-table scan: exhaustive, and negative for this specific question.** All resource
types/entries in `stars.exe`'s NE resource table were enumerated directly (not just the
previously-catalogued `RT_BITMAP`/`WAVE`/three sprite blobs from `code-coverage-report.md`). The full
type list is: `0x8001` (cursors, 11), `0x8002` (bitmaps, 38 — includes the sprite sheets already
catalogued), `0x8003` (icons, 10), `0x8004` (menu, 1), `0x8005` (dialog templates, 36), `0x8009`
(accelerator tables, 2), `0x800C`/`0x800E` (cursor/icon group headers, 11/10), the three custom
~4.3/51.8/19.9 KB blobs (`0xa710`/`0xa712`/`0xa714`, previously identified as packed sprite
containers), and the six `WAVE`-tagged audio resources (`0x72e`, 12–44 KB each). **No `RT_STRING`
(`0x8006`) and no `RT_RCDATA`/generic-data (`0x800A`) resources exist anywhere in this executable** —
every resource present is a standard UI/media type already accounted for. This rules out "the
component table is a distinct Win16 resource" as a hypothesis: whatever holds this data, it is not a
resource-table entry, reinforcing that it is plain program data embedded in a code/data segment (as
the pointer-construction evidence above already suggested) rather than a resource.

**A genuinely new, concrete finding recovered along the way: the tech-level-prerequisite check is now
located.** `FUN_1008_5194`'s final step (`FUN_1008_5916`, same segment) was previously
uncharacterized. Read directly, it takes the far pointer's offset (advanced by 2 bytes past the record
start — consistent with skipping a leading "level"/tier field, matching §7's separately-documented
"component's own data-table level field") and compares **6 consecutive bytes** there against 6 bytes
fetched from byte offset `0x1a` of the current player's record — the same 192-byte-stride per-player record this project's other segments already use (confirmed
elsewhere, e.g. segment 18/`FUN_1088`'s "3-bit category field from each 192-byte player-slot record").
Six consecutive per-player byte fields at a fixed offset is an exact structural match for
`research-tech-tree.md`'s six tech categories in their documented order (**Energy, Weapons,
Propulsion, Construction, Electronics, Biotechnology**) — i.e., each component record's first 6 data
bytes (after the level field) most plausibly hold that component's **required tech level in each of
the six fields**, and `FUN_1008_5916` is the code computing whether/how far the current player falls
short (its return value distinguishes "fully met," "exactly one field one level short," and "further
away" — the two near-miss grades apply only when the single field that is short is the one
currently being researched; any other shortfall gives the plain "unavailable" result — matching the graded, non-binary compatibility feedback `client-ui-dialog-catalog.md`'s
Slot/equipment editor entry describes and which this document's own §6 previously flagged as unlocated
— **this closes that Open Question**, at the mechanism level, independent of the raw-table-location
problem above.

**Net result for the placeholder-numbers problem (as of this pass).** The lookup structure was
understood in more detail than before (contiguous 16-category ~19.3 KB block, exact base/stride/count
for all 16 categories including the previously-unresolved `0x0800`, and the
first-bytes-are-tech-requirements layout), but the actual numeric content remained unrecovered.
**§15 closes this**: the block was located and fully extracted on a later pass, and the
"first 6 bytes after the level field are the six tech prerequisites" prediction made here was
confirmed byte-for-byte across all 239 records.

### 12. The 36-byte per-race/per-slot record decoded, correcting §2's "hull allowed-mask template" hypothesis (`FUN_1070_2808`)

Confirmed by inspection of the exported client: `FUN_1070_2808` (segment 15, `stars.exe.export.c:46000`)
— the function §2 identified only as "a per-hull 'slot template' resolver and writer" — does not
read or test any category-mask, allowed-type, or maximum-quantity data anywhere in its body. It is
a pure write/cache routine, invoked only after opcode `0x1e`'s own case has already run every
legality check available to it (the mount-nibble/second-nibble/quantity range checks quoted in §2,
themselves applied after §4-§6's resolver). Its destination is a per-race, 16-entry, 36-byte-stride
array — confirmed at its allocation site (`stars.exe.export.c:44913`, sized exactly `0x240` = 16 ×
36 bytes, allocated lazily the first time a race's design editor touches a given race) — addressed
by the same 0-15 slot index §2 already documents as checked against the per-race "next free slot"
counter.

Each 36-byte entry is written in two pieces:

- **Bytes 0-3** are copied verbatim from the incoming opcode-`0x1e` payload's own leading 4 bytes
  (the race/slot-index/mount-nibble/second-nibble/quantity fields §2 already documents from the
  wire format), after which the destination's byte 0 has its high nibble unconditionally
  overwritten with the slot index itself — so the cached entry always self-identifies its own slot
  position, independent of whatever the wire payload's own high nibble carried.
- **Bytes 4-35 (32 bytes)** hold a variable-length trailing field from the same payload, written one
  of two ways selected by a flag byte immediately after the 4-byte header: either copied verbatim as
  a null-terminated byte string, or — when the flag is nonzero — unpacked through a generic
  nibble-oriented variable-width decoder, `FUN_1040_35a4` (segment 8, `stars.exe.export.c:23497`),
  using the flag byte as one of that decoder's own parameters.

Two details point strongly to this 32-byte field being a **cached display label for the slot**,
not hull-defined data: 32 bytes is exactly this project's established 31-character-plus-null-terminator
name-field convention (`save-turn-file-format.md` §3's "String/name fields... capped at 31
characters"), and the plain-copy branch is the simplest possible "cache this text" operation. The
most plausible reading is that `FUN_1070_2808` exists so the ship-design editor's slot list can
redraw a component's name/quantity label without re-resolving it from `FUN_1008_5194`'s tables on
every repaint — a UI-side cache populated once per edit, not a hull-defined legality template.

**Consequence for item 3's original question:** this record is not "the hull's allowed category
mask per slot." No bitmask, category-set, or maximum-quantity field of the kind originally
hypothesized is read, tested, or written anywhere in this function — every legality decision it
could need has already been made upstream by the time it runs. §13 below identifies a
substantially stronger candidate location for genuine per-hull slot data.

As an incidental secondary finding: `FUN_1040_35a4`'s body is a nibble-at-a-time variable-width
value decoder (each 4-bit unit is read as a value unless it equals a reserved escape value, in
which case further nibbles are consumed to extend it). It was once offered as a candidate for a
record-level "compression step" in `save-turn-file-format.md` §1; that file's later pass showed the
record layer has no compression (the per-record transform is a keystream cipher, its §10.4), so this
decoder is only the packer for a few string fields (opcodes `0x1e` and `0x2c`).

### 13. Hull-type definitions located: categories `0x4000` and `0x0400` (a follow-up to items 3 and 4)

Confirmed by inspection of the exported client: two small standalone accessor functions bypass
`FUN_1008_5194`'s validation entirely and simply compute an address into categories `0x4000` and
`0x0400`'s data region — `FUN_1008_0000` (`stars.exe.export.c:3167`, returns `index * 143 + 0x5db`,
exactly category `0x0400`'s base/stride from §4's table) and `FUN_1008_5118`
(`stars.exe.export.c:3185`, returns `index * 143 + 0x1548` for `index <= 31`, otherwise re-dispatching
to `FUN_1008_0000(index - 32)`). This proves categories `0x4000` (32 subtypes) and `0x0400` (5
subtypes) are **one single, contiguous 37-entry table** addressed by one unified 0-36 index — not
merely two categories that happen to sit adjacent in §11's contiguous-block finding, but one
logical table split across two category bit-values for the ordinary legality-dispatch system.

`FUN_1008_5118` is an exceptionally widely-used accessor — over 40 call sites spread across many
segments (production, fleet display, combat, the AI production planner, and more) — and a large
fraction of them compute its input index as a value read directly out of a design record at a fixed
offset, in the pattern `*(int *)(designPointer + hullSlotIndex * 0x93 + tableBase)` (`0x93` = 147,
matching §1's design-record size exactly; representative call sites: `stars.exe.export.c:20091`,
`26219`, `36082`, `41033`, `56957`, `73226`, `86152`, `110185`, `110517`, `114279`). This is decisive:
every design record stores a hull-type index at a fixed offset, and that index is resolved through
this one shared accessor into a record from this 37-entry, 143-byte-stride table. Categories
`0x4000`/`0x0400` are, with high confidence, the **hull-type (and starbase-chassis) definition
table**: 32 regular ship hulls plus 5 starbase-type chassis, one 143-byte record each.

143 bytes is by far the largest per-subtype record size of any of the 16 categories (every other
category is 54-78 bytes; see §4's table) — consistent with a hull record needing to hold
considerably more data per entry than a weapon or engine does. One consumer of this accessor
(`stars.exe.export.c:47030`-`47051`) treats a portion of the returned record as a null-terminated
name string (a few bytes into the record), and another (`stars.exe.export.c:19563`-`19565`) copies
two specific fields out of the record (at record-relative offsets `0x28` and `0x38`) into the owning
design's own cached fields — confirming the record holds at least a name plus multiple distinct
numeric stat fields, though this pass did not identify which named stat (mass, armor, slot count, or
otherwise) corresponds to which offset, nor did it locate a per-slot sub-array within the 143 bytes.

**This is offered as a strong structural answer to item 4** ("whether hull slot count truly varies
by hull type"): a dedicated 143-byte-per-hull definition table, addressed identically to every
other real component category and clearly carrying multiple hull-specific fields beyond just a
name, is difficult to explain unless individual hulls really do differ in their stats — plausibly
including slot count and/or a per-slot allowed-category mask of the kind item 3 was originally
looking for. **This inference is now confirmed byte-for-byte in §15e**: the table was located and
read, every hull's slot count, per-slot capacity and per-slot allowed-category mask are stored
exactly where this section predicted. **Correction to this section's own original field guess:**
the two fields observed being copied out of the record into a design's cached fields (at
record-relative `0x28` and `0x38`) are the hull's **Mass** (the universal `+40` field every
component/hull record carries, per §15b's layout table, not a hull-specific stat) and **base armor
points** (`+56`, per §15e) respectively — not cargo capacity as this section originally guessed. The
hull's actual cargo capacity lives at a different offset, `+52`, per §15e.

### 14. Component category identity: a PRT/ability-bit fingerprinting pass (item 2 follow-up)

No outside per-component list was available to this pass, so that avenue could not be attempted. Instead, this pass read
`FUN_1008_5194`'s full dispatch table directly (`stars.exe.export.c:3251`-`3629`, all 16 category
branches) and extracted each category's complete set of hard-coded per-subtype PRT-id and
generic-ability-bit checks, on the theory (already validated for Engine and Bomb in §5) that a
category's gating "fingerprint" narrows down its identity even without string data.

A byproduct worth recording first *(corrected, lesser-trait re-check pass)*: the "bit" numbers in
this section's table are bits of the **lesser-trait word** (record `+0x4e`, `FUN_10e0_226e`). They
are not bits of the §6 ability mask (record `+0x52`, §14a), and none of them is Inner Strength.
**Bit 9 is Only Basic Remote Mining.** It blocks four of the five mining robots in category `0x0080`.
It also blocks the Midget Miner, Miner, Maxi-Miner and Ultra-Miner hulls (`0x4000` subtypes `0x14`,
`0x16`, `0x17`, `0x18`, `:3527`-`3531`), which leaves the Mini-Miner as the only mining hull, as the
trait's description says. **Bit 2 is Advanced Remote Mining.** It is required for the Robo-Midget
and Robo-Ultra robots and for the Midget Miner, Miner and Ultra-Miner hulls (`:3533`-`3537`), which
are the three extra mining hulls and two extra robots the trait is documented to grant. The Maxi-Miner hull needs no
trait. The earlier claim that Inner-Strength-linked hull exclusions exist is withdrawn. Inner
Strength's only hull gate is the ordinary PRT-4 requirement on the Super Freighter (subtype 3) and
the Fuel Transport (subtype `0x19`).

**Status note added after §15.** The whole point of this fingerprinting exercise — putting names to
the categories without a string source — has been overtaken by §15, which read every category's
names directly out of the recovered records. The table below is retained because its *gating*
content (which subtypes are PRT- or ability-bit-restricted) remains valid and is not duplicated
elsewhere, but its "candidate identity" column should be read against §4's now-confirmed list.
Scoring the guesses: `0x0010` Beam Weapons, `0x0020` Torpedo/Missile, `0x0100` Mine Layers and
`0x0200` gate/packet-related were all **correct**; `0x1000` Fuel Tanks was **close** (fuel tanks are
in it, but it is a broader mechanical family); `0x0800` Mining Robots and `0x8000`
Orbital/Colonization were **wrong** (electrical, and planetary scanners/defenses respectively), as
were the `0x0040`/`0x0080` labels carried over from §5.

Per-category fingerprints and candidate identities (PRT ids per §5's convention: Hyper Expansion=0,
Super Stealth=1 *(presumed by standard PRT ordering; not independently confirmed by this project's
own cross-references the way 0/2-8 are)*, War Monger=2, Claim Adjuster=3, Inner Strength=4, Space
Demolition=5, Packet Physics=6, Interstellar Traveller=7, Alternate Reality=8):

| Category | Subtypes | Stride | PRT/ability-bit fingerprint | Candidate identity |
|---|---|---|---|---|
| `0x0001` | 16 | 78 | PRT0 subtype 0; bit7 (No Ram Scoop Engines) required for subtype 7, blocks subtypes 10-15; bit0 (Improved Fuel Efficiency) required for subtypes 2, 15 | **Engine** — confirmed (§5) |
| `0x0002` | 16 | 56 | PRT1 for subtypes 5, 6, 14; bit10 (No Advanced Scanners) blocks subtypes 7, 8, 12, the penetrating Ferret, Dolphin and Elephant scanners | **Ship scanners** (§4, §15) |
| `0x0004` | 10 | 54 | PRT1 for subtype 4; PRT4 for subtype 3 | **Shields** (§4, §15) |
| `0x0008` | 12 | 54 | PRT1 for subtype 7; PRT4 for subtype 6 | **Armor** (§4, §15) |
| `0x0010` | 24 | 60 | PRT4 for subtype 2; PRT2 for subtypes 14, 16 | **Beam weapons** (§4, §15; the fingerprint guess was correct) |
| `0x0020` | 12 | 60 | none found | **Torpedoes / capital missiles** (§4, §15; the fingerprint guess was correct) |
| `0x0040` | 15 | 58 | PRT3 for subtype 9; PRT4 blocks subtypes 10-14 | ~~Not identified~~ **Bombs** (§15): PRT 4, Inner Strength, loses the five smart bombs 10-14, and PRT 3 gets the Retro Bomb (9). No lesser-trait test |
| `0x0080` | 8 | 54 | PRT3 for subtype 7; bit9 (Only Basic Remote Mining) blocks subtypes 0, 2-5; bit2 (Advanced Remote Mining) required for subtypes 0, 5 | ~~**Bomb** — confirmed (§5)~~ **Mining robots** (§15; corrected, see §5) |
| `0x0100` | 10 | 54 | PRT5 for 8 of 10 subtypes (0, 2-6, 8, 9); PRT5-or-4 for subtype 7; PRT2 **blocked from** subtype 1 (Mine Dispenser 50 is open to every PRT except War Monger) | **Mine layers** (§4, §15; the fingerprint guess was correct) |
| `0x0200` | 16 | 56 | PRT6 for most of subtypes 7-15 (all except 9 and 12); PRT7-linked gating for subtypes 0-6 (without PRT 7 only stargates 0, 2 and 3 are available; PRT 0 is blocked from all seven) | **Stargates (subtypes 0-6) and mass drivers (7-15)** (§4, §15) |
| `0x0400` | 5 (part of the 37-entry hull table, §13) | 143 | ~~PRT4 for subtypes 1, 3~~ *corrected:* lesser-trait bit3 (Improved Starbases) required for subtypes 1 and 3, the Space Dock and Ultra Station (`:3459`-`3463`); PRT8 required for subtype 4, the Death Star (`:3464`, `:3507`-`3510`) | **Starbase-type hull chassis** — see §13 |
| `0x0800` | 17 | 54 | PRT1 (subtypes 0, 3), PRT4 (8, 11, 15), PRT0 (13), PRT5 (14), PRT7 (16); whole-category ability-bit gate | **Electrical** — cloaks, battle computers, jammers, capacitors (§4, §15); the earlier Mining Robot guess from the "Ironium" caption was wrong (that caption belongs to category `0x0200`) |
| `0x1000` | 11 | 54 | PRT8 blocks subtype 0, requires subtype 1; whole-category ability-bit gate | **Mechanical** — colonisation modules, cargo pods, fuel tanks, thrusters and related (§4, §15) |
| `0x2000` | 20 | 54 | ~~bit1 blocks subtypes 0-7 only~~ *corrected:* bit1 (Total Terraforming) is **required** for subtypes 0-7, the eight Total Terraform items; subtypes 8-19 have no gate (`:3589`-`3595`) | Terraforming (§15) |
| `0x4000` | 32 (part of the 37-entry hull table, §13) | 143 | PRT0 (14, 31), PRT4 (3, 25), PRT2 (8, 10), PRT1 (12, 18), PRT5 (27, 28); bit9 (Only Basic Remote Mining) blocks 20, 22, 23, 24; bit2 (Advanced Remote Mining) required for 20, 22, 24 | **Regular ship hulls** — see §13 |
| `0x8000` | 15 | 54 | PRT8 blocks most subtypes below 14; bit10 (No Advanced Scanners) blocks the penetrating planetary scanners (subtypes 5-8, the ones whose range field is negative); PRT2 blocked from subtypes 11-13 (`:3564`-`3579`) | **Planetary scanners and planetary defenses** (§4, §15); the earlier Orbital/Colonization guess was wrong |

This pass firms up Bomb and Engine's existing confirmations with previously-unrecorded bit numbers,
identifies the hull table (§13) with high confidence, and offers several hedged candidates (Mine
Layer for `0x0100` is the strongest of the new ones, given 8 of 10 subtypes exclusive to one PRT),
but at the time most of the originally-unnamed categories remained unconfirmed, since no
outside string source was available. §15 later closed the gap by
reading every category's names from the recovered records, and the identity column above now
reflects those names.

### 14a. The generic ability bitmask fully enumerated — and identified as a one-time battle/event gift tracker, not a Race-Wizard trait flag (item 2, resolved)

A follow-up pass fully read `FUN_10d8_4b8e` (`stars.exe.export.c:91837`-`91913`, segment 28) — the
§6 gate this document had previously sampled for only two bits — in its entirety. The function is a
single, exhaustive `if`/`else if` chain with **no unhandled cases**: every `(category, subtype)`
pair it recognizes maps to exactly one bit of the 32-bit mask, and every other pair maps to bit 0
(no-op, since the test is skipped when the computed bit value is 0). This gives a **complete**
enumeration of all 12 bits the ship-design system actually consults, closing the "only bits 2 and 9
have candidates" starting point entirely. *(Lesser-trait re-check pass: the "bits 2 and 9" of that
starting point were never bits of this mask. They are lesser-trait bits, Advanced Remote Mining and
Only Basic Remote Mining, of the separate word at record `+0x4e` that the resolver tests directly;
see §5 and §14.)*

| Bit | Value | Category (subtype) | Named component (`extracted-game-data/component-stats.tsv`) |
|---|---|---|---|
| 0 | `0x0001` | Mechanical `0x1000` (4) | Multi Cargo Pod |
| 1 | `0x0002` | Electrical `0x0800` (4) | Multi Function Pod |
| 2 | `0x0004` | Shield `0x0004` (6) | Langston Shell |
| 3 | `0x0008` | Armor `0x0008` (9) | Mega Poly Shell |
| 4 | `0x0010` | Mining robot `0x0080` (6) | Alien Miner |
| 5 | `0x0020` | Bomb `0x0040` (8) | Hush-a-Boom |
| 6 | `0x0040` | Torpedo/missile `0x0020` (7) | Anti Matter Torpedo |
| 7 | `0x0080` | Beam `0x0010` (0x12/18) | Multi Contained Munition |
| 8 | `0x0100` | Hull `0x4000` (0x1e/30) | Mini Morph |
| 9 | `0x0200` | Engine `0x0001` (8) | Enigma Pulsar |
| 10 | `0x0400` | Planetary `0x8000` (0xe/14) | Genesis Device |
| 11 | `0x0800` | Mechanical `0x1000` (9) | Jump Gate |

(`stars.exe.export.c:91849`-`91907`, one comparison per row.) The mask itself lives at a fixed
offset inside the 192-byte per-player record, `player_index * 0xc0 + 0x5a14`
(`stars.exe.export.c:91908`), i.e. player-record offset `+0x52` — the same field `FUN_1008_5194`
reaches through this gate for the 12 rows above (`stars.exe.export.c:3294`, `3499`, `3604`) and for
no others; none of these 12 `(category, subtype)` pairs overlaps any of the PRT-index or
boolean-trait-flag checks `FUN_1008_5194` performs directly (§5/§14's fingerprint table), confirming
this is a wholly separate gating mechanism layered on top of ordinary PRT/LRT availability, not a
restatement of it.

**None of the 12 named components matches any PRT- or LRT-exclusive item already documented in
`race-traits.md`, and this pass found why: the bit is not set by the Race Wizard at all.** Searching
every other reference to the same field address (`0x5a14`, six further hits across the whole
decompile — `stars.exe.export.c:50844`, `102999`, `115362`, `115747`, `115759`, `115763`, `115823`)
finds no Race-Wizard/save-race code touching it (`50844` merely zeroes it at race-record
initialization); instead, two live, executing writers were found, both **one-time random in-game
grants**, not race-design choices:

- **`FUN_10f0_61a2` (`stars.exe.export.c:102978`-`103038`), the tech-gain-from-battle roller this
  project's `combat-resolution.md` §9 already documents structurally** ("once a battle is won
  outright... roughly even odds... of granting one of 13 tech-related field bonuses... or, failing
  that, one of 6 fallback 'growth'-style field bonuses"). Reading it directly: on a successful roll
  (a 0-99 roll above 49), the function picks a random field `0`-`12` (a 0-12 roll through
  `FUN_1040_1652`, i.e. 13 candidates — matching "13 tech-related field bonuses" exactly) and, if
  that field's global eligibility weight is nonzero *and* its bit is not already set in this exact
  mask (`stars.exe.export.c:102999`) *and* a second
  weighted roll succeeds, calls `FUN_1118_1196` to **set that bit** and post a reward message
  (`stars.exe.export.c:103002`-`103011`). This is a direct, executing link between this bitmask and
  the "13 tech-related field bonuses" combat-resolution.md already knew existed but had not traced
  to a mechanism: **the bitmask is the record of which one-time battle-victory tech/part gifts a
  race has already received**, and 12 of its 13 possible bits are exactly the 12 special components
  §6's gate unlocks once granted (the 13th slot most plausibly grants a bare tech-level bump with no
  attached component, consistent with this project's dynamic-string-table.md cataloguing "Mystery
  Trader encounters (technology, part, hull, or ship gifts..." as a similarly-shaped one-time-reward
  vocabulary, though a Mystery Trader link specifically was not traced this pass). *(Superseded by
  the random-event pass: bit 12 is the Mystery Trader's "auxiliary ships" item, not a tech bump;
  salvage never feeds it, see the Mystery Trader paragraph at the end of this section and
  `turn-generation-engine.md` §5/§5a.)*
- **`FUN_1118_1196` itself** (`stars.exe.export.c:115815`-onward) is independently documented in
  `fleet-movement-scanning-cargo.md` as "the per-race event bitmask/message-code dispatcher: it OR's
  a caller-supplied flag bit into a per-race 'already happened' bitmask and returns a message-code
  pair selected by which bit was set (12 distinct flag values handled, plus a fallback)" — the
  **12** matches this section's 12-row table exactly, independently corroborating both the bit count
  and the "one-time event, not a race trait" reading from a document written without reference to
  this one. That document's only traced caller for it was the (as since corrected: Mystery Trader encounter, see `turn-generation-engine.md` §5a and §9) wormhole heavy-cargo-transit routine
  (`FUN_1118_0784`, `stars.exe.export.c:115653`), meaning the *same* bitmask, dispatcher, and (at
  least some of) the same 12 message codes are reachable from at least two independent in-game
  triggers (battle victory and the Mystery Trader) — consistent with a general "one-time special
  reward, however it was earned" bookkeeping table rather than a single-source mechanic.

**Conclusion for item 2.** All 12 bits the ship-design system actually consults are now enumerated by
exact `(category, subtype)` and named component, closing the "only bits 2 and 9 have candidates"
starting point completely. The reason none of the 12 maps cleanly onto a PRT or LRT in
`race-traits.md`'s exclusive-component lists is that this mechanism is not PRT/LRT-gated at all —
it gates a fixed roster of 12 (of a possible 13) components behind a **one-time, per-race,
per-component random grant** earned during play (confirmed live for battle victories; also reached
through the Mystery Trader, which the earlier text called the wormhole-transit event, now traced in
`turn-generation-engine.md` §5a), tracked as "already granted" bits in this same 32-bit field so a
race can never win the same reward twice. The remaining 20 of the 32 bit positions are not read
anywhere by `FUN_10d8_4b8e`'s dispatch and were not found tested by any other function; whether they
are simply unused headroom in a 32-bit field sized generously for a smaller table, or gate additional
components this pass's search of `FUN_1008_5194`'s call sites did not surface, was not determined.

**Follow-up pass: every reference to this exact field anywhere in the decompile has now been
enumerated, not just `FUN_1008_5194`'s call sites, and the 20-bit gap remains unexplained by any
found code.** Rather than trusting the two access paths already traced, this pass grepped the whole
executable for the field two different ways: every literal occurrence of the folded constant
`0x5a14` (`player_index * 0xc0 + 0x5a14`, i.e. this exact `+0x52` byte offset from the per-player
record base), and every occurrence of the record-base symbol `DAT_1128_59c2` itself (234 call sites
in total, to catch any access written with the base symbol and an unfolded `+ 0x52` rather than the
compiler-folded literal). **The `0x5a14` search turns up exactly the sites already attributed above,
nothing new**: the zero-init at race-record creation (`stars.exe.export.c:50844`), `FUN_10d8_4b8e`'s
own 12-bit dispatch (`91908`), the battle-victory tech-gain roller `FUN_10f0_61a2` (`102999`), the
Mystery Trader encounter routine (formerly read as a wormhole heavy-cargo-transit routine) `FUN_1118_0784` (`stars.exe.export.c:115362`, `115747`,
`115759`, `115763` — read in full this pass rather than only via its call into the dispatcher below:
it independently rolls `FUN_1040_1652(0xd)`, a random value 0-12, and re-rolls until it lands on a
bit not already set in this exact field, i.e. it duplicates `FUN_10f0_61a2`'s "pick one of 13
candidate slots" logic inline rather than delegating to it; the Trader-side rules are in the
paragraph at the end of this section), and the generic per-race
event-bit-OR/message-code helper `FUN_1118_1196` (`stars.exe.export.c:115815`-`115883`, `115823`).
`FUN_1118_1196` itself was also read in full this pass: its first argument is an arbitrary
caller-supplied bit value that gets OR'd into the field unconditionally (`stars.exe.export.c:115824`)
regardless of whether the subsequent chain of tests recognizes it — so the
function *could* set any of the 32 bits if a caller ever passed one, but its own `if`-chain only
assigns distinct messages to bit values `0x02` through `0x800` (bits 1-11) plus an implicit bit-0
fallback, exactly matching the 12-row table above, and both of its only two callers
(`FUN_10f0_61a2`, `FUN_1118_0784`) only ever construct that argument from a 0-12 random roll — so bits
13-31 are never actually reached (bit 12 only through the Trader's computer-planet trade, see the
last paragraph of this section) even though the setter itself has no such restriction coded in. The
`DAT_1128_59c2` search (234 references) turned up no unfolded `+0x52`/`+0x29` access pattern anywhere
outside the `0x5a14` sites already listed — every other reference either scales by `* 0x60` (words)
or `* 0xc0` (bytes) into an entirely different field read through the shared per-race boolean-trait
accessors `FUN_10e0_222c`/`FUN_10e0_226e` (the first reads the signed-byte settings array at `+0x3e`,
whose byte 14 is the PRT; the second tests the lesser-trait word, an unrelated 32-bit field at record offset
`+0x4e`, immediately adjacent to but distinct from the `+0x52` ability mask — confirmed by reading
`FUN_10e0_226e`'s body directly, `stars.exe.export.c:93584`-`93591`: it tests a single bit of the 32-bit word at `+0x4e`, with the bit index confined to 0-31, which by construction never reaches byte `+0x52`),
or `* 0xc0 + 1` into a different single-byte field, or reference the fixed single-race global
`DAT_1128_59c2` byte 1 directly (unrelated UI/mode state). **No fourth reader or writer, and no
access to bits 12-31, exists anywhere in the executable by either search method.** This upgrades the
"was not determined" hedge to a considered negative: the remaining 20 bits are not merely unread by
`FUN_10d8_4b8e` specifically, they are not touched by any code this decompile contains, under either
of two independent, exhaustive search techniques. Whether they are simply unused headroom in a
32-bit field sized for a nominally-13-slot reward table, or gate content that exists only in a
version of the client not covered by this decompile, cannot be resolved by further static search of
this executable.

**Mystery Trader side of this field (random-event pass; full rules in `turn-generation-engine.md`
§5a).** `FUN_1118_0784` is the Mystery Trader encounter routine, not a wormhole routine.
- **What a Trader carries.** Each Trader carries exactly one value, drawn when it is created
  (`FUN_10b8_3976`): technology (0), or one of these 13 bits. In the Trader's context, bit 12
  (`0x1000`) is the "auxiliary ships" gift: 1-10 "M.T. Lifeboat", "M.T. Scout" or "M.T. Probe"
  ships, from built-in templates. Bits 8 and 10 (Mini Morph and Genesis Device), which salvage can
  never feed, are reachable here.
- **Human fleets.** When a human fleet is absorbed for a part, the grant goes through
  `FUN_1118_1196` (messages 267, 268 or 271). The ships gift does not set bit 12.
- **Computer planets.** A computer player's planet-side trade does set bits directly (`0x103a` in
  segment 36), **including bit 12** when the Trader carries the ships item. That is a way to reach
  bit 12 that the paragraph above missed.
- **Messages 265 and 266.** The Trader chooses between them by testing whether all 13 bits
  (`0x1fff`) are set.

### 15. The component data block located and extracted in full (resolves §11, §12, §13 and item 3/4)

This section supersedes §11's negative conclusion. The ~19.3 KB block described structurally there
has now been located in the binary and read out in its entirety: **all 16 categories, 239 records,
including every component's name, six tech-level prerequisites, mass, four-way cost, and
category-specific stats, plus every hull's complete slot layout.** The extracted dataset is stored
verbatim in `extracted-game-data/component-stats.tsv` (raw game data, deliberately outside this
clean-room folder per that folder's standing rules). This section describes the addressing fix, the
record format, and the field semantics.

#### 15a. The addressing fix, and the reusable method

The obstacle in §11 was the far pointer's segment half, the literal `0x10e0`, which §11 read as a
relocated selector naming NE segment 29 and then found to be too small to hold the table. **The
constant is not a data selector.** In the compiled code the resolver builds its far pointer by
pairing the computed offset with the *current code segment register* — the classic 16-bit pattern for
addressing constant data placed in the same segment as the code that reads it. The decompiler
rendered that register read as a literal, and the literal it chose happens to be the synthetic
selector of an unrelated segment, which is what sent §11 chasing segment 29. The resolver itself
lives in the segment this project calls segment 2; therefore **the component tables live in segment
2, alongside the resolver, and a segment-relative offset maps to a file offset simply by adding
segment 2's start**.

Five independent checks confirm this, and the method generalises:

1. **Re-derive the NE segment table from the raw executable**, scaling both the file-offset and
   length fields by the header's sector/alignment shift, and treating a zero length as 64 KB. This
   yields each segment's exact byte range.
2. **Calibrate the decompiler's synthetic segment numbering against those ranges empirically**,
   rather than assuming it. Collecting the maximum segment-relative offset the decompile ever
   mentions for each synthetic selector and comparing it with the corresponding segment's real
   length matched cleanly for all 38 segments, every one landing just inside its segment's end. This
   confirms the `0x1000 + (index − 1) × 8` selector convention *and* the file-offset mapping, and it
   is the check that should be run before trusting any decompiler-reported segment constant.
3. **Search the file for the table rather than trusting the pointer.** Because the category bases,
   strides and counts were already known from §4/§11, the block's shape is heavily over-constrained:
   sliding a candidate segment-start across the whole file and scoring how many of the 239 predicted
   record positions contain plausible small-integer tech-requirement bytes produced exactly one
   perfect-scoring candidate, and that candidate was precisely a segment boundary — segment 2's.
   This is the step that would have found the data even without understanding the pointer.
4. **Validate against outside knowledge.** The recovered records contain readable component names,
   and dozens of the recovered numbers match independently published figures for the same named
   components exactly (examples in §15d). No inference chain is load-bearing here; the data is
   self-identifying.
5. **Check the claim against the decompiler's own function map**, which turns out to be decisive.
   Ghidra identified exactly eleven functions in segment 2. One is a fifteen-byte accessor at offset
   zero; **every other one begins at or after `0x5118`** — which is precisely the byte at which the
   last category's table ends. **Not a single function lies anywhere inside `0x000f`–`0x5118`.** The
   segment's layout is therefore a tiny accessor, then a ~19.3 KB unbroken data block, then the rest
   of the code, and the data block's boundaries coincide exactly with the table extents derived
   independently from the resolver's own arithmetic. §11's contiguity finding (all 16 tables
   back-to-back with zero gap) is what makes the coincidence meaningful: the block is bounded on
   both sides by code, and its size matches to the byte.

**Reusable takeaway for other unresolved tables in this project** (including the hull-definition
table §13 flagged, which is simply two of the categories here): when a decompiled far pointer's
segment half is a bare literal with no backing data symbol, treat it as a possible code-segment-
relative constant-data reference rather than a failed relocation, and check the *reading function's
own* segment first. Combine that with a raw NE segment-table recomputation and, where the target's
shape is known, a constraint-driven scan of the whole file.

#### 15b. Universal record layout

Every record in all 16 categories shares the same leading layout, regardless of stride:

| Offset | Size | Field |
|---|---|---|
| `+0` | 2 | Per-category display ordinal (hulls and starbase chassis share one 0–36 sequence, per §13) |
| `+2` | 6 | Six tech-level prerequisites, one byte each, in `research-tech-tree.md`'s order: Energy, Weapons, Propulsion, Construction, Electronics, Biotechnology |
| `+8` | 32 | Component name, null-terminated within a fixed 32-byte field |
| `+40` | 2 | Mass (kT) |
| `+42` | 2 | Cost in resources |
| `+44` | 2 | Cost in ironium |
| `+46` | 2 | Cost in boranium |
| `+48` | 2 | Cost in germanium |
| `+50` | 2 | Icon/bitmap index for the designer and detail cards |
| `+52` | … | Category-specific stats; the record's stride is exactly `52 +` the space this needs |

All multi-byte fields are little-endian 16-bit. **This confirms §11's prediction exactly**: the
tech-level-shortfall check described there reads six consecutive bytes starting two bytes into the
record, which is precisely this six-byte prerequisite block. It also confirms this project's
established 31-character-plus-terminator name-field convention (`save-turn-file-format.md` §3) —
the field here is 32 bytes.

#### 15c. Category-specific stat fields

| Category | Fields from `+52` |
|---|---|
| `0x0001` Engines | `+52` special-behaviour selector (nonzero only for the six engines with special rules); `+54`…`+74` fuel usage indexed **by warp 0–10** (11 words); `+76` reserved |
| `0x0002` Ship scanners | `+52` scan range (ly); `+54` penetrating-capability class (0 = none) |
| `0x0004` Shields | `+52` shield points |
| `0x0008` Armor | `+52` armor points |
| `0x0010` Beam weapons | `+52` range; `+54` damage; `+56` initiative; `+58` fire mode (0 standard, 1 shield-sapper, 2 hits-all-targets) |
| `0x0020` Torpedoes / capital missiles | `+52` range; `+54` damage; `+56` initiative; `+58` base accuracy % |
| `0x0040` Bombs | `+52` constant 1; `+54` population kill rate in tenths of a percent; `+56` installations destroyed per bomb |
| `0x0080` Mining robots | `+52` mining rate (kT/year) |
| `0x0100` Mine layers | `+52` mines laid per year ÷ 10 |
| `0x0200` Stargates / mass drivers | `+52` max mass (gates; `0xFFFF` = unlimited) or warp speed (drivers); `+54` max range in ly (`0xFFFF` = unlimited), 0 for drivers |
| `0x0800` Electrical | `+52` the family's single stat — cloak units, accuracy bonus %, deflection %, or capacitor bonus %, depending on sub-family |
| `0x1000` Mechanical | `+52` the family's single stat — e.g. battle-movement bonus in quarter-points for the thrusters, deflection % for the beam deflector; for the colonisation/cargo/fuel sub-families it reads as a small sub-family code rather than a capacity, so those capacities appear to be applied in code, not stored here |
| `0x2000` Terraforming | `+52` maximum terraform amount |
| `0x8000` Planetary scanners / defenses | `+52` **signed**: positive = scanner range in ly; negative = a penetrating scanner whose range is the magnitude; for the five planetary defenses it is instead the per-unit coverage in hundredths of a percent |
| `0x0400` / `0x4000` Chassis and hulls | see §15e |

Two of these deserve a hedge. The engine `+52` selector takes a distinct small value on exactly the
six engines that carry special rules in the game (the Hyper-Expansion free-warp engine, the two
Improved-Fuel-Efficiency engines, the radiating ram scoop, and the two engines with unusual
high-warp behaviour) and zero on the other ten. **Its reader is now located, and it turns out to be
cosmetic, not a gameplay dispatch.** The field (values 1-6, one per special engine — Settler's
Delight=1, Radiating Hydro-Ram Scoop=2, Fuel Mizer=3, Galaxy Scoop=4, Interspace-10=5, Enigma
Pulsar=6) is read by the Ship/Starbase Designer's own component detail-card renderer
(`FUN_10d8_1e40`, segment 28), inside a `switch` selecting which explanatory footnote string to
display for that engine — i.e. it picks UI caption text, not a code path. Every one of the six
engines' actual special behaviors was independently confirmed to be implemented by its own hardcoded
subtype check elsewhere in the client, never consulting this field: Hyper-Expansion/Improved-Fuel-
Efficiency exclusivity via the PRT/ability-bit gates already documented above, the Radiating
Hydro-Ram Scoop's colonist-hazard flag via a literal subtype-10 check, Warp-10 safety via a hardcoded
five-subtype whitelist (which does not match this field's nonzero set, confirming the two are
unrelated), and ramscoop fuel behavior purely from zero-entries in the fuel-by-warp table itself. The
caption strings' own text was not recovered (this project's decoded string tables do not cover this
particular caption set). The scanner `+54` field is
tested in the consuming code only as "greater than zero", i.e. as a boolean "this scanner
penetrates"; its actual small values (1–4) distinguish classes. **Searched to a conclusion: there is
no separate penetrating-range table.** The scanner-combination consumer (`FUN_1038_337e`,
`stars.exe.export.c:19841`, called from `FUN_1038_32be`) was read in full: inside its per-slot
accumulation loop, a scanner's contribution to the combined range always uses the ordinary `+52`
range field, regardless of penetrating status — `+54` is read only once, as a `<1` threshold, and for
two specific subtypes (Pick Pocket Scanner and Robber Baron Scanner) bypasses the range math entirely
to set a small output flag instead, which looks like an unrelated resource-stealing ability bit (matching
those two components' names) rather than a range source. **Conclusion: a penetrating scanner's
detection range is simply its ordinary `+52` range value** — there is no separate, larger
"penetrating range" stored or computed anywhere in the exported client. This same function also
reproduces two already-documented mechanics end to end, now pinned to concrete code for the first
time: the No Advanced Scanners doubling (a race-trait-bit-10 check) and the Jack Of All Trades
built-in-scanner exception (a preload gated on PRT 9 for specific hull indices). Also recovered
incidentally from that same consumer: **ship scanner ranges combine as
a sum of fourth powers** across a design's scanners, matching the game's documented scanner-stacking
rule — the combination itself is `sqrt(sqrt(Σrange⁴))`, implemented as two chained FPU square-root
calls rather than a single fourth-root operation.

One observation offered without explanation: the `0x0200` stargate/mass-driver family's four cost
fields are uniformly **twice** the figures commonly published for those items, consistently across
all sixteen entries. Every other category's costs match published figures directly.

**Searched exhaustively and no halving found anywhere in the exported client.** The shared build-cost
routine (`FUN_1050_7d44`, `stars.exe.export.c:36923`) copies this category's four raw cost words
straight from the component record with no scaling, before applying two narrower, unrelated
conditional adjustments (a generic tech-level-over-requirement discount applying to all categories,
and a race/PRT-conditional ~25% reduction on Stargates specifically for Interstellar Traveler and on
a few other categories for other PRTs — see the "adjusted build cost" note below, which this same
trace resolved). Every other consumer of category `0x0200` (jump-range lookups, unrelated Windows
message-ID literals that happen to share the value `0x200`) was checked and does not touch cost at
all. **Conclusion: the stored values are used as-is everywhere in the client; whatever discrepancy
exists against commonly-published figures most likely means the community figures themselves are
already-halved (a display or strategy-guide convention), not that the game itself halves its own
stored data.**

#### 15d. Cross-validation

The extraction is validated well beyond internal consistency. Representative exact matches against
independently published figures for the same named components:

- **Tritanium**: mass 60 kT, 5 ironium + 10 resources, 50 armor points.
- **Superlatanium**: mass 30 kT, 25 ironium + 100 resources, 1500 armor points.
- **Mole-skin Shield**: mass 1 kT, 1 ironium + 1 germanium + 4 resources, 25 shield points.
- **Colloidal Phaser**: mass 2 kT, 14 boranium + 18 resources, damage 26, range 3, initiative 5.
- **Alpha Torpedo**: mass 25 kT, 9/3/3 minerals + 5 resources, damage 5, range 4, initiative 0,
  accuracy 35%.
- **Armageddon Missile**: damage 525, range 6, initiative 3, accuracy 30%.
- **Battleship**: mass 222 kT, fuel 2800, armor 2000, 11 slots.
- **Dreadnought**: fuel 4500, armor 4500, 13 slots.

Three structural predictions this project had already made independently also came out right:
§11's six-tech-prerequisite block; §13's claim that categories `0x4000` and `0x0400` are one unified
37-entry hull table (the extracted records confirm it, and their shared `+0` ordinal runs 0–36
across both); and §5's deduction that a particular Claim-Adjuster-exclusive entry sits at subtype 7
of category `0x0080` — that entry is the Orbital Adjuster. §5 and §14 had reasonable but wrong
*names* for two categories (`0x0040` is Bombs, not `0x0080`; `0x0080` is the mining-robot family),
while the PRT/trait reasoning attached to them was correct; those identifications are corrected in
§4's table above.

#### 15e. Hull records: the slot layout, resolving item 3 and item 4

The 143-byte hull/chassis records carry, after the universal header:

| Offset | Field |
|---|---|
| `+52` | Cargo capacity (kT); `0xFFFF` on the three largest starbase chassis, i.e. unlimited |
| `+54` | Fuel capacity |
| `+56` | Base armor points |
| `+58`…`+121` | The **slot array**: 4 bytes per slot — a 16-bit allowed-category bitmask, a reserved byte (always zero across all 37 records), and a one-byte slot capacity — terminated by a zero mask |
| `+122` | Slot count |
| `+123`… | Designer-diagram display metadata: two words, then one byte per slot that reads as a packed nibble-pair grid coordinate for that slot's box in the ship designer (the nibble reading is inferred from the byte count matching the slot count exactly in all 37 records, not confirmed against the rendered dialog) |

**This is the per-slot "allowed category mask" that §2 hypothesised, §12 correctly ruled out of the
`FUN_1070_2808` cache, and §13 predicted would live inside the hull record.** It is exactly the
bitwise-OR of category bits that §4 anticipated: a slot's mask is a subset of the 16 category bits,
and a component is legal in a slot when its single category bit is present in that mask. The
recurring masks are self-explanatory once read — a weapons slot is beam-plus-torpedo, a
general-purpose slot is the union of the eight ship-mountable families, the "scanner/electrical/
mechanical" and "shield/armor" combinations appear throughout, and engine, bomb, mining and
mine-layer slots are single-category.

The slot count declared at `+122` matches the array length in **all 37 records** with no
discrepancies, which is a strong self-check on the parse.

**Item 4 is now answered directly rather than by inference**: hull slot count genuinely varies by
hull type, from 2 (the colony ships, the small bombers, the miner and fuel-transport starters) up to
16 (the two largest starbase chassis), and both the number of slots and each slot's capacity and
allowed-category mask are per-hull data. Some concrete examples:

- **Scout** — 3 slots: engine ×1, scanner ×1, general-purpose ×1.
- **Destroyer** — 7 slots: engine ×1, weapon ×1, weapon ×1, general-purpose ×1, armor ×2,
  mechanical ×1, electrical ×1.
- **Battleship** — 11 slots: engine ×4, scanner/electrical/mechanical ×1, shield ×8, weapon ×6,
  weapon ×6, weapon ×2, weapon ×2, weapon ×4, armor ×6, electrical ×3, electrical ×3.
- **Nubian** — 13 slots: engine ×3 plus twelve identical general-purpose ×3 slots, which is the
  structural reason for that hull's reputation for total configurability.
- **Meta Morph** — 7 slots, all general-purpose, with capacities 3/8/2/2/1/2/2.

The fact that the Battleship's eleven slots are stored in a definite order, with the weapon slots
appearing as a 6/6/2/2/4 run, is also the most likely origin of the positional firing-order
behaviour reported for that hull in `combat-resolution.md` — see the note added there.

## Cross-references

- `client-ui-dialog-catalog.md` — "Ship and starbase designer" and "Slot/equipment editor"
  entries: this document is the mechanism behind both; see the cross-reference note added to that
  file's Slot/equipment editor entry.
- `research-tech-tree.md` — the "Full hull/component prerequisite table" entry (resolved: it
  summarizes the six prerequisite bytes of all 239 records recovered in §15, read straight from
  the executable, with no external source): §4–§6 of this document describe the
  **runtime code** that actually enforces category/subtype/race-trait legality at design-edit
  time, complementing that static table; see the cross-reference note added there.
- `race-traits.md` — §2 (PRT table) and §3 (LRT table) are corroborated in detail by §5 above (the
  Engine and Bomb category exclusivity examples); §7 (ship-design cost modifiers) is **confirmed this
  pass to be a genuinely different function from §8 above** (§8's `FUN_10f0_50e0`/`FUN_1038_2df8` pair
  turned out to be the same routine as this document's own §9 cache-populator, not §7's routine).
- `combat-resolution.md` — the per-design aggregate-stats function (§7) is the direct input to
  that document's battle-roster-construction mechanism; the min/max weapon-range-bracket tracking
  here is the same one referenced there.
- `production-queue.md` — no direct overlap; that document's cost-calculator branches (race-trait
  3-way index for Defenses/Terraforming base costs) were not found to reuse any of the machinery
  described here, and remain a separate, already-documented mechanism.
- `fleet-movement-scanning-cargo.md` — its "Per-race design/value and combat-power caches" note
  documents the segment-8 routines that populate and consume the §9 trailing cache fields.
- `research-tech-tree.md` — §11's identification of `FUN_1008_5916` as the per-component
  tech-level-shortfall check consumes that document's six-field tech-level ordering directly
  (Energy/Weapons/Propulsion/Construction/Electronics/Biotechnology); no change needed there, this is
  a one-way consumption.
- `combat-resolution.md` — **§15 closes that document's long-standing Open Question on canonical
  weapon/armor/shield numeric tables.** The full beam, torpedo, shield, armor and hull stat sets are
  now recovered; that document's worked examples have been rewritten around real values, and its
  energy-capacitor rate and Battleship slot-order questions are addressed there from §15's data.
- `save-turn-file-format.md` — §12's identification of `FUN_1040_35a4` as a nibble-oriented
  variable-width decoder is offered as a candidate for that document's §1/Open-Questions
  "compression/decompression step" for extended record types, not confirmed identical; and its §9
  (new) enumerates opcodes from the same decoder switch (`FUN_1048_68a8`) this document's §10 already
  cross-referenced for opcodes `0x1b`/`0x1e`.

## Open Questions / Uncertainties

- ~~Category `0x0800`'s subtype table (stride/base/count) was not traced in `FUN_1008_5194`
  itself~~ **Resolved in §11**: base `0x407e`, stride 54 bytes, 17 subtypes (`0`–`0x10`). Its
  real-world name is now confirmed by §15: **Electrical** (the "Ironium"-caption candidate of §7 is
  withdrawn).
- ~~No component category was matched to a definitive human-readable name beyond Engine
  (`0x0001`) and Bomb (`0x0080`)~~ **RESOLVED in §15.** All 16 categories are now identified by
  name, read directly from name fields inside the recovered records rather than inferred — see §4's
  table. No external source was needed; the names are in the
  executable's own data.
- ~~The exact hull-type "allowed category mask" per slot... was not decoded~~ **Corrected and
  redirected in §12/§13**: the 36-byte, per-race, 16-entry record reached via `FUN_1070_2808` is not
  a hull-defined mask at all — it is a UI-side cache of already-validated, already-placed slot
  contents (category/subtype/quantity header plus a cached 31-character display-name field),
  written only after legality has already been decided elsewhere. **RESOLVED in §15e**: the genuine
  per-hull allowed-category mask lives inside the 143-byte hull record exactly as §13 predicted, as a
  4-byte-per-slot array of (allowed-category bitmask, reserved byte, slot capacity).
- ~~Whether hull slot count truly varies by hull type... No hull definition record itself... was
  located~~ **RESOLVED in §15e**, no longer an inference from structure: slot counts are stored
  explicitly and range from 2 to 16 across the 37 hulls and chassis, with per-slot capacities and
  masks also varying per hull. **This directly contradicted §2's own original "up to 9 slots"
  heading/claim about the turn-file opcode `0x1e` component-slot writer, and that contradiction is
  now resolved by re-reading §2's own source material**: no field in opcode `0x1e` actually caps
  the number of occupied slots at 9 — the "0–9"-shaped figures there are two unrelated wire
  fields (a UI mount-group nibble capped at 6, and a per-slot quantity field capped at 8, matching
  real observed per-slot capacities such as Meta Morph's and the Battleship's capacity-8 slots).
  The real, and only, slot-count/index cap in that opcode is 0–15 (16 slots), which already
  matches the largest real hulls exactly. A Nubian-hulled design (13 slots) can genuinely have
  components installed in all 13 slots during normal gameplay; §2, §3, §7 and §9 have been
  corrected accordingly.
- ~~The per-component base-stat table's real on-disk location~~ **RESOLVED in §15**: the block lives
  in the resolver's own code segment (this project's segment 2), addressed relative to that segment's
  start. The `0x10e0` constant §11 chased is the decompiler's rendering of a code-segment register
  read, not a relocated data selector. All 239 records are extracted to
  `extracted-game-data/component-stats.tsv`.
- ~~The graded (not binary) compatibility feedback described in `client-ui-dialog-catalog.md`~~
  **Partially resolved in §11**: `FUN_1008_5194`'s own final step, `FUN_1008_5916` (segment 2, the
  same segment as the resolver itself — not the slot-editor dialog's segment as previously guessed),
  compares a component's own 6-byte tech-requirement block against the current player's tech levels
  and returns a graded result. The two gates in §5/§6 remain simple booleans as before; this is a
  third, separate mechanism, not a refinement of either of them. The display side is traced in
  `research-tech-tree.md` §4 (cost-to-reach figure from `FUN_10d8_4a86`, drawn by the shared
  detail-card renderer as one of four caption variants), so this item is closed.
- ~~**Whether the AI cost-estimator's race-trait-bit-12 transform (§8) is the same function as
  `race-traits.md`'s independently-documented "adjusted build cost for a hull design" routine**~~
  **RESOLVED this pass: no, they are different functions.** §8's estimator (`FUN_10f0_50e0`, segment
  `10f0`) delegates, when trait 12 is set, to `FUN_1038_2df8` (segment 8) — which is itself identified
  this pass as the same cache-populating routine already described in §9 below, not a bespoke
  ±25%-adjustment routine. Its actual logic (per-slot cost, mass and armour accumulation with a
  handful of category-specific rules, one of which halves the Armor-category part of the armour
  total for a fully specified design, detail level 7, of a Regenerating Shields race; see §8) does
  not match `race-traits.md` §7's described habitability-multiplier/±25%/doubling shape.
  `race-traits.md` §7 has since resolved its own side: no separate routine of that shape exists; the
  description was an early summary of the shared build-cost routine `FUN_1050_7d44`.
- ~~**Exact meaning of the min/max "level" value tracked in §7**~~ **RESOLVED in §7.** A full read
  of `FUN_10f0_27da` shows the 0–63-clamped pair is a **weapon-initiative** min/max bracket (the
  resolved component's own `+56` initiative field plus a per-design base byte), not a tech/mass
  tier, and the sibling nibble-pair tracker is confirmed to be a **weapon-range** min/max bracket
  (the component's `+52` range field). The initiative pair is read back by the battle engine's
  firing dispatcher (`FUN_10f0_5950`) to skip initiative brackets a design has no weapon in.
- **Armor+shield combined components' secondary stat — now fully resolved for all four.** §7 traces a concrete
  application site inside `FUN_10f0_27da` for two of the four combined items: Langston Shell
  (95%) and Mega Poly Shell (80%) each hard-code a per-unit percentage into the design's
  base-10000 "value" accumulator, feeding the cached defense-percentage byte at design offset
  `+0xb`. **The other two, found in a later pass in two different design-stat functions neither previously fully traced:** Croby Sharmor gives a flat **+65 points/unit** into a design-cached "effective armor" field (`FUN_1038_2df8`, design offset `+0x38`; Langston Shell is also given this same treatment there, redundantly), and Fielded Kelarium gives a flat **+50 points/unit** into a "total shield points" field (`FUN_1038_0a0e`, called from the very start of `FUN_10f0_27da`; Mega Poly Shell gets +100 there). All four combined items now have at least one traced, quantified secondary-stat application site. See `combat-resolution.md`
  §10c for the corresponding update.
- ~~**Ship scanners' penetrating-range numeric table.**~~ **RESOLVED in §15c, by a later pass than
  the one that wrote the note below.** This pass searched near the component resolver and the
  `FUN_10f0_27da` aggregate-stats function for a class-indexed lookup and did not find one there —
  that negative was correct as far as it went, but the actual answer turned out to be simpler and
  lived in the scanner-combination consumer instead (`FUN_1038_337e`): a penetrating scanner's
  detection range is just its ordinary `+52` range value: there is no separate, larger
  "penetrating range" stored or computed anywhere in the client. See §15c for the full trace,
  including the two Pick Pocket/Robber Baron Scanner subtypes that turned out to be an unrelated
  resource-stealing flag rather than a range exception.
