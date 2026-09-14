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
  and **fleet-meets-foreign-colony cargo exchange** (relevant to `combat-resolution.md`, not
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
each in its own separately-allocated per-race array. A design record holds up to **9 installed
component slots** (a per-design "how many are filled" counter plus a small fixed array), where
each installed entry packs a component's **category** (one of 16 fixed values, functionally a
single set bit of a 16-bit word), a **subtype index** within that category, and a **quantity**.
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
word (`(word >> 8) & 0x1f`, i.e. a 5-bit index, 0–31) and a race index (`(word & 0xf0) >> 4`,
0–15). If the design index is **below 16**, it addresses the race's **hull-design array** at
`base + index * 0x93`. If it is **16 or above**, it is rejected outright once it exceeds **25**
(`0x19`); otherwise it addresses a **separate starbase-design array** at
`base + index * 0x93 - 0x930` (i.e. `0x930 = 16 * 0x93`, renormalizing the starbase index back to
0–9 within its own array). Each race therefore has exactly 16 hull-design slots and 10
starbase-design slots, and every design record — regardless of hull or starbase — is exactly
147 bytes. A per-race in-use counter is adjusted by ±1 on create/delete, tracked in a separate
small per-race table.

### 2. Per-design installed-component list (up to 9 slots)

A design record carries a byte giving the number of occupied component slots (0–9) and, from that
count, a small fixed array of installed-component entries. Each occupied entry is validated and
written through the same decoder's component-assignment case (opcode `0x1e`, segment `1048`):

- A **mount/category-group nibble** (bits 0–3 of one input byte) must be **6 or less** (7 legal
  values) or the write is rejected.
- A second nibble in a following byte must be **8 or less** (9 legal values) or the write is
  rejected.
- A third value (the high nibble of that same byte) must be **0x80 or less**, i.e. one of the 9
  values `0x00, 0x10, 0x20, ... 0x80` — a quantity-like field expressed in steps of 16, capping
  the effective range at 0–8 in its own unit.
- Slot indices are filled **sequentially, not arbitrarily**: the write only proceeds if the
  supplied slot index equals a per-race "next free slot" counter (tracked in a small per-race
  table), which is then incremented; the index itself must not exceed 15.
- A separate bit in the input (`0x40`) distinguishes **assigning** a component to a slot (which
  calls into a per-hull "slot template" resolver and writer in a different segment, `FUN_1070_2808`)
  from **removing** one, which is handed off entirely to a function in the *other* segment in
  scope for this document — `FUN_10f0_0ff0` (segment `10f0`, i.e. Ghidra's "segment 31"). This is
  the one concrete, direct code-level link between the two segments this document was assigned to
  cover, confirming they are two parts of the same subsystem even though neither is individually
  "the" ship-design validator.

### 3. Removing a slot from an already-in-use design (fleet-consistency check)

Confirmed by inspection of the exported client (`FUN_10f0_0ff0`, segment `10f0`): before a slot
is actually removed from a design, this function walks **every existing fleet in the game**,
filters to fleets using this exact (race, design) pair, and compares the slot index being removed
against that fleet's own cached "components in this slot" count. If any matching fleet already
has that slot filled (i.e., ships already built with the design as it stands would be affected by
shrinking it), the function raises a confirmation prompt (a yes/no dialog, looked up by a string
ID and shown via a message-box helper) before proceeding; answering "no" aborts the whole
edit. If no existing fleet is affected, the removal proceeds silently. After removal, the
remaining installed-component entries are shifted down to close the gap (a 9-entry, 36-byte-stride
array), and the per-race "next free slot" counter (§2) is decremented.

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
ceiling** and its own fixed-stride, fixed-base lookup table (all in one shared static data area):

| Category (bit) | Subtype count | Record stride | Table base |
|---|---|---|---|
| `0x0001` | 16 | 78 bytes | `0x4c38` |
| `0x0002` | 16 | 56 bytes | `0x4630` |
| `0x0004` | 10 | 54 bytes | `0x4414` |
| `0x0008` | 12 | 54 bytes | `0x49b0` |
| `0x0010` | 24 | 60 bytes | `0x2728` |
| `0x0020` | 12 | 60 bytes | `0x2cc8` |
| `0x0040` | 15 | 58 bytes | `0x2f98` |
| `0x0080` |  8 | 54 bytes | `0x3a60` |
| `0x0100` | 10 | 54 bytes | `0x3c10` |
| `0x0200` | 16 | 56 bytes | `0x000f` |
| `0x0400` |  5 | 143 bytes| `0x05db` |
| `0x0800` | (not fully traced here — see §5 and Open Questions) |
| `0x1000` | 11 | 54 bytes | `0x3e2c` |
| `0x2000` | 20 | 54 bytes | `0x32fe` |
| `0x4000` | 32 | 143 bytes| `0x1548` |
| `0x8000` | 15 | 54 bytes | `0x3736` |

A "category" value outside this list, or a subtype index at or beyond its category's ceiling,
resolves to a plain "does not exist" result. This is the concrete structure underlying the
assignment's hypothesized "nibble-masked category checks" — the check is real, but it lives one
segment away from where it was expected, and the mask is 16 bits wide (one full word), not a
4-bit nibble.

**A slot's "allowed types" field is therefore almost certainly a bitwise-OR of a subset of these
16 category bits** (letting a hull slot accept, e.g., both a Weapon-family and a Shield-family
component), while an installed *component itself* always carries exactly one of the 16 values.
This document did not locate and decode a hull's per-slot "allowed category mask" record content
directly (see Open Questions) — only its existence, indexing, and 36-byte stride (§2 above,
referenced via the `FUN_1070_2808` slot writer).

### 5. Per-subtype race-trait/PRT gating, embedded directly in the resolver

Beyond the category/subtype-count structure, `FUN_1008_5194` hard-codes specific
**PRT-exclusivity and boolean-trait exclusions per (category, subtype) pair**, returning a
distinct "not available to this race" result (as opposed to "does not exist"). Two fully-traced
examples, cross-checked against `race-traits.md`'s existing PRT/LRT tables and found to match
exactly:

- **Category `0x0001` (16 subtypes) reads as the Engine family.** Subtype 0 requires PRT id 0
  (Hyper Expansion) — matching race-traits.md's HE-exclusive "Settler's Delight" free Warp-6
  engine. Subtypes 10–15 (6 engines) are blocked outright if a specific boolean race-trait flag is
  set — matching the "No Ram Scoop Engines" LRT's "all free-fuel ram scoop engines above Warp 4
  become unavailable" (exactly the ram-scoop-family engine range). Subtypes 2 and 15 (2 engines)
  additionally require a *different* boolean race-trait flag — matching Improved Fuel Efficiency's
  "unlocks the Fuel Mizer and Galaxy Scoop engines" (exactly 2 named exclusive engines); engine 15
  sitting in both ranges is consistent with Galaxy Scoop being IFE's ram-scoop-type engine.
- **Category `0x0080` (8 subtypes) reads as the Bomb family.** Subtypes 0, 2, 3, 4, and 5 (5
  bomb types) are blocked if a specific boolean race-trait flag is set — matching Inner Strength's
  documented exclusion of exactly 5 named bomb types ("Smart/Neutron/Enriched-Neutron/
  Peerless/Annihilator"). Subtype 7 requires PRT id 3 (Claim Adjuster) — matching Claim Adjuster's
  documented exclusive "Retro Bomb."

**Also confirmed:** the PRT-id values checked throughout this resolver (0, 2, 3, 4, 5, 6, 7, 8) are
internally consistent with `race-traits.md`'s existing PRT ordering (Hyper Expansion=0, War
Monger=2, Claim Adjuster=3, Inner Strength=4, Space Demolition=5, Packet Physics=6, Interstellar
Traveller=7, Alternate Reality=8), independently corroborated here by the two exact
name/count matches above and by this same PRT-index convention recurring throughout the segment
`1048` habitability/growth cluster (where PRT 8/Alternate Reality and PRT 9/JOAT already receive
distinct code paths, per `population-growth.md`'s and `race-traits.md`'s existing material).

Every other category follows the identical pattern (a handful of hard-coded subtype-vs-PRT-id or
subtype-vs-boolean-flag checks); this document did not attempt to map every remaining subtype to a
named component for all 16 categories — see Open Questions.

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
touching the resolver's per-subtype logic for every LRT combination. Both this gate and the §5
per-subtype checks feed into the same single "is this component legal for this race" boolean
consumed by the slot editor.

**Cross-reference to `client-ui-dialog-catalog.md`'s Slot/equipment editor entry:** that entry's
existing note that "compatibility checking is graded, not a simple yes/no" (fully available / one
tech level away / needs multiple fields raised / unavailable for other reasons) is **not fully
explained by anything found in segments 10 or 31**. Both gates described here (§5, §6) are binary
(available/unavailable) with no partial state. The tech-level-distance grading the UI displays
must be computed from a separate tech-level-vs-required-level comparison this document did not
trace to a specific function — most plausibly consuming the tech-level field of the same
component-definition record this resolver returns a pointer to, but that comparison itself lives
outside segments 10/31 (see Open Questions).

### 7. Per-design aggregate-stats computation

Confirmed by inspection of the exported client (`FUN_10f0_27da`, segment `10f0`): given a
resolved design record, this function walks its up to 9 occupied component slots, resolves each
via the same shared component lookup as §4–§6 (with the owning race set as lookup context), and
accumulates several running totals depending on the resolved category/subtype:

- A **base-10000 "value" multiplier**, progressively divided down by a fixed percentage
  contributed by specific subtypes of category `0x0800` (a category this document did not fully
  map into the §4 table — see Open Questions). At the end it is converted into a **0–95-range
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

This is the concrete "armor/mass/cost aggregation across installed components" mechanism the
original assignment asked about. Its general shape (accumulate percentage-style multipliers per
category, track min/max brackets, cap and rescale at the end) is confirmed with high confidence;
which specific named stat (armor vs. shield vs. cloak vs. jamming vs. fuel tank) each accumulator
ultimately represents was **not** confirmed, since no accompanying string/label table survives in
this decompile to anchor category `0x0800`'s and `0x1000`'s subtypes to real component names. This
matches and extends the existing, less-detailed characterization of this same function noted for
`combat-resolution.md`'s battle-roster construction.

**Candidate identification for category `0x0800`, found while tracing segment 28's shared detail-card renderer (`client-ui-dialog-catalog.md`'s "Search and record browser" section).** That renderer (`FUN_10d8_1e40`) has its own dedicated, 17-subtype-wide display branch for category `0x0800`, entirely separate from this section's aggregate-stats accumulator. One of that branch's caption paths references a literal **"Ironium"** string (a mineral name, not a generic label) when formatting a subtype's detail line, which points to `0x0800` being a mineral/mining-flavored component family — most plausibly the Mining Robot components gated by the Advanced Remote Mining LRT referenced in §6 above. Offered as a strong candidate, not a confirmed name, since the remaining 16 subtypes' captions were not individually decoded.

### 8. AI/production-side design costing

Two further design-related helpers were found in segment `10f0`, both used by the AI production
planner (see `combat-resolution.md` and the AI-behavior material there for how they're consumed,
not repeated here):

- A **design cost/tier comparator**, used purely to establish a sort order between two designs
  (e.g. "which of these two designs is more advanced/expensive"), with no gating behavior of its
  own.
- A **cost estimator gated by a race-trait boolean flag** (trait index 12 in the per-race trait
  table this document observed elsewhere): when set, a copy of the design's per-slot cost data is
  passed through an additional transform (in a different segment) before being used to estimate
  build cost; when clear, the raw per-slot costs are used directly. This is consistent in shape
  with `race-traits.md`'s independently-documented §7 finding of "a separate general-purpose
  compute-adjusted-build-cost routine... flat roughly-±25% adjustments keyed to specific hull-type
  categories crossed with specific race-trait checks" — **plausibly the same underlying mechanism
  approached from the AI-costing side here and from the race-wizard side there, but this was not
  confirmed to be literally the same function** (see Open Questions).

### 9. Cached per-design value and combat-power fields (owned by segment 8, not 10/31)

Confirmed by inspection of the exported client: beyond the up-to-9-slot component list (§2), each
147-byte design record carries a small trailing block of **cached, recomputed-on-demand summary
fields** that are populated by routines living in a different segment (Ghidra prefix `FUN_1038_`,
this project's segment 8) rather than by the aggregate-stats function in §7. One cached field holds
a general per-design "value" figure (a weighted sum over the design's category-`0x0010`/`0x0020`/
`0x0040` components, further scaled by a category-`0x0800`-driven percentage and by a segment-`10f0`
helper distinct from §7's), recomputed for every non-deleted design of every race in one pass. A
further, separate cached field (or small set of fields) holds a randomized combat-power estimate,
a range-like value, a defense percentage, and a weapon-type bitmask, normally computed on demand by
walking the design's weapon/shield-like components but readable directly from this cache under a
global mode flag instead of being recomputed. See `fleet-movement-scanning-cargo.md`'s "Per-race
design/value and combat-power caches" note for the full behavioral description of the routines that
populate and consume these fields — this document only records their existence and location within
the record layout established in §1–§2.

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

## Cross-references

- `client-ui-dialog-catalog.md` — "Ship and starbase designer" and "Slot/equipment editor"
  entries: this document is the mechanism behind both; see the cross-reference note added to that
  file's Slot/equipment editor entry.
- `research-tech-tree.md` — the "Full hull/component prerequisite table" discussion (its
  `TECHITEM.DOC`-derived static per-level unlock table): §4–§6 of this document describe the
  **runtime code** that actually enforces category/subtype/race-trait legality at design-edit
  time, complementing that static table; see the cross-reference note added there.
- `race-traits.md` — §2 (PRT table) and §3 (LRT table) are corroborated in detail by §5 above (the
  Engine and Bomb category exclusivity examples); §7 (ship-design cost modifiers) is plausibly the
  same mechanism as §8 above, not confirmed identical.
- `combat-resolution.md` — the per-design aggregate-stats function (§7) is the direct input to
  that document's battle-roster-construction mechanism; the min/max weapon-range-bracket tracking
  here is the same one referenced there.
- `production-queue.md` — no direct overlap; that document's cost-calculator branches (race-trait
  3-way index for Defenses/Terraforming base costs) were not found to reuse any of the machinery
  described here, and remain a separate, already-documented mechanism.
- `fleet-movement-scanning-cargo.md` — its "Per-race design/value and combat-power caches" note
  documents the segment-8 routines that populate and consume the §9 trailing cache fields.

## Open Questions / Uncertainties

- **Category `0x0800`'s subtype table (stride/base/count) was not traced in `FUN_1008_5194`
  itself** — its subtype behavior is known only indirectly, through how `FUN_10f0_27da` (§7)
  consumes its resolved records (percentage-style shield/mass-like effects on subtypes 4, 8–15).
- **No component category was matched to a definitive human-readable name beyond Engine
  (`0x0001`) and Bomb (`0x0080`)**, which were inferred with high confidence from exact
  count/PRT matches against `race-traits.md`. The other 13–14 categories (Armor, Shield, Weapon
  variants, Scanner, Mine Layer, Mechanical, Electrical, Orbital/Mining, hull types themselves,
  etc.) were not individually identified — this would need either string/resource data not present
  in this decompile, or a much larger cross-reference pass against `components.xml`/`TECHITEM.DOC`
  the way `research-tech-tree.md`'s own prerequisite-table work already did for tech levels.
- **The exact hull-type "allowed category mask" per slot** (the 36-byte-stride, up-to-9-entry
  per-hull template referenced via the slot-assignment writer in §2) was confirmed to exist,
  confirmed to be indexed per-race and per-slot, and confirmed to be 36 bytes per slot — but its
  internal field layout (which bits/bytes encode the allowed-category mask, a maximum quantity, or
  anything else) was not decoded. This is the single most valuable next target for a follow-up
  pass, since it would settle whether slot capacity/allowed-mask genuinely varies by hull (the
  assignment's original question) or whether all hulls share one universal 9-slot template with
  only the *count* of active slots varying by hull.
- **Whether hull slot count truly varies by hull type**, beyond the fixed ceiling of 9 slots per
  design observed structurally in every function that iterates a design's component list. No hull
  definition record itself (hull mass, base armor, per-hull slot count) was located within segments
  10 or 31 — hull-level base stats most likely live in a different, unexamined segment.
- **The graded (not binary) compatibility feedback described in `client-ui-dialog-catalog.md`**
  (exact-match / one-level-away / multi-field-away / unavailable-for-other-reasons) was not
  resolved to a specific function within segments 10 or 31; the two gates found here (§5, §6) are
  both simple booleans. The actual tech-level-distance computation likely lives in the slot-editor
  dialog's own segment, not examined as part of this pass.
- **Whether the AI cost-estimator's race-trait-bit-12 transform (§8) is the same function as
  `race-traits.md`'s independently-documented "adjusted build cost for a hull design" routine** —
  both apply race-trait-conditional percentage-style adjustments to a design's cost, but they were
  found from two different segments in two different investigation passes and were not confirmed
  to be the same code.
- **Exact meaning of the min/max "level" value tracked in §7** (clamped 0–63, derived from a
  component's own level field plus a per-design base value) — read as plausibly a tech-tier or
  mass-class range, not confirmed against a named stat.
