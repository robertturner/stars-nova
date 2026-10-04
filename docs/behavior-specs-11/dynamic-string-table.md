# Dynamic String Table Specification

This document covers the exported client's mechanism for storing and retrieving all player-facing
dynamic text (race/tech/component names, victory-condition labels, and most dialog label text that
is not fixed at layout time — see `client-ui-dialog-catalog.md`'s note on the Lesser Racial Traits
and victory-conditions wizard pages, whose control labels are confirmed to be populated this way
rather than from static dialog resources). It is derived from decompiled control-flow logic for a
single shared lookup routine, cross-referenced against the exported client's real on-disk data
layout. Unlike the MENU/DIALOG findings elsewhere in this project (recovered as plain, uncompressed
resource text), the mechanism here is a proprietary compression scheme. **Message-log body text
uses a second, separate table built from the identical mechanism but with its own base offsets and
alphabet, documented in §6** — an earlier revision of this paragraph listed "message bodies" as
covered here; that turned out to be wrong (see §6.1) and has been corrected.

> **Status: SOLVED — all three tables.** The mechanism is now fully resolved and all three tables
> have been decoded. The single blocking gap described in earlier revisions of this document (§3,
> "what the accumulated sum indexes") turned out to be an *addressing* mistake, not a missing
> algorithm: all of this mechanism's tables live in the **lookup routine's own code segment**, not
> in the automatic data segment every previous pass searched. See §3. Decoding now reproduces all
> nine independently-verified known (identifier, string) pairs byte-for-byte, and yields **1,392
> populated strings across 1,414 identifier slots (identifiers 0-1413)**. A second, structurally
> identical table feeding message-log body text (colonization outcomes, minefield damage,
> random-event flavor text, and more) was subsequently located and decoded too — see §6 — yielding a
> further **387 populated strings across identifiers 0-386**, with complete coverage and no gaps. A
> third, structurally identical table feeding the client's built-in default object-name pool
> (the `<name> <number>` planet-naming convention) was located and decoded in a later pass — see
> §7 — yielding **999 populated strings across identifiers 0-998**, again with complete coverage and
> no gaps.

## Overview

Nearly every dynamically-labeled piece of UI text in the client is produced by one shared lookup
function taking a single numeric string identifier and returning a pointer to a decoded, human-readable
byte string in a shared, non-reentrant static buffer (the routine is called from 324 distinct call sites
spanning nearly every major UI and game-logic segment, confirming it is the general-purpose text-lookup
path, not a narrow one-off helper). It also short-circuits: if asked for the same identifier it decoded
last, it returns the existing buffer contents without re-decoding. Valid string identifiers run from 0 to 1413 (see §3.1),
and the lookup is **paged**: an identifier's high bits select a 64-entry page and its low 6 bits select
an entry within that page.

## Mechanics

### 1. Paging and length lookup — confirmed

- A string identifier splits into a **page index** (identifier divided by 64) and a **within-page index**
  0-63 (identifier modulo 64).
- Each page has a fixed-size, 64-byte **length table**: entry *i* holds the number of **nibbles**
  (4-bit units, not characters) needed to encode within-page string *i*. A zero entry means "no string
  at this slot."
- The nibble offset where a given string's encoded data begins, within its page's shared nibble stream,
  is the running sum of every preceding entry's nibble-length in that same 64-byte table.
- A separate, 2-bytes-per-page **page pointer table** supplies, per page, an additional offset added
  when locating that page's own nibble stream (allowing pages to store their stream data at different
  base locations rather than assuming one contiguous region for the whole table).

### 2. Nibble stream decoding — confirmed

- The encoded nibble stream is read in pairs per byte, high nibble first, then low nibble, advancing
  the byte pointer only after consuming a low nibble.
- Nibbles accumulate into a running sum. A nibble value of 15 (0xF) is a **continuation marker**: it
  adds 15 to the accumulator and continues reading without emitting output. Any other nibble value (0-14)
  adds to the accumulator, then **terminates the current character's code** — the final accumulated sum
  is used to emit one output byte, and the accumulator resets to zero for the next character.
- This is a variable-length-per-character scheme: a common character can be encoded as a single nibble
  (fast path), while emitting a character whose lookup value exceeds 14 requires one or more `0xF`
  continuation nibbles first. The accumulated sum is not masked or wrapped by the code, so it is
  formally unbounded; in practice the table's own data never drives it above 83, the last index of the
  84-entry alphabet it feeds (§3.2). Earlier revisions treated the absence of a mask as evidence that
  the lookup target must be larger than an alphabet — that inference was wrong, and the bound is simply
  enforced by the encoder rather than by the decoder.
- Decoding stops after the page's per-string nibble-length (from §1) has been fully consumed; the output
  buffer is then null-terminated.

### 3. What the accumulated sum indexes — RESOLVED

The final per-character accumulated sum is a **direct index into an 84-entry character table**
(a frequency-ordered substitution alphabet), not into a "shared reference pool". Both the earlier
"fixed 256-entry alphabet" reading and the later "shared reference pool" hypothesis were wrong,
and so was the assumption that the missing data was materialised at runtime.

**The single error that blocked every previous pass was the segment, not the algorithm.** The table
reads in the lookup routine carry a segment override that selects the **routine's own code segment**
rather than the program's automatic data segment; the decompiled output silently omits that override,
so every table offset in this document (0xfb, 0x14f, 0x17d, 0x703) reads in the decompile as though it
were a data-segment offset. Resolving those same four offsets against the *code* segment that hosts the
lookup routine (the segment already described elsewhere in this project as carrying the client's
low-level text/window-class plumbing) makes the whole structure fall out immediately and consistently.
This also explains, retroactively and completely, every symptom documented in earlier revisions: the
"genuine but semantically unrelated" help-file name / window-class-name / format-string block found at
data-segment offset 0xfb is real content that simply has nothing to do with this mechanism, and the
page-pointer values that read as zero or as fragments of that text were never page pointers at all.
The §1/§2 mechanics were correct as written throughout.

Only one output-side address in the routine genuinely *is* data-segment relative: the shared,
non-reentrant decode buffer the routine returns a pointer to. That mixture of one data-segment address
and four code-segment addresses inside a single routine is what made the decompiled output look
internally consistent while being wrong about four fifths of its own memory references.

#### 3.1 Layout of the table's host code segment — confirmed

All four structures are packed contiguously at the very start of the code segment, immediately behind
the lookup routine's own body, with no padding of consequence:

| Offset | Structure | Size |
| --- | --- | --- |
| 0x000 | the lookup routine's executable body | ~251 bytes |
| 0x0fb | **character table** (substitution alphabet) | 84 bytes |
| 0x14f | **page pointer table**, 2 bytes per page, 23 pages | 46 bytes |
| 0x17d | **length tables**, 64 bytes per page | see below |
| 0x703 | **nibble stream base** | remainder of the segment |

The packing is deliberately tight to the point of overlapping. The character table ends exactly where
the page pointer table begins; the page pointer table ends exactly where the length tables begin; and
the length tables would need 23 x 64 = 1,472 bytes to cover all 23 pages, which would run 58 bytes past
the nibble stream base. The final page's length table is simply **truncated in place** — only its first
six entries exist, and the bytes that would have held entries 6-63 are the opening bytes of the nibble
stream itself. This is not a bug: the final page only holds six strings, so the author let the two
structures butt into each other to save space. The practical consequence is that the table's valid
identifier range is **0 to 1413 inclusive** (22 full pages of 64, plus 6), and identifiers at or above
1414 read length bytes out of the nibble stream and decode to garbage rather than failing cleanly.

#### 3.2 The character table — confirmed

The 84-byte table at code-segment offset 0xfb is an ordinary substitution alphabet ordered by
descending frequency in the corpus it encodes, which is exactly what makes §2's variable-length scheme
pay off: index 0 (the space character) and the common lowercase letters cost a single nibble, while
rare characters cost one or more 0xF continuation nibbles first. In order (84 characters, index 0 is
the space character; read each row left to right, the first column giving the index range the row
covers):

| Indices | +0 | +1 | +2 | +3 | +4 | +5 | +6 | +7 | +8 | +9 | +10 | +11 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0-11 | (space) | `e` | `a` | `t` | `i` | `o` | `r` | `n` | `s` | `l` | `h` | `d` |
| 12-23 | `u` | `c` | `p` | `m` | `y` | `g` | `f` | `.` | `b` | `%` | `v` | `w` |
| 24-35 | `T` | `S` | `:` | `'` | `k` | `P` | `M` | `R` | `Y` | `0` | `C` | `A` |
| 36-47 | `D` | `F` | `,` | `W` | `x` | `*` | `E` | `I` | `B` | `N` | `-` | `q` |
| 48-59 | `H` | `L` | `O` | `1` | `U` | `G` | `2` | `5` | `9` | `V` | `?` | `(` |
| 60-71 | `)` | `z` | `/` | `!` | backslash | `J` | `3` | `j` | `4` | vertical bar | `6` | `K` |
| 72-83 | `Z` | `Q` | `&` | `<` | `>` | `8` | `#` | `X` | `7` | `;` | `=` | `@` |
 A decode sweep of the entire table confirms the **maximum
accumulated sum actually used anywhere in the table is 83** — precisely the last entry — so the
"unbounded accumulator" noted in §2 is bounded in practice by the table the author sized to fit it,
and the earlier inference that an unbounded accumulator implied something larger than an alphabet was
a false lead.

#### 3.3 Validation against known-good output — confirmed

The nine independently-recovered (identifier, string) pairs described in earlier revisions of this
document (the Waypoint Task list, obtained by querying a live client's own dropdown control and
cross-checked against a real fleet's on-screen order — see `fleet-movement-scanning-cargo.md`) now
reproduce **exactly, all nine, with no adjustment to §1 or §2**: identifiers 100-108 decode to
"Transport", "Colonize", "Remote Mining", "Merge with Fleet", "Scrap Fleet", "Lay Mine Field",
"Patrol", "Route" and "Transfer Fleet". The decode additionally recovers the one member of that run
whose text had never been captured — identifier 99, the list's "no task" placeholder entry, which
reads `(no task here)` — and identifier 109, an adjacent `(no action)` placeholder, neither of which
was used to derive the decode and both of which are consistent with the run's established shape.

#### 3.4 Independent confirmation from a live, running instance — confirmed

Because earlier revisions' leading hypothesis was that the real table content was materialised only at
program startup, the resolution was cross-checked against a **live, running instance of the client**
under a Win16 emulation layer on a modern host. A process handle was obtained with ordinary read-only
debugging APIs, the running process's committed memory regions were enumerated, and both the automatic
data segment and the table-hosting code segment were located by matching against byte sequences whose
static file content was already known. Results:

- The table-hosting code segment's live image is **byte-for-byte identical to its shipped file image**.
  Nothing about this mechanism is constructed, decrypted or relocated at runtime; the complete string
  table ships fully-formed in the executable.
- The automatic data segment's live image differs from its file image in only about 270 of its 8,850
  file-backed bytes, and **none of those differences fall in the 0xfb-0x703 range** this document
  previously believed to hold the tables. The static window-class names and format strings found there
  are still intact in the running program, confirming they are that region's real and only purpose.

This **retires §4's "runtime-only region" theory for this mechanism specifically** (see §4 below).
The runtime-only region described there is real and does exist, but it is not where this table lives;
the only part of this mechanism that touches it is the decode output buffer.

#### 3.5 Relationship to the other nibble codec — unchanged

The separate, independently-implemented nibble-packing codec documented elsewhere in this project
(the word-wrap text layout engine and the default planet-name generator) remains a genuinely different
scheme, as earlier analysis concluded: it treats the accumulated value as a small categorical
discriminator backed by several tiny tables, whereas this mechanism performs a single unconditional
lookup in one flat 84-entry alphabet. The two share only the house convention of emitting the high
nibble of each byte first. Nibble-based text packing is confirmed as a recurring technique in this
codebase, but neither codec's tables unlock the other's.

**Resolved: `FUN_1040_1a78` *is* the default planet-name generator; the "categorical discriminator"
attribution above was a conflation with an unrelated function, not a second generator.** §5.5 (and
now §7, its fully-decoded successor) identifies `FUN_1040_1a78` (`stars.exe.export.c:21857`-`21929`)
as a third instance of *this* document's alphabet-lookup mechanism (paged length/pointer tables,
`0xF`-continuation nibble stream, flat alphabet), whose callers and behavior (word-initial
capitalization, a 999-entry range) matched the "default object/planet name lookup" role this
paragraph attributes to a categorical-discriminator-based codec instead. That match is now
confirmed rather than merely circumstantial, on two independent legs:

1. **`client-interface.md`'s own, separately-written "Default planet-name table" paragraph**
   describes — without naming a function — "a dedicated decompression routine [that] takes a
   numeric index (wrapped into a fixed range of roughly a thousand entries) and unpacks one entry
   from a compact, variable-width-nibble-encoded name table into a plain capitalized string
   (capitalizing the first letter and the first letter after any internal space or hyphen)," whose
   caller appends "a small numeric suffix" to build the `<name> <number>` default-planet-name
   convention. That is a precise structural description of `FUN_1040_1a78` and no other function in
   this project (999-entry modulo range, word-initial capitalization after space/hyphen, nibble
   decompression) — it was written independently of this document and did not know its offsets, but
   it is unambiguously describing the same routine.
2. **Call-site tracing confirms the planet connection directly.** `FUN_1040_1a78`'s caller
   `FUN_1038_1d12` (`stars.exe.export.c:18474`-`18516`) is itself called at
   `stars.exe.export.c:38612`, inside `FUN_1058_0b46`'s planet-iteration loop — the same loop
   `client-interface.md`'s "planet-name label toggle" paragraph documents as drawing each planet's
   on-map name label. The decoded, capitalized name this call produces is copied into a shared
   global name buffer and handed straight to the label-drawing routine `FUN_1040_1f3e` two lines later.

So the two were never independent mechanisms for the same purpose: **there is exactly one default
object/planet-name generator, and it is the alphabet-lookup table documented in full in §7 below.**
The "categorical-discriminator" codec this paragraph originally described (shared with the word-wrap
text layout engine) is real and still a genuinely different scheme from §1-§2's — but it is not the
default-name generator; an earlier pass appears to have conflated the two before either was traced
to concrete code. This paragraph is left in place, corrected, as a record of that conflation rather
than silently rewritten.

### 4. Storage location — confirmed, and rules out three alternative theories

- **The tables live in the code segment that hosts the lookup routine, not in the automatic data
  segment** (see §3). That segment is roughly 30 KB of file-backed, read-only-in-practice content, and
  the string table occupies essentially all of it beyond the routine's own body: the nibble stream runs
  from the segment's 0x703 mark to within a few bytes of its end. Only the decode *output* buffer is
  data-segment relative, at a fixed offset inside the automatic data segment's zero-filled runtime range.
- **The "runtime-only region" theory recorded in earlier revisions is withdrawn for this mechanism.**
  That region is real — the automatic data segment carries only 8,850 bytes of file-backed content while
  the executable header additionally specifies an 8,192-byte local heap and a 16,384-byte stack appended
  at load time, so a substantial part of its runtime address range genuinely does not exist in the file.
  But nothing about the string table is stored there. The earlier evidence pointing that way (per-page
  pointer values apparently landing inside the runtime-only range) was an artefact of reading the pointer
  table at a data-segment offset where it does not exist; those bytes were unrelated static content being
  misread as pointers. Direct comparison of a running instance's automatic data segment against the
  shipped file (§3.4) confirms the 0xfb-0x703 range of that segment is *unchanged* at runtime.
- **Two further alternative storage theories were tested and ruled out.** Earlier analysis speculated that the
  bulk of this dynamic text might instead live in one of three large custom-type resources found in the
  executable's resource table (roughly 4,300 / 51,800 / 19,900 bytes). Direct inspection of those three
  resources' raw bytes shows each begins with an identical, structured, repeating record header
  containing an incrementing per-record counter — a pattern consistent with a proprietary packed
  image/sprite container (plausibly ship, planet, or portrait graphics), not a string table, and
  inconsistent with the length-table/nibble-stream shape described above. A second, separate group of
  six mid-sized resources (roughly 12-44 KB each) was also considered; the exported resource table's own
  name records resolve this group's *type* to an explicit internal label of **"WAVE"**, confirming these
  are packaged sound-effect audio files, not text. Neither resource group is the dynamic string source.
  *(Corrected by §8: the three custom resources are neither graphics nor compressed. They are the
  tutorial's pre-made game files, and the "incrementing counter" is the turn number in each file's
  header record.)*

## 5. Recovered content — inventory

A full sweep of identifiers 0-1413 decodes **1,392 populated strings**; the remaining 22 slots carry a
zero nibble-length. Those 22 are not reserved or retired entries: they all fall inside one narrow
stretch (identifiers 632-672) that holds the client's credits block, and they are the block's
deliberate blank spacer lines between contributor groups. In other words the table has **no unused
slots at all** below its 1,414-entry ceiling. Every decoded string is plain printable ASCII — no
embedded control bytes, no multi-byte encoding, and no evidence of a second decoding layer.

This document specifies the mechanism, not the client's text assets, so the table's contents are
characterised here by *region and purpose* rather than transcribed. A handful of short strings are
quoted only where they serve as evidence.

### 5.1 Identifier-space map

Identifiers are grouped by purpose in broadly contiguous runs, aligned loosely (not strictly) to the
64-identifier page boundaries:

| Identifiers | Content |
| --- | --- |
| 0-63 | File-I/O and startup error/warning messages (corrupt or missing universe, log, history, host and turn files; out-of-memory; version mismatch) |
| 64-127 | Per-component availability and effect explanations (shield/armour/bomb/stargate/hull notes, primary-racial-trait gating text) |
| 128-191 | Production-queue item names (planetary installations, terraforming and alchemy actions, the four mineral-packet kinds), immediately followed by the client's own settings-file section and key names |
| 192-255 | More settings keys and font-resource names, then component-effect explanation text |
| 256-383 | Longer rules-explanation prose: mining and minefield rules, and the descriptive text for primary and lesser racial traits |
| 384-447 | Hull role classification labels (warship, utility, bomber, miner, fuel transport) and assorted confirmation/warning messages |
| 448-511 | Bombing and planetary-damage explanation text |
| 512-575 | Tutorial prompts and the "zip order" transport-order help text |
| 576-631 | Sentence fragments assembled at runtime into population/support and habitability messages |
| 632-703 | The credits block: contributor names, company names, section headings and blank spacer lines |
| 704-831 | Remainder of the credits, then cost/hull summary format strings |
| 832-1023 | Ship-design, production and fleet-panel column and field labels |
| 1024-1087 | The built-in/default ship design names |
| 1088-1151 | Component category names (armour, beam weapons, bombs, and the rest of the technology-browser categories) |
| 1152-1279 | Fleet/planet report column headings, battle-report labels and custom-order UI labels |
| 1280-1343 | Minefield and map-overlay legend labels, plus battle-plan tactic/target labels |
| 1344-1413 | Miscellaneous short labels, then the built-in race names |

Two identifier clusters flagged in earlier revisions as worth a follow-up live-UI query are now
resolved directly and needed no such query:

- **0x4c-0x52 (76-82)** are the research-screen summary row labels — resources needed to complete,
  estimated time to completion, annual resources from all planets, resources spent on research last
  year, resources budgeted for research, next year's projected budget, and next field to research.
  The earlier guess that these were "ship/component design summary" labels was close but wrong; they
  belong to the research-allocation screen.
- **0x234-0x23f (564-575)** are the "zip order" help panel: four order-preset names each paired with a
  one-line description, plus a four-line instructional paragraph stored as four separate identifiers
  in **reverse display order** (the last line has the lowest identifier). That reversal is worth noting
  as a general caution — multi-line prose in this table is not guaranteed to be stored in reading order.

### 5.2 Substitution markers inside decoded strings

Decoded strings are templates, not finished text. Two distinct substitution conventions appear:

- Ordinary C-style format specifiers (`%d`, `%s`, `%c`, `%ld`, `%02d`, `%04d`, `%%`), by far the more
  common, used wherever the client formats the string through its normal printf-style path.
- A second, client-private convention of a **backslash followed by a single lowercase letter**, used
  exclusively in the file-related messages in the 0-63 range, where the letter selects which of the
  game's file kinds to name: observed markers cover the game/current file, the turn file, the player
  log, the host file, the universe-definition file and the history file. These are presumably expanded
  to a real filename by a wrapper around the lookup rather than by the lookup itself, which performs no
  substitution of any kind.

### 5.3 Examples (illustrative only)

Short, representative entries used as decode evidence (described by purpose; only game-vocabulary
names are given as-is):

| Identifier | What it decodes to |
| --- | --- |
| 76 | the first research-screen summary row label (the resources still needed to finish the current level), ending in two trailing spaces |
| 100 | the Transport waypoint task name (one of the nine pre-verified pairs) |
| 306 | the lesser racial trait name Improved Fuel Efficiency |
| 307 | the lesser racial trait name Total Terraforming |
| 1088 | the component category name Armor |
| 1089 | the component category name Beam Weapons |

### 5.4 What this unblocks elsewhere in this project

The trait, technology, component-category, race and victory-condition names previously recorded as
unrecoverable in `race-traits.md`, `race-designer-ui-and-availability.md`,
`ship-design-and-components.md` and `client-ui-dialog-catalog.md` are all present in this table and can
now be resolved by identifier. The per-component explanation strings stating that a part needs a particular primary racial
trait in the 64-127 and 320-383 ranges are a particularly dense source of
availability-gating facts, since each one names both the component and the trait that gates it, and
those pairings can be cross-checked against the gating logic already documented from the decompile.

## 5.5 A third compressed string table, found — mechanism confirmed (historical record; fully decoded in §7)

**Status: SUPERSEDED — see §7 for the full decode.** This section is left as-written below as the
historical record of how the table was first located from the decompile alone, before `stars.exe`
was available in the project's working directory. A later pass located the executable (at
`C:\Downloads\Games\Stars\stars.exe`, one directory above where this project's other files live —
see §7's own note on that), applied the identical extraction method used for §1-§4 and §6, and
decoded the table's full 999 entries. Read on for the mechanism-identification writeup, then see §7
for the decode itself, the recovered content, and the reconciliation with §3.5's "categorical
discriminator" false lead.

**Status: EXISTENCE CONFIRMED, content not decoded [as of the pass that wrote this section].** A targeted search for the same structural
shape elsewhere in the decompile (paged 64-byte length tables, a per-page pointer table, a
nibble-stream decoder with the `0xF` continuation marker feeding a fixed-offset alphabet lookup — the
exact control-flow fingerprint of §1-§2's mechanism) turned up a **third, independent instance**,
distinct from both tables above in every one of its base offsets, its character alphabet, and its
identifier range.

**Location.** `FUN_1040_1a78` (`stars.exe.export.c:21857`-`21929`, segment `1040` — this project's
general-purpose-helper segment, home to e.g. the shared random-integer helper
`FUN_1040_1652` cited throughout `combat-resolution.md`). Structurally identical decode loop to §1/§2
and to §6 below (paging by identifier modulo 64 for the within-page index and identifier divided by
64 for the page, a running per-page byte-length sum, a page-pointer-table lookup, then the same high-nibble-first nibble stream with `0xF` as a
continuation marker accumulating into a per-character sum used as an alphabet index), packed
contiguously in its own code segment in the same tight, overlapping style as the first two tables:

| Structure | Offset (code-segment-relative) |
|---|---|
| character table (substitution alphabet) | `+0x38` |
| page pointer table | `+0x6c` |
| length tables (64 bytes/page) | `+0x8c` |
| nibble stream | `+0x473` |
| decode output buffer | a fixed buffer in the automatic data segment |

**A distinctive, explicit identifier-range clamp not present in either of the first two tables.**
Immediately on entry (`stars.exe.export.c:21872`-`21874`), any identifier above 998 is replaced by
its remainder after division by 999 — i.e. the valid identifier range is **0-998 (999 entries)**,
enforced by an explicit modulo rather than the "ceiling, garbage beyond it" behavior §3.1/§6.2 document for the
first two tables. This is worth flagging on its own: 999 is the well-known size of *Stars!*'s
built-in default-object-name pool, a strong circumstantial match for this table's purpose before its
callers are even considered.

**A decode-time post-process not present in either of the first two tables.** Where §1-§2's and
§6's decoders copy each decoded alphabet character straight to the output buffer, this one tracks
word boundaries and **uppercases the first letter of each word** (`stars.exe.export.c:21914`-`21922`:
a lowercase letter immediately following a space, a hyphen, or the start of the string is
capitalized; every other character is copied verbatim) — i.e. the decoder itself produces
Title-Case output. This is the signature of a **name table** rather than a UI-label or
message-template table (neither of which would need automatic capitalization baked into the decoder
itself).

**Callers confirm a default-name lookup/search role.** Every call site
(`stars.exe.export.c:18486`, `41797`, `41811`, `112246`, `112378`) decodes an entry by numeric index
out of a small per-race or per-object index array (a global array) and string-compares the
result against player-supplied or stored text — the shape of "does this object's current name match
(or already equal) one of the built-in defaults," not a display-only lookup. This is consistent with
*Stars!*'s well-documented default planet-naming pool, though this pass did not trace far enough to
confirm the specific object kind (planet vs. some other default-named object) with certainty.

**Why this wasn't found by earlier passes' resolution method for tables 1/2.** Both earlier tables
were physically decoded by locating their host code segment's real file offset and reading the raw
bytes (§3's segment-relative-addressing fix, reused verbatim for §6). That method requires the
executable itself; **this project's working directory does not currently contain `stars.exe`** (only
the exported decompile, the Ghidra project files, and previously-extracted data tables), so the same
byte-level extraction could not be attempted this pass. The table's existence, mechanism, exact
structure offsets, and identifier count are established beyond reasonable doubt from the decompiled
control flow and the explicit range/capitalization logic alone; decoding its actual 999 entries is a
mechanical follow-up (identical procedure to §3's, given access to the binary) rather than an open
analytical question.

**Answer to the open question this resolves.** `dynamic-string-table.md`'s prior Open Questions
entry asked "whether any *further*, third compressed table exists elsewhere in the client... was not
investigated in this pass." It does: a third table, of the same general design but serving what is
almost certainly the client's default-object-name pool (999 entries, auto-capitalized), sitting in
segment `1040` at the offsets above. Full content extraction remains for a future pass with binary
access.

## 6. A second, separate compressed string table for message-log body text — SOLVED

**Status: SOLVED.** The client's message-log body text (colonization outcomes, minefield and
mineral-packet damage notices, random-event flavor text, battle-outcome summaries, Mystery Trader
encounters, and the copy-protection tamper/hacked-race notices) does **not** come from the table
described in §1-§5 above. It comes from a second, independent lookup routine, reached from the
message-record store's own text-resolution path (documented in `client-ui-dialog-catalog.md`'s
"Messages" section) rather than from any of the 324 call sites that feed the general-purpose
table. Cross-referenced with `turn-generation-engine.md`'s Open Questions, which first identified
this second routine's existence without decoding it.

### 6.1 Relationship to the first table

This is **the same algorithm reused with a different base, not a new scheme.** Every mechanic in
§1 (paging, per-page 64-byte nibble-length table, per-page 2-byte pointer table) and §2 (nibble
stream decoding, the 0xF continuation marker, accumulation into a per-character sum) applies
unchanged. The only differences are the four base offsets the routine adds to its own paging math,
the size of the pointer/length-table region those offsets carve out (hence a different valid
identifier ceiling), and the content and size of the character alphabet the final sum indexes into.
The two tables share no bytes, no alphabet entries, and no identifier space — an identifier valid in
one table means nothing in the other.

### 6.2 Location — confirmed

The lookup routine lives in the **message-record store's own code segment** (the 26-function
segment `code-coverage-report.md` documents as hosting the message-record header, append, and
read-bitmap logic — reached from the message store's text-resolution path, i.e. the routine chain
that turns a message record's stored type code into displayable text). Exactly as with the first
table, the four table offsets this routine's own paging math depends on read in the decompile as
plain integer literals added to a computed pointer, with no recognizable data-segment symbol behind
them — the same **CS-relative addressing idiom Ghidra silently drops**, pointing into the reading
routine's own code segment rather than the automatic data segment, that was the single blocking
error for the first table (§3) and, separately, for an unrelated component/hull stat table
documented elsewhere in this project. Locating this segment's real file position and reading the
four offsets directly against it (rather than against any data segment) reproduces perfectly
formed, coherent message text immediately, with no further trial and error — a third confirmation
of the same toolchain-level trap.

Packed contiguously, in the same tight, deliberately-overlapping style as the first table (§3.1):

| Structure | Size |
| --- | --- |
| character table (substitution alphabet) | 72 bytes |
| page pointer table, 2 bytes per page, 7 pages | 14 bytes |
| length tables, 64 bytes per page | truncated (see below) |
| nibble stream | remainder of the segment |

The length-table region has room for exactly 6 full 64-entry pages plus a 7th page truncated to
its first 3 entries before the nibble stream begins — the same "final page's length table butts
into the nibble stream" overlap the first table exhibits (§3.1), just at a much smaller scale. This
puts the table's valid identifier range at **0 to 386 inclusive** (387 slots: 6 full pages of 64,
plus 3). Identifiers at or above 387 read length bytes out of the nibble stream itself and decode
to visible garbage — a clean, sharp boundary that was used as the range check for the full sweep
below.

### 6.3 The character table — confirmed

The alphabet is 72 characters (smaller than the first table's 84, consistent with these being
longer-run message sentences drawn from a narrower, more repetitive vocabulary than the first
table's mixed labels/prose/credits). In frequency order, index 0 being the space character (read
each row left to right, the first column giving the index range the row covers):

| Indices | +0 | +1 | +2 | +3 | +4 | +5 | +6 | +7 | +8 | +9 | +10 | +11 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0-11 | (space) | `e` | `o` | `t` | `a` | `s` | `n` | `i` | `r` | `l` | `d` | `h` |
| 12-23 | backslash | `u` | `c` | `p` | `f` | `y` | `b` | `m` | `.` | `g` | `v` | `w` |
| 24-35 | `k` | `,` | `Y` | `T` | `0` | `'` | `A` | `z` | `P` | `M` | `S` | `X` |
| 36-47 | `F` | `x` | `O` | `I` | `j` | `%` | `U` | `V` | `L` | `-` | `C` | `D` |
| 48-59 | `E` | `N` | `!` | `G` | `H` | `q` | `*` | `W` | `(` | `)` | `2` | `5` |
| 60-71 | `:` | `Q` | `R` | `1` | `B` | `/` | `4` | `6` | `Z` | `7` | `8` | `?` |

Notably, this alphabet includes a literal backslash as its 13th-most-frequent character — expected,
since message bodies are templates that use the client's private backslash-letter escape convention
documented in `client-ui-dialog-catalog.md`'s "Escape-coded report-text formatting" section far more
densely than the general-purpose table's file-message templates (§5.2) do.

### 6.4 Validation against known-good ground truth — confirmed

The decode was checked against message-type codes already documented by identifier elsewhere in
this project, all independently recovered from call-site literals in the decompile rather than from
this table:

- Waypoint-task codes already fully verified against a live client (§3.3 above, and
  `fleet-movement-scanning-cargo.md`'s Transfer Fleet / Scrap Fleet / Merge with Fleet sections):
  identifier 0xf7 (247) decodes to the Merge with Fleet notice (a fleet-name escape, then a sentence
  saying that fleet was merged into a second, escape-substituted fleet); identifier 0x5b (91) decodes
  to the Scrap Fleet notice (two leading escapes, then a sentence saying the fleet was dismantled and
  its scrap left in deep space);
  identifiers 0x14a/0x14d/0x14e (330/333/334) decode to the three Transfer Fleet gift-outcome
  messages (recipient lacks capacity, success, and the receiving side's own notice).
  Identifiers 62/63 decode to the two production-queue-empty notices.
- The two copy-protection notices `turn-generation-engine.md` and `client-ui-dialog-catalog.md`
  reference only obliquely (per this project's standing rule never to document the validator's own
  algorithm — see below) resolve to plain, unremarkable user-facing text: identifier 279 tells the
  player that their own race definition was found to be tampered with and that its statistics have
  been adjusted back into line with the rules, and identifier 386 (the table's last populated slot)
  announces that a hacked race (named through a race escape code) was discovered and that its
  statistics were likewise adjusted. These are ordinary notification strings the message store displays; nothing
  about how the validation check itself works is disclosed or discussed by decoding them, consistent
  with this project's house rule.
- Random-event and minefield-notification codes already characterized structurally (not by exact
  text) in `turn-generation-engine.md`: identifier 382 (a fleet was unable to lay mines this year
  because of a technical malfunction) and identifier 218 (a planet was wiped out by an incoming
  mineral packet, killing all the player's colonists there) both decode to messages consistent with the fleet/planet "gated
  race" effects and mineral-packet-collision mechanics that document already describes structurally.

Every one of these independently-sourced identifiers decodes to plain, grammatically complete,
contextually appropriate English — full confirmation before the broader sweep below.

### 6.5 Full sweep — 387 strings recovered, complete coverage

A sweep of the entire valid range (identifiers 0-386) decodes **387 populated strings — every
slot, with no gaps at all**, unlike the first table's 22 deliberate blank credits-spacer lines.
Every decoded string is plain printable ASCII (plus the backslash escape character used by the
formatting convention above); none were left as unresolved garbage. The full set is written to
`extracted-game-data/message-strings.txt` (same tab-separated identifier/text format as
`extracted-game-data/dynamic-strings.txt`), and `extracted-game-data/README.md` documents it
alongside the existing entry.

The recovered range covers: colonization outcomes (successful, contested, and various failure
modes — no colonists, already populated, uninhabitable), ground-combat and orbital-battle
narration across many participant-count and casualty combinations, population loss/growth
notices, fuel and waypoint-tracking notices, cargo transfer (success/partial/failure, for both
minerals and colonists), starbase/factory/mine/defense construction notices, bombing outcomes
(with and without planetary defense mitigation, across several casualty/installation-loss
combinations), remote mining and terraforming notices, minefield lay/sweep/collision outcomes
(for both the player's own and foreign minefields), stargate transit outcomes (including the
several severity tiers of ships lost in transit), Mystery Trader encounters (technology, part,
hull, or ship gifts, and several refusal/failure variants), mineral-packet launch/capture/
collision outcomes, comet-impact events at four size tiers (with and without colonist deaths),
victory/defeat/elimination announcements, and the two copy-protection notices discussed above.
This is a substantially complete inventory of the game's turn-generation message vocabulary, not
a sample.

### 6.6 What this unblocks elsewhere in this project

This closes the specific open item recorded in `turn-generation-engine.md`'s Open Questions (the
paragraph noting that message body text needed "a second decode pass ... against this other
table"), and gives `client-ui-dialog-catalog.md`'s "Messages" section — which previously described
the message-record store's structure but could not show what any message actually says — concrete,
verified example text. It also independently corroborates several mechanics documented by structure
alone elsewhere: the "gated race" fleet/planet degradation effects, the mine-laying/mineral-packet
technical-malfunction random events, and the minefield collision/sweep notification pairing
(own-field vs. foreign-field variants) in `turn-generation-engine.md`.

## 7. A third, separate compressed string table for the default object/planet-name pool — SOLVED

**Status: SOLVED.** §5.5 located this table's mechanism, offsets and identifier range from the
decompile alone but could not decode its content because `stars.exe` was not present in this
project's working directory at the time. A later pass found the executable — not at this project's
own root, but one directory up, at `C:\Downloads\Games\Stars\stars.exe` (a plain NE-format Windows
3.x binary, 3,153,152 bytes, dated April 2000; this project's working directory holds the Ghidra
project and exported decompile derived from it, not the binary itself) — and applied the identical
segment-relative extraction method used for §1-§4 and §6 to decode the table in full: **999
populated strings across identifiers 0-998, complete coverage, no gaps.**

### 7.1 Relationship to the first two tables

Like §6, this is **the same algorithm reused with a different base, not a new scheme.** Every
mechanic in §1 (paging, per-page 64-byte nibble-length table, per-page 2-byte pointer table) and §2
(nibble stream decoding, the 0xF continuation marker, accumulation into a per-character sum) applies
unchanged. Two things are genuinely new relative to both earlier tables, and both are documented
structurally in §5.5 from the decompile alone, then confirmed here against the real bytes:

- An explicit **modulo-999 wrap** applied to the raw identifier before paging begins
  (`stars.exe.export.c:21872`-`21874`), rather than the "ceiling, garbage beyond it" behavior of the
  first two tables.
- A **decode-time word-initial capitalization** pass (`stars.exe.export.c:21914`-`21922`): a
  lowercase letter immediately following a space, a hyphen, or the start of the string is
  uppercased; every other character is copied verbatim. Neither of the first two tables' decoders
  does this.

The three tables share no bytes, no alphabet entries, and no identifier space.

### 7.2 Location — confirmed against the real executable

The lookup routine, `FUN_1040_1a78` (`stars.exe.export.c:21857`-`21929`), lives in the code segment
Ghidra's synthetic selector `0x1040` names. Using this project's already-established selector
convention (selector = hex 1000 plus 8 times one less than the segment index, validated across all 38 segments in
`ship-design-and-components.md` §15a and re-confirmed here — see below), selector `0x1040` is
**NE segment 9**. Re-deriving the NE segment table directly from the executable's MZ/NE headers
(scaling the segment table's offset and length fields by the header's sector/alignment shift, `6`
for this binary) places segment 9 at **file offset `0x2b180`, length `0x4de2`** — the same parsing
this project's earlier passes used for the first two tables (segment 3, file offset `0x8c40`, hosts
§1-§4's `FUN_1010_0000`; segment 38, file offset `0xb0bc0`, is the automatic data segment `0x1128`;
both reproduced exactly from a fresh parse, which is the calibration check for this pass).

Reading from segment 9's real file bytes, the four structures §5.5 placed by decompiled offset
line up exactly, and their sizes are self-confirming — each structure's real byte extent equals the
gap to the next structure's start, with no slack:

| Structure | Segment-relative offset | Real file offset | Size |
| --- | --- | --- | --- |
| character table (substitution alphabet) | `+0x38` | `0x2b1b8` | 52 bytes (`0x6c − 0x38`) |
| page pointer table, 2 bytes/page | `+0x6c` | `0x2b1ec` | 32 bytes = 16 pages (`0x8c − 0x6c`) |
| length tables, 64 bytes/page | `+0x8c` | `0x2b20c` | 999 bytes (`0x473 − 0x8c`) |
| nibble stream | `+0x473` | `0x2b5f3` | remainder of the segment |
| decode output buffer | fixed buffer in the automatic data segment | data segment | — |

The length-table region's size is a clean, independent confirmation of the 999-entry range on its
own: 16 pages of up to 64 one-byte length entries each would need 1,024 bytes, but only 999 are
present before the nibble stream begins — exactly one byte per valid identifier (15 full 64-entry
pages, i.e. identifiers 0-959, plus a 16th page truncated to its first 39 entries, covering
identifiers 960-998), the same "final page's length table butts into the nibble stream" packing
convention §3.1 and §6.2 both describe, just landing on a total that happens to equal the modulo
bound exactly rather than needing it stated separately.

### 7.3 The character table — confirmed

The alphabet is 52 characters — smaller than either earlier table's (84 and 72), consistent with a
vocabulary of short single- or two-word proper nouns rather than sentences. In frequency order,
beginning with `e`, not a space (the only one of the three tables whose most frequent character
isn't the space, because these entries are short names, not prose, and rarely have leading spaces;
the space is index 17). Read each row left to right, the first column giving the index range the
row covers:

| Indices | +0 | +1 | +2 | +3 | +4 | +5 | +6 | +7 | +8 | +9 | +10 | +11 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0-11 | `e` | `a` | `r` | `o` | `n` | `i` | `l` | `s` | `t` | `u` | `d` | `c` |
| 12-23 | `h` | `m` | `p` | `g` | `b` | (space) | `y` | `k` | `w` | `f` | `v` | `z` |
| 24-35 | `x` | `j` | `q` | `'` | `-` | `1` | `0` | `2` | `3` | `9` | `M` | `4` |
| 36-47 | `5` | `G` | `6` | `C` | `8` | `A` | `O` | `S` | `V` | `7` | `B` | `D` |
| 48-51 | `F` | `I` | `P` | `R` | | | | | | | | |

A decode sweep of the entire valid range confirms the maximum accumulated sum actually used
anywhere in the table is 51 — the alphabet's last entry — with no value going unused and none
running past the table, the same tight-fit pattern §3.2 and §6.3 document for the other two tables.
The alphabet is a mix of lowercase letters (for ordinary name characters, capitalized at decode time
per §7.1), digits, and a handful of punctuation marks (space, hyphen, apostrophe) — no format
specifiers or backslash-escape characters appear anywhere in it, consistent with these being
finished display names rather than templates.

### 7.4 Validation against known-good output — confirmed

There is no independently-published, per-identifier ground truth for this table the way the
Waypoint Task list (§3.3) or specific message codes (§6.4) provided for the first two tables. The
validation here is instead the content's own internal shape, which is unambiguous:

- **Every one of the 999 decoded entries is a short, plausible name** — ordinary place, person,
  mythological and word names, alongside a handful of recognizable pop-culture in-jokes built from
  famous numbers (a film-spy code number, an emergency phone number, a well-known postal code, a
  directory-assistance phone number) at the low, numeric-looking end of the range — never garbled
  fragments, repeated substrings, or non-printable output, across the
  entire range.
- **The list is alphabetically sorted end to end**, identifier 0 through 998 (after the numeric-
  looking joke entries at the very start), which is exactly the shape a hand-authored default-name
  roster would have and not something a decode of the wrong bytes would ever coincidentally produce.
- **No duplicates and no empty slots** anywhere in the 999-entry range — every identifier decodes to
  a distinct, non-empty string, matching the explicit modulo-999 wrap (§5.5, §7.1) exactly: the
  author sized the table to precisely the range the clamp enforces, with nothing left over.
- **The call chain independently confirms the real-world role.** `FUN_1040_1a78`'s caller
  `FUN_1038_1d12` (`stars.exe.export.c:18474`-`18516`) is itself called at
  `stars.exe.export.c:38612`, inside the map-rendering routine `FUN_1058_0b46`'s planet-iteration
  loop — the same loop `client-interface.md` documents as drawing each colonized planet's on-map
  name label — and the result is hand off directly to the label-drawing call two lines later. A
  second, independently-written paragraph in `client-interface.md` ("Default planet-name table")
  describes this exact mechanism (nibble-decompressed, ~1,000-entry range, word-initial
  capitalization, a numeric suffix appended by the caller) without naming a function or offsets,
  having been written from a different angle of approach; see the reconciliation in §3.5 above.

Taken together — coherent, sorted, complete, duplicate-free output from a decode whose structure is
self-confirming, produced by a routine directly wired into the game's on-map planet-name-label path
— this is conclusive without needing an external ground-truth list.

### 7.5 Full sweep — 999 strings recovered, complete coverage

A sweep of the entire valid range (identifiers 0-998) decodes **999 populated strings — every slot,
with no gaps at all**, the same complete-coverage shape §6.5 found for the message-body table (in
contrast to the first table's 22 deliberate blank credits-spacer lines). Every decoded string is
plain printable ASCII: lowercase-and-uppercase letters, digits, spaces, hyphens and apostrophes only
— no embedded control bytes, no format specifiers, no backslash escapes. The full set is written to
`extracted-game-data/default-names.txt` (same tab-separated identifier/text format as
`extracted-game-data/dynamic-strings.txt` and `extracted-game-data/message-strings.txt`), and
`extracted-game-data/README.md` documents it alongside the existing two entries.

Across the range, the entries run alphabetically from the numeric joke names at identifier 0,
through names beginning with A in the first few dozen identifiers, to names beginning with Z in the
last few; roughly, identifiers near 100 begin with B, near 300 with F, near 550 with M, near 800
with S and near 900 with T. The roster is not transcribed here (see `extracted-game-data/default-names.txt`).
The mix of straight place-name-style words with a visible streak of wordplay, puns and pop-culture
jokes (including the cluster of famous-number names at the very start) matches the game's
well-documented, slightly tongue-in-cheek default-planet-naming convention.

**Availability and use (gap-report answer).** The roster is not missing: `extracted-game-data/default-names.txt`
is present in the delivered data set and holds all 999 names, one per line as identifier (0-998), a tab and the
name, sorted alphabetically (it starts with the numeric joke names and ends with Zulu). How the game uses it
(`FUN_1078_1334`, `stars.exe.export.c:50450`-`50465`): when a galaxy is generated, each planet in turn draws a
uniform random index 0-998; if that index is already used by an earlier planet it steps forward one at a time,
wrapping to 0, to the first unused index (a "used" bit set is kept for the duration of the loop). In a
tutorial game the wrap happens one step later, at 1,000, a quirk that is harmless because the tutorial galaxy is
small. The chosen index is stored per planet (the per-planet word table at data offset `0x278e`; in the game files
it is packed above six other bits of a planet word, `:44104`) and the name is decoded from the roster on demand
(`FUN_1040_1a78`, the identifier taken modulo 999, first letter and every letter after a space or hyphen
capitalised). So planet names are unique within a galaxy.
A reimplementation can load the file directly and reproduce the selection with its own random generator.

### 7.6 What this unblocks elsewhere in this project

This closes §5.5's original gap (full decode blocked by lack of binary access) and the corresponding note in
this document's Open Questions. It also resolves §3.5's open cross-check: `FUN_1040_1a78` is
confirmed to be the same default planet-name generator `client-interface.md` independently
identified by mechanism without pinning it to a function, not a second, coexisting generator — see
the reconciliation written into §3.5 above. `client-interface.md`'s note that "the actual roster of
names could not be recovered, only the unpacking mechanism" is superseded: the roster is now fully
recovered, in `extracted-game-data/default-names.txt`.

## 8. The three custom resources (types 10000, 10002, 10004): the tutorial's pre-made game files, not strings — SOLVED

The resource inventory (`client-interface.md`) lists three user-defined resources that earlier passes
called compressed blobs or a packed graphics container (§4). Both readings were wrong. **None of the
three is compressed and none holds text.** Each is a run of complete game files in the ordinary
record format of `save-turn-file-format.md` §1 and §10, stored back to back and copied to disk as-is.
The client contains no string-table resource, so the three nibble tables (§1-§7) remain the only
compressed text in the executable.

### 8.1 Container format

- **Byte 0** is a count: the number of files in the resource.
- **Then the files**, one after another. Each is a normal record stream: a 2-byte record header (type
  in the top 6 bits, payload length in the low 10 bits) followed by the payload. Each file starts with
  a type-8 header record carrying the file magic, game id, version, turn, player and salt, and ends
  with a type-0 end record.
- **Then a terminator**: one more type-8 header word with nothing after it but the resource's zero
  padding.

There is no compression layer: no dictionary, no back-references, no Huffman or nibble coding. The
only transformation is the standard record cipher of `save-turn-file-format.md` §10.4. Every payload
except types 0 and 8 is XORed with the two-generator keystream, which is reseeded at each file's header
record. Because every embedded file carries its own header, the bytes can be written out unchanged and
the result is a valid enciphered file. Deciphering every payload with that rule gives well-formed
records throughout. For example, the player records come out at the exact lengths the packer of
`save-turn-file-format.md` produces (130 or 123 bytes for a full record, 17 for a partial one), with
the player index in byte 0. That confirms both the format and the cipher. *(Correction for
`save-turn-file-format.md` §10.4: the 64-entry seed table is not in the data segment. The seeding
routine reads it from its own code segment, segment 9 (`1040`), offset `0x1476`. The table holds small
primes. The data-segment bytes at that offset are unrelated.)*

**How the code walks a resource.** Both loaders find the resource by type and id, lock it, and read
the count byte. To reach file *n*, they step from record to record using each header's length field.
Every time they meet a type-8 header they count one file, and they stop at the *n*-th. Records are
never parsed in place. They are either copied to a file or fed to the orders loader as if read from
disk.

### 8.2 The three resources

| Resource (type / id) | Size allocated / used | Count byte | Files | File kind (`save-turn-file-format.md` §10.3) | Turns | Version word | Records per file |
|---|---|---|---|---|---|---|---|
| 10000 / 10001 | 4,352 / 4,338 bytes | 37 | 37 | orders file (kind 1), player 0 | 0-36, one each | `0x2606` (turns 0-20), `0x2607` (21-36) | 3-22 (296 in all) |
| 10002 / 10003 | 51,776 / 51,725 bytes | 32 | 32 | turn file (kind 3), player 0 | 0-31 | `0x2780` | 29-96 (1,970) |
| 10004 / 10005 | 19,904 / 19,890 bytes | 6 | 6 | five turn files (kind 3, player 0), then one host file (kind 2, player "none") | 32-36; host file at 36 | `0x2780` | 98-123 (630) |

"Size allocated" is the resource table's length, which is rounded up to the 64-byte alignment unit.
"Used" counts the count byte, the files and the 2-byte terminator. Every file has the tutorial's fixed
game id (`new-game-setup.md` §1, option bit `0x08`) and the submitted flag clear. The host file is the
only file with a flag set: header flag bit 11, the "game over" bit of `save-turn-file-format.md` §10.2.
On load that bit is copied into the client's victory-declared state (`:44380`); what that changes for
the rest of the tutorial game was not followed.

**Record content, by type number.** This is an inventory of record kinds only; no game text appears.
- *Orders files (10000):* identity record type 9 (one per file); the order records of
  `save-turn-file-format.md` §9 (types 1, 2, 3, 4, 5, 10, 23, 24, 27, 29, 34, 35 and 37); the
  display-name record type 46 (twice); and the end record. The last six files (turns 31-36) hold almost
  nothing: turns 32, 34, 35 and 36 are only the header, the identity record and the end record.
- *Turn files (10002, and 10004's first five):* player records type 6 (the own race in full in every
  file; the other race as a partial record in 18 of the 37 files, from turn 7 on and not every turn); one event record type 12; planets in full (13) and partial
  (14); fleets in full (16) and partial (17); waypoint records (19, 20); designs (26); production queues
  (28); five battle plans (30); battle records (31, 39) on some turns; special objects (43); score
  history (45); and the end record carrying the turn number.
- *Host file (10004's last file):* both races' full player records, 24 planet records, 18 fleets with
  their waypoint records, 22 designs, 12 queues, ten battle plans (five per race), two special-object
  records and the end record. It has no event, partial or score records.

The only text-like bytes are the packed race and design names inside the player and design records.
None of the three resources holds tutorial lesson prose, help text or message templates. So they are
**not** the missing lesson-introduction text of `tutorial-system.md` (Open Questions). They are not the
source of the engine footnotes of `ship-design-and-components.md` §15c either: those are identifiers
41-45, 933 and 396 of this document's main table, reached through a selector-to-identifier `switch`
(see §15c).

### 8.3 Who loads them, and when

- **Turn files and host file (10002, 10004): `FUN_1048_82d8`, `stars.exe.export.c:31115`-`31237`.** The
  argument is a file number 0-37. Numbers below 32 index the 10002 resource; numbers 32-37 index the
  10004 resource after subtracting 32. If the number is not below that resource's count, the routine
  returns a distinct "not present" code. Otherwise it builds a file name from dynamic string 486 (the
  tutorial base name) plus the turn-file extension for player 1, or the host-file extension when the
  number is 37. It creates that file and writes the selected file's records into it unchanged.
- **Caller: the tutorial's turn submit, `:7940`-`7948`.** In a game marked as the tutorial (option bit
  `0x08` of the player's copy of the game options), submitting a turn while the turn counter is 35 or
  less does not run turn generation. The client writes the player's history file (`FUN_1048_84c8`),
  writes the pre-made turn file for the next turn (number = turn + 1), and on turn 35 also writes the
  host file (number 37). It then reopens the game after a short minimum delay. A failure to write
  either file takes the error path. So the tutorial's first 36 turns are not generated at all. They
  are replayed from 10002/10004, and nothing the player ordered changes the turn file that comes back.
  From turn 36 on, with a host file now present, submission takes the normal path.
- **Never written:** file 0 of 10002, the turn-0 turn file. The only two call sites pass turn + 1 (at
  least 1) and 37. The tutorial's turn-0 files come from ordinary game creation, started
  by the tutorial start routine (`FUN_10f8_0522`, `:104236`ff.) with the fixed game id
  (`new-game-setup.md` §1). File 0 is presumably the turn-0 file that creation produces, shipped for
  completeness (not compared byte for byte).
- **Orders script (10000): `FUN_1048_7c66`, `:30709`-`30745`** (described from the loader's side in
  `turn-generation-engine.md` §1a step 2 and `tutorial-system.md` §5). When the host generates a turn in
  a tutorial game, the loader reads slot 0's orders from file number *turn* of this resource instead of
  from the player's orders file, as long as the turn counter is below 37. Every file in the script has
  player number 0. Together with the pre-made turn files, which are all for player 0, this shows **the
  tutorial's human plays slot 0** and the scripted second race is slot 1. In the normal flow above,
  generation is skipped through turn 35, so the script's turns 0-35 are reached only if a tutorial turn
  is generated by some other route (for example from the host machinery, not traced). Its turn-36 file,
  the one the normal flow reaches at the first real generation, has no orders. By the code as read, the
  human's own orders for turn 36 are therefore replaced by an empty set (not exercised, since the game
  was not run). The script's version words (`0x2606`/`0x2607`) are older than the turn files'
  (`0x2780`) and below the general file validator's floor (`save-turn-file-format.md` §10.2). The orders
  loader does not check the version, so the script is still accepted. This suggests the script predates
  the switch to pre-made turn files and was left in as the generation-path fallback (inference, not
  proven).

### 8.4 What this closes elsewhere

- `client-interface.md`'s resource inventory: the three "compressed data blobs" are three containers of
  enciphered (not compressed) game files: the tutorial's player-0 orders script, its pre-made turn
  files for turns 0-36, and its turn-36 host file.
- `tutorial-system.md` §5 and `turn-generation-engine.md` §1a: the human tutorial player is slot 0. The
  scripted orders are now inventoried by structure.
- `ship-design-and-components.md` §14a: the gift-mask word reads 0 in every full player record in these
  files.

## Cross-references

- `client-ui-dialog-catalog.md` documents where this mechanism's output is known to surface (LRT and
  victory-condition wizard checkbox labels, among many others referenced by call sites throughout the
  client) and the plain-text MENU/DIALOG resource findings that were successfully recovered *without*
  needing this mechanism. Those wizard labels are now directly recoverable from this table.
- `race-traits.md` and `race-designer-ui-and-availability.md` reference specific LRT/PRT/trait names
  that earlier revisions of this document recorded as unrecoverable. They are recoverable now; see §5.4.
- `fleet-movement-scanning-cargo.md` supplied the nine live-confirmed (identifier, string) pairs that
  validated the decode (§3.3).
- `ship-design-and-components.md` can be cross-checked against the per-component availability text in
  identifier ranges 64-127 and 320-383.
- `turn-generation-engine.md` first identified §6's second table (without decoding it) while tracing
  the message-record store's text-resolution path, and cross-references the resolution back here.
  Its "gated race" fleet/planet effects, mine-laying/mineral-packet random events, and minefield
  collision/sweep mechanics are now independently corroborated by §6's recovered text.
- `client-ui-dialog-catalog.md`'s "Messages" section documents the message-record store §6's table
  feeds text into, and its "Escape-coded report-text formatting" section documents the
  backslash-letter template convention §6's recovered strings use throughout.
- `client-interface.md`'s "Default planet-name table" paragraph and "planet-name label toggle" item
  first identified §7's third table's real-world role (the on-map default planet-name label) and its
  mechanism (nibble-decompression, word-initial capitalization) from the decompile alone, without
  naming `FUN_1040_1a78` or resolving its offsets; §7 confirms that identification, decodes the
  table in full, and supersedes that document's note that "the actual roster of names could not be
  recovered."

## Open Questions

- **The mechanism itself is closed.** §3's accumulated-sum lookup is resolved (an 84-entry
  frequency-ordered substitution alphabet), the decode reproduces all nine independently-verified known
  pairs exactly, and a full sweep of the valid identifier range produces 1,392 coherent, plain-ASCII
  strings with no residual garbage. No part of §1, §2, §3 or §4 is now marked unresolved.
- **There is no table-initialisation routine to find.** Earlier revisions treated locating a startup
  routine that builds these tables as the most promising next step. No such routine exists: the tables
  ship fully-formed in the executable and are byte-identical in a running instance (§3.4). That open
  question is closed rather than answered.
- **The identifier ceiling is settled at 1,413.** The earlier "highest observed call-site value of
  roughly 1536" over-estimated the table's extent: the structure physically cannot hold more than
  1,414 entries (§3.1), and the last populated slot is 1413. Call sites that appear to compute
  identifiers above that ceiling either add a base to a bounded small value that keeps the result in
  range, or would read past the end of the length tables — worth a spot-check if any such call site is
  ever observed producing visibly wrong text in a live client, but no such case has been seen.
- **The page pointer table's width — searched directly in the paging function, confirmed negative.**
  `FUN_1010_0000` (`stars.exe.export.c:3702`-`3769`), the general-purpose lookup routine §1-§3
  describe, was read in full specifically looking for a range check or clamp against the page index
  (identifier divided by 64) before it is used to address the page-pointer entry (page index times
  2, plus offset hex 14f; `stars.exe.export.c:3745`) or the page's length table (page index times 64,
  plus offset hex 17d; `stars.exe.export.c:3727`). **None exists.** The function performs no bounds
  check of any kind on the identifier it is given — no comparison against 1413, no comparison against a page count, nothing —
  it simply computes both addresses unconditionally from whatever index it is given. The 23-entry
  count therefore remains exactly what it was: consistent with every observation and with the
  surrounding structures butting tightly against it with no slack (§3.1), but not provable from an
  explicit bound in code, because no such bound exists in this function. This is now a confirmed
  negative rather than an unexamined gap — the callers (324 sites) were not individually audited for
  a pre-call clamp, but every literal identifier observed among them is comfortably in-range, and a
  clamp living in even a handful of the 324 call sites rather than in the one shared paging routine
  itself would be a strange design for the original game's usual conventions (compare §1's single shared
  lookup routine framing). The same negative result was checked for and found for §6's sibling
  message-table routine (`FUN_1030_8984`, `stars.exe.export.c:16352`-`16409`) and for §5.5's newly
  found third table's routine (`FUN_1040_1a78`) — none of the three paging routines in this project
  clamps its page index internally; §5.5's table is the only one of the three with *any* internal
  range enforcement, and that enforcement (a modulo-999 wraparound on the raw identifier, not a
  page-index clamp) is applied before paging even begins, not as part of it.
- **Follow-up work is now content-level, not mechanism-level.** The useful next step is no longer
  cracking this table but consuming it: mapping recovered identifiers back to the call sites that use
  them, so that the strings can be attributed to specific screens, message types and rules in the other
  specifications (see §5.4). That is ordinary cross-referencing work and needs neither Ghidra nor a live
  instance.
- **The second, message-body table (§6) is now also closed.** It uses the identical mechanism at a
  different base, its character table and full 387-entry identifier range are both fully decoded, and
  known-good message codes from `fleet-movement-scanning-cargo.md` and elsewhere confirm the decode.
  The one remaining follow-up here is likewise content-level: attributing individual identifiers in
  `extracted-game-data/message-strings.txt` to the specific turn-generation events that log them (a few
  are already attributed in §6.4, but the bulk of the 387 have not been individually traced back to a
  call site).
- ~~Whether any *further*, third compressed table exists elsewhere in the client... was not
  investigated in this pass~~ **Resolved: yes, and now fully decoded — see §7.** A third table using
  the identical mechanism, at yet another set of base offsets, was located in segment `1040`
  (`stars.exe.export.c:21857`-`21929`) and, in a later pass once `stars.exe` was located (at
  `C:\Downloads\Games\Stars\stars.exe`, one directory above this project's own root — see §7's
  opening note), decoded in full: **999 populated strings across identifiers 0-998, no gaps.** It is
  not dialog/tooltip text: its explicit modulo-999 identifier range, decode-time word-initial
  capitalization, and default-name-lookup call sites confirm it as the client's built-in
  default-object-name pool, specifically the on-map default *planet*-name label (§7.4). §3.5's
  separately-documented "categorical discriminator" nibble codec, once attributed there to (among
  other things) "the default planet-name generator," is now confirmed to be a different, unrelated
  mechanism; that attribution was a conflation from an earlier pass, corrected in §3.5's own note —
  there is exactly one default-name generator, and it is `FUN_1040_1a78` / §7's table, not the
  categorical-discriminator codec.
- **The third table (§7) is now also closed.** Like §6, its one remaining follow-up is content-level
  rather than mechanism-level: `extracted-game-data/default-names.txt` holds all 999 recovered names,
  but individual identifiers have not been traced to the specific per-planet index array
  entries a given game instance actually assigns, since that assignment is
  presumably seeded/shuffled at universe-generation time rather than fixed — a question for
  `turn-generation-engine.md` or a future pass, not for this document.
