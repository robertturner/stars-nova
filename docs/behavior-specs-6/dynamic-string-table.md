# Dynamic String Table Specification

This document covers the exported client's mechanism for storing and retrieving all player-facing
dynamic text (message bodies, race/tech/component names, victory-condition labels, and most dialog
label text that is not fixed at layout time — see `client-ui-dialog-catalog.md`'s note on the Lesser
Racial Traits and victory-conditions wizard pages, whose control labels are confirmed to be populated
this way rather than from static dialog resources). It is derived from decompiled control-flow logic
for a single shared lookup routine, cross-referenced against the exported client's real on-disk data
layout. Unlike the MENU/DIALOG findings elsewhere in this project (recovered as plain, uncompressed
resource text), the mechanism here is a proprietary compression scheme, and this document is explicit
about which parts of it are proven versus unresolved.

## Overview

Nearly every dynamically-labeled piece of UI text in the client is produced by one shared lookup
function taking a single numeric string identifier and returning a pointer to a decoded, human-readable
byte string in a shared, non-reentrant static buffer (the routine is called from well over 150 distinct
call sites spanning nearly every major UI and game-logic segment, confirming it is the general-purpose
text-lookup path, not a narrow one-off helper). String identifiers observed across call sites span
roughly 0 to at least ~1536, and the lookup is **paged**: an identifier's high bits select a 64-entry
page and its low 6 bits select an entry within that page.

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
  continuation nibbles first. Critically, **the accumulated sum is not bounded to a single byte** —
  nothing in the decompiled logic masks or wraps it — so the lookup structure it indexes into must be
  able to represent values well beyond 255 for any character reachable only via multiple continuation
  nibbles.
- Decoding stops after the page's per-string nibble-length (from §1) has been fully consumed; the output
  buffer is then null-terminated.

### 3. What the accumulated sum indexes — NOT resolved

This is the one open piece of the mechanism, and it is the reason no actual string content is reported
below. The decompiled logic uses the final per-character accumulated sum as a byte offset from a fixed
base location, and copies the single byte found there to the output. Several lines of evidence rule out
the straightforward reading of that base location as a conventional "256-entry substitution alphabet":

- The literal base offset used for this lookup, when resolved against the exported client's real data
  segment (confirmed correct — see §4), lands on a block of genuine, meaningful, but semantically
  **unrelated** static content: a help-file name, a run of internal window-class-name fragments, and a
  handful of `sprintf`-style format strings (`%d`, `%ld`, `%d%%`, and similar). This content is real and
  intentional (it is referenced elsewhere in the binary for unrelated purposes), not corrupted data —
  but it is not a plausible 256-entry character-substitution table for general English text.
- Because the accumulated sum is unbounded (see §2), whatever this base location really represents must
  logically be able to serve lookups reaching arbitrarily far past a 256-byte window — inconsistent with
  a small fixed alphabet table, and more consistent with a **shared reference pool**: a scheme where each
  character is encoded not as "the Nth letter of a fixed alphabet" but as "the byte located at this
  computed position within one large, shared block of text" (exploiting the redundancy of natural-language
  text — a common letter that already occurs somewhere nearby can be referenced cheaply). Under this
  reading, the "unrelated" text found at the expected base location would not be wrong at all — it would
  simply be an early, low-offset slice of the same shared pool that later, larger offsets also draw from,
  and the specific slice decoded for any one string identifier depends on getting the identifier's page
  and pointer-table resolution exactly right, which has not yet been achieved (attempts using this
  document's §1-§2 mechanics reproducibly decode to short runs of the letters already visible in that
  early slice — `s`, `t`, `a`, `r`, `h`, `p`, `l`, `!`, `.` — rather than coherent words, consistent with
  landing near the right neighborhood of the pool but at the wrong precise offsets).
- Confirming or refuting this "shared reference pool" model would most likely require either (a) locating
  and decompiling the routine that *builds* these tables at program startup (not yet found — no call to
  a recognizable heap-allocation import was found near this routine, meaning the exported client's own
  decompile does not name whatever populates them), or (b) dynamic tracing of a running instance of the
  original program, neither of which was available to this analysis pass.
- **A structurally similar but independently-implemented nibble-packing codec exists elsewhere in the
  client and was checked as a possible shortcut to resolving this — it is not.** A separate
  encode/decode pair (documented under "Word-wrap text layout engine" and the default planet-name
  generator elsewhere in this project) compresses short, fixed-alphabet text (roughly A-Z, digits, and
  an escape path for arbitrary bytes) into 1-3 nibbles per character, using the *same* within-byte
  nibble ordering (high nibble emitted first) as this routine. However, its decode step works
  completely differently: rather than accumulating nibbles into an unbounded sum and indexing one flat
  table, it treats the accumulated value's low nibble as a small discriminator (roughly five cases) and
  the remaining bits as a sub-index or raw character, each case backed by its own small (roughly
  11-entry or smaller) lookup table. This confirms nibble-based text packing is a recurring house
  technique in this codebase, but the two codecs are independent, differently-structured routines —
  finding one did not unlock the other's tables, and this specific "categorical decode" shape was
  checked directly against §3's routine and ruled out (its decode step is a plain, unconditional single
  byte dereference with no branching on the accumulated value, unlike this alternate scheme).

### 4. Storage location — confirmed, and rules out two alternative theories

- The routine's tables are addressed relative to the program's single automatic data segment (there is
  exactly one data segment in the whole executable). This was independently confirmed authoritative via
  the executable's own header fields (the automatic-data-segment index and the initial stack-segment
  index agree, and both match the segment this analysis targets), not merely inferred from address
  naming conventions.
- That data segment is only 8,850 bytes of file-backed (initialized) content, but the executable header
  specifies an additional 8,192-byte runtime local-heap region and a 16,384-byte stack appended after it
  at load time — meaning a meaningful portion of this segment's *runtime* address range (roughly the
  8,850-26,086 byte range, zero-filled at load, plus the 26,086-34,278 byte local-heap range) **does not
  exist in the executable file at all** and could only be populated by code that runs at program startup.
  Several of the per-page pointer-table values recovered from the file fall exactly inside this
  runtime-only local-heap range, meaning at least some pages' string data is only ever materialized in
  memory during actual program execution, not statically present in the shipped file.
- **Two alternative storage theories were tested and ruled out.** Earlier analysis speculated that the
  bulk of this dynamic text might instead live in one of three large custom-type resources found in the
  executable's resource table (roughly 4,300 / 51,800 / 19,900 bytes). Direct inspection of those three
  resources' raw bytes shows each begins with an identical, structured, repeating record header
  containing an incrementing per-record counter — a pattern consistent with a proprietary packed
  image/sprite container (plausibly ship, planet, or portrait graphics), not a string table, and
  inconsistent with the length-table/nibble-stream shape described above. A second, separate group of
  six mid-sized resources (roughly 12-44 KB each) was also considered; the exported resource table's own
  name records resolve this group's *type* to an explicit internal label of **"WAVE"**, confirming these
  are packaged sound-effect audio files, not text. Neither resource group is the dynamic string source.

## Cross-references

- `client-ui-dialog-catalog.md` documents where this mechanism's output is known to surface (LRT and
  victory-condition wizard checkbox labels, among many others referenced by call sites throughout the
  client) and the plain-text MENU/DIALOG resource findings that were successfully recovered *without*
  needing this mechanism.
- `race-traits.md` and `race-designer-ui-and-availability.md` reference the specific LRT/PRT/trait names
  that remain unrecoverable pending resolution of §3 above.

## Open Questions

- The true semantics of the "accumulated-sum lookup" in §3 — fixed alphabet table versus shared
  reference pool versus something else — are unresolved. This is the single blocking gap preventing
  recovery of any actual dynamic string content (message text, trait/tech/race names, and the several
  dialog control labels noted above).
- The routine that initializes these tables at program startup was not located in the decompiled
  material. Finding it (if it exists in an analyzable form) would likely resolve the open question above.
- Whether identifiers beyond roughly 1536 (the highest observed call-site value) are valid, and how many
  total pages/strings the table holds, is not confirmed.
