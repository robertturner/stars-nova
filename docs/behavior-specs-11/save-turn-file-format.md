# Save and Turn-File Format

Behavior specification for the on-disk turn-order and save-file protocol in the 1995-2000 4X game *Stars!*. This document is derived directly from the exported client's decompiled logic; it is new material, since no prior document in this set covers file formats. Facts are described in generic terms; no original identifiers or literal code are used.

## Overview

Every turn, each player's client writes a compact, opcode-tagged log of every change the player made (order edits, design changes, renamed objects, and so on) to a per-player turn-order file. The host reads every submitted file, applies each recorded change, resolves the turn (see `turn-generation-engine.md`), and writes back a new per-player state file for the following turn. The same record-stream format is used in both directions (player-to-host and host-to-player), and also for a full "here is everything you're allowed to see" state dump rather than only incremental deltas.

## Mechanics

### 1. Record stream format

The stream is a flat sequence of variable-length records. Each record begins with a 2-byte header: the low 10 bits encode the **payload length in bytes** (capping any single record's payload at 1,023 bytes; the shared append routine is called with byte counts, for example 2 for a 16-bit value and 4 for a 32-bit value), and the high bits encode a **record-type code** (an opcode). **Correction (step-6 pass, see §10):** there is no compression. Every record except the end marker (type 0) and the file header (type 8) has its payload XOR-enciphered with a keystream that each header record reseeds; the reader deciphers immediately after reading the payload and the writer enciphers just before writing. The word "opcode" above is the record type in bits 10-15 of the header word; the same numbering is used by the decoder switch of §9 and by the header/footer/identity record types of §10.

### 2. Local change buffer and coalescing

While a player is editing orders during their turn, changes are accumulated into a local buffer (capped at roughly 32,000 bytes) rather than written to disk immediately. Appending a new change record first checks whether the buffer's most recent record already targets the same object; if so, the previous record is rewound and replaced rather than appended alongside it — i.e., **edits to the same object are coalesced into their net effect**, not stored as a full edit history. This buffer is what gets flushed to the actual turn-order file when the player ends their turn.

### 3. Observed opcode families

Recovered opcodes and their apparent purpose (numeric values are internal and omitted; grouped here by function):

- **Object selection/active-context changes** — which object is currently selected, and broadcasting a set of currently-selected fleet IDs (up to roughly 510 at once).
- **Design create/delete** — creating or deleting a ship or starbase design. Design slots are confirmed as **16 regular hull designs plus 10 starbase designs per race (26 total)**, each design record occupying a fixed 147-byte layout. **Where the caps and the name limit are enforced (design-limits pass):**
  - *Client.* The designer's Copy button is enabled only while the race has a free slot of the kind being edited, found by scanning the 16 ship or 10 starbase records for one marked unused (`FUN_10c8_4122` `:85473`-`85503`; the designer dialog procedure `FUN_10c8_01f6` picks the first unused slot at `:82816`-`82835`); there is no error message, the button is simply greyed. The design-name box is limited to **31 characters** (edit-control limit 31, `FUN_10c8_01f6` `:83257`).
  - *Host, step 6* (opcode `0x1b`, `:30297`-`30373`, and the record unpacker `FUN_1070_0000` `:43705`-`43749`). The caps hold by construction: a record names its slot directly, 0-15 for ships and 16-25 for starbases, and a slot above 25, a race other than the acting one, or a race number out of range makes the record fail. Replacing a design that is in use and has ships in existence also fails. The name field in the record is 32 bytes (31 characters and a terminator). A packed name whose stored length byte exceeds 32 fails, and the unpacked text is cut to the 32-byte field. Any failed record makes the apply of that orders file fail, which **aborts the whole turn generation** with the corrupt-file dialog (`turn-generation-engine.md` §1a). The race's design counters are only adjusted, never tested, so no separate count limit exists. The unpacker also silently zeroes the quantity of any slot whose part the hull slot does not accept, whose quantity exceeds the slot's capacity, or which the race cannot build unless an exemption bit in the record is set (`:43797`-`43808`).
  - *Reimplementation note (recommendation, not original behavior):* enforce 16 + 10 slots and 31-character names in the client and reject (rather than abort on) a bad record on the host.
- **List diffs (delete/insert/replace)** — applied to two different kinds of variable-length list: an 18-byte-per-entry production-queue-shaped list, and a 9-int-per-entry fleet waypoint/task list. The same three opcodes are reused for both list types, disambiguated by an accompanying target-object id.
- **Bit/nibble toggles** — small single-bit or single-nibble field changes (e.g., a checkbox-style option).
- **Narrow numeric-array diffs** — 1-byte, 2-byte, or length-tagged encodings chosen to minimize wire size for small changes to per-field numeric arrays.
- **Fleet record updates** — a compact form (mass-only) and a fuller form (cargo plus waypoint data), the fuller form's cargo section carrying 3 unconditional mineral/colonist fields plus 1 additional field sent only if it changed. In the turn and host files a full-detail fleet record (type 16, the owner's own fleets and every fleet in the host file) also carries the stack damage words: a 16-bit mask of the stacks with damage, then one word per marked stack; other players' records carry none (`combat-resolution.md` §8, "The damage word outside battle").
- **String/name fields** — planet names, fleet names, message text, and similar are capped at **31 characters**.
- **Relationship changes** — a 2-byte write into a **100-entry per-race table**, **now confirmed identical to** the diplomacy relationship table described in `diplomacy-relations.md` (same storage location — offset 0x5A32 of the per-race record block, one 192-byte row per race and one byte per other race — verified by tracing this exact opcode's decoder case through to the table's own read-side accessor).
- **Per-race identity number and player display name** (corrected by the step-6 pass, see §9's note on `0x24`/`0x2e`) — applied only while the host is processing orders (`DAT_1128_078a` bit 1); the earlier "home-system coordinates ... networked-only" reading was wrong on both counts.
- **Score-history snapshot** — a per-turn score record, retained only for the most recent 100 turns (older entries are not written/retained). *(Corrected by the score-visibility pass; full rule below. The 100-turn history is kept by each player's client in its history file, not by the host.)*

  **Score records: who receives which, and where the history lives (score-visibility pass; writer `FUN_1070_374a` `:47657`-`47668`; scoring in step 35, `victory-conditions.md` §2; client loader `:44707`-`44800` and `:44270`-`44345`; history-file writer `FUN_1048_84c8` `:31243`-`31404`).**
  - *What the host keeps.* Nothing historical. Every generation, step 35 (after the year counter is incremented) recomputes the current 24-byte score record of every race in memory (`client-ui-dialog-catalog.md`, "Score display", for the layout). The host file itself receives no score records.
  - *What each player's turn file carries.* One record (type `0x2d`, 24 bytes) per race k, for the new year only, written when any of these holds: (1) the game is over (the victory bit is set); (2) k is the player's own race; (3) race k is eliminated (status bit 0, raised by step 35 for a race with no planets and no ships), so its final standing is shown to everyone; (4) the "Public Player Scores" option is on and the new turn counter exceeds 19, i.e. from the file for 2420 onward. Otherwise another race's score never reaches the player.
  - *Where the history comes from.* The client merges each received record into a per-race list keyed by year (at most 101 entries per race; a record for a year already present replaces it, and the oldest entry is dropped when the list is full). When the player saves, the client writes its history file (kind 4, `.h<N>`) with every list entry whose year is no more than 100 years before the current one, and reads it back on load. So a player's score graph shows its own race every year it played, other races only for the years their records were sent under rules (1)-(4); a player who loses its history file keeps only what later turn files bring.
  - *Reimplementation note (recommendation, not original behavior):* a server may keep the history itself, but should send a player only what rules (1)-(4) allow, so that private scores stay private.
- **Large blob transfer** — a generic large-payload opcode, chunked at the same 1,023-word cap as any other record.

### 4. File naming

Per-player, per-turn output files follow a fixed convention: the base game name has its extension stripped and is rebuilt with a single-letter file-type code (one letter for the turn-order file, a different letter for a host/history file, and a generic letter for other player-state files) combined with the player's slot number.

### 5. Reading and applying incoming records

A dedicated loader reads an incoming stream either from a bundled internal resource (used for pre-made scenarios) or from an open file/stream, validating a magic/version and game-identity header before trusting any of its content. Applying a full buffer of records replays each one through a shared decoder in sequence.

**Correction, confirmed by inspection of the exported client:** the replay pass does **not** short-circuit on the first failing record. It walks every record in the buffer unconditionally (advancing by each record's own length field regardless of outcome) and combines every individual record's success/failure flag with a bitwise AND, so the pass as a whole reports failure if *any* record failed to apply, but a single bad record does not stop the rest of the buffer from being processed. A separate global flag is set for the duration of the whole replay pass and checked by the same low-level "append a record" primitive used when building outgoing changes (§8): while the flag is set, that primitive is a no-op. This means record *application* reuses the exact same field-setter code paths that interactive editing uses (so, e.g., replaying a fleet-cargo change calls the identical setter a player's own edit would call), but new change records are never generated as a side effect of replaying existing ones.

### 6. Full state-dump mode

Beyond incremental per-turn deltas, the same record format is used to write a **full snapshot** of everything one player is currently allowed to see about another race: a planet header record, every visible fleet (using either the compact or full fleet-record form depending on stack size), a message-text blob, other races' visible minefields and designs, and the score-history snapshot described above (§3) — again chunked using the same large-blob opcode where needed.

### 7. Race file (.r1) save and load logic, confirmed by inspection of the exported client

Saving a race design from the Race Wizard is confirmed to reuse this same generic record-stream writer rather than a bespoke race-file format: the save-as handler prompts through the standard Windows Save dialog (its filter-string list is built from a single pipe-delimited string-table entry, split into null-terminated segments at each `|` — the same pipe-delimited-list pattern already noted elsewhere in this project's coverage of the executable), opens the destination file through the same shared low-level file-open helper used elsewhere for turn/save files, writes the fixed 192-byte race-data block as one record, then computes a running XOR checksum over that same 192-byte block and writes the checksum as a second, trailing 2-word-payload record, and closes the file.

**Correction to the previous pass's finding, and the missing load logic located:** loading was not found in segment 29 because it does not live there — it lives in the client's window-command layer (Ghidra prefix `FUN_1020_`, this project's segment 5), invoked from the Race Wizard's "load a race" UI action (a standard Windows Open-file dialog — the only such dialog call found anywhere in the executable — feeding a chosen filename into a dedicated loader routine). That loader:

- Opens the file through the same shared low-level file-open helper as the save path (and as ordinary turn/save files).
- Reads one leading record and validates it as a game-identity/version header — the **same generic header record already documented in §5** as gating any incoming stream, not a race-file-specific invention. Two fields are checked: a version-range field (required to fall within a specific narrow numeric band) and a one-byte "file kind" tag, which must equal a specific fixed value distinguishing a race file from every other file kind that flows through this same generic reader (turn-order files, host/history files, etc. presumably carry their own distinct tag values, none of which were enumerated in this pass). **The race-file tag value is now confirmed: `5`.** The concrete check (`FUN_1020_44ba`, `stars.exe.export.c:8614`-`8658`) rejects the load unless the decoded header's kind byte equals 5 (`stars.exe.export.c:8652`), alongside a version-range field required to fall in the (word-masked) range `0x620`-`0xa7f` (`stars.exe.export.c:8649`-`8650`). A further search across the whole decompile for any *other* literal comparison against this same header field found none — every other reference to the same underlying storage location is an unrelated reuse of the same memory for in-game option bits at other points in execution, not a second file-kind check — so at this point the other kinds' tag values were unconfirmed. **Settled by §10.3:** every file kind carries its kind byte (0 universe, 1 orders, 2 host, 3 turn, 4 history, 5 race), but only the race loader compares it; the other loaders select the kind by file name and validate identity fields instead (next paragraphs).

  **Follow-up pass — a second, structurally parallel loader was found, and it explains the negative result above rather than contradicting it.** `FUN_1070_2dfc` (`stars.exe.export.c:46421`-`46577`) is a second generic file/record-header validator: it performs the identical version-range test as `FUN_1020_44ba` (masked version strictly between `0x61f` and `0xa80`, `stars.exe.export.c:46465`, the same bounds as line `8650`), reusing the same underlying open/read primitives (`FUN_1070_329a`/`FUN_1070_31f4`). But it does **not** read or compare any stored "file kind" byte out of the loaded file at all — instead it checks the file's stored game-identity fields against the *current* game's own identity (game id, plus a turn/player-index match) and dispatches on a small "purpose" code the *caller* supplies (the low byte of its first argument; observed values `0`, `1`, `3`, `4`, and a combined-flags `0x2003` across its seven call sites: `stars.exe.export.c:8319`, `30910`, `44074`, `44135`, `44376`, `46578`, `46619`), not on anything read from the file itself. This is the loader actually reached from turn-processing/new-game-commit contexts (its call sites sit in the segments hosting turn-generation and new-game-commit code, not the Race Wizard).

  **This gives a well-evidenced explanation for why no second literal tag-byte comparison exists to find, rather than leaving the question open by default.** Turn-order and related files are most plausibly not validated via a self-describing "kind" byte inside the file at all — that mechanism is specific to `FUN_1020_44ba`, the loader behind the Race Wizard's interactive "Open" file dialog, which needs a defensive check because a human can browse to and select *any* file on disk. Turn-order/host-history/other-player-state files, by contrast, are always opened by code that already knows exactly what it's opening (by filename convention, per §4) and go through `FUN_1070_2dfc` instead, which authenticates by matching the file's game-identity fields against the current game plus a caller-supplied expected "purpose," never by reading a stored kind constant. Under this reading there is no undiscovered tag-value table for the other file kinds to recover — the two file families use genuinely different authentication mechanisms, one self-describing (race files, tag byte) and one context-supplied (turn/save files, caller-known purpose code). The purpose codes are decoded in §10.3/§10.6: they are the same numbers as the file-kind codes (0 universe, 1 orders, 2 host, 3 turn, 4 history), with `0x2000` and `0x1000` as flag bits (read the multi-turn trailer; rewind after validation). This is a concrete, well-evidenced answer to the "or through some other mechanism entirely" branch this document's own text already allowed for.
- Reads a second record and requires it to carry a specific record-type tag, then copies its full payload out as the 192-byte race-data block.
- Reads a third record (the trailing 2-word checksum) and recomputes the same XOR checksum over the just-read 192-byte block, using the identical checksum routine the save path uses; a mismatch aborts the load and no race data is accepted.
- Only on a full match does it copy the 192-byte block into the live race-data buffer that both the save path and the rest of the race-design system read from.

This resolves the open question from the prior pass definitively as **(a) found**: `.r1` loading exists, is driven by the same generic record-stream reader/header-validation machinery as every other file this client reads (confirming §5's mechanism applies uniformly, including to race files), and adds its own file-kind tag plus the same 192-byte-block-then-checksum framing the save path writes — the read and write sides were independently confirmed to agree on record order and content. The on-disk order is therefore **data block first, checksum second** (not checksum-first as the prior pass's save-only inspection guessed — that guess is corrected here now that the read side's record-by-record validation order is visible).

### 8. Building a change record: field-level diffing, adaptive width, and coalescing (turn-order delta-record protocol)

Confirmed by inspection of the exported client: beneath the opcode/record-stream layer already described in §1-§3, there is a distinct, generic mechanism responsible for actually *building* a change record from an in-game edit before it is ever appended to the buffer. This is the "turn-order delta-record protocol" — it is not a separate file format, but the encode-side counterpart to the record stream, and it is what §2's "coalescing" behavior and §3's "narrow numeric-array diffs" bullets were referring to at a level of detail neither had previously been traced to.

- **Trigger.** A shared field-setter function is the single entry point interactive UI code calls whenever a fleet's mass/cargo-like fields are changed (confirmed for a 6-field fleet record: it is handed both a pointer to the fleet's currently-stored field values and a pointer to the proposed new values). It compares old vs. new field-by-field; if every field is unchanged, nothing is recorded. If any field differs, it hands the two field-value sets to the delta-record builder described below, then overwrites the stored fields with the new values. The same shared "append a record" primitive underlies every opcode family in §3, not only this one, but this fleet-field case is the one traced in full end-to-end.

  **Field-to-quantity mapping, resolved this pass by reading the shared field-setter itself
  (`FUN_1050_3cee`, `stars.exe.export.c:33394`).** It dispatches on an object-type code, not just
  fleets: type `1`/`4` addresses a 4-field object with a field count that always reports 0 for index 4
  (fewer than the fleet case); type `8` addresses a 3-field object whose total is capped against a
  scaled value derived from a stored field-7 concentration (a strong match for a *planet's*
  mineral-concentration triple, not a fleet field at all); the default branch — the one this section
  and §3 describe as "the fleet record" — exposes **5** real fields (index 0-4), not 6, at the fleet's
  own field-array base. Within that default branch, field index **3** is specially gated: the caller's
  delta for that field is discarded outright unless a stored byte at the fleet record's own offset 2
  equals the literal value `7` (`stars.exe.export.c:33510`) — i.e. the field only actually changes when
  the fleet's current waypoint-task-type field reads a specific value, most plausibly "Colonize." Field
  index **4** is capacity-checked through a distinct helper (`FUN_1050_3cc8`) from fields 0-3
  (`FUN_1050_3c82`), confirming it is a materially different kind of quantity from the other four. Read
  together with §3's existing "3 unconditional mineral/colonist fields plus 1 additional field sent
  only if it changed" wording, this is best read as: **fields 0-2 = the three mineral cargo types
  (always unconditional), field 3 = colonists (conditional on task-type `7`), field 4 = a separately
  capacity-checked resource, most plausibly fuel** (the "1 additional field sent only if it changed").
  The document's separate "compact, mass-only form" does not appear to be part of this same 5-field
  dispatch and is plausibly a distinct, simpler opcode/field not covered by `FUN_1050_3cee` at all —
  this was not confirmed.
- **Encoding: adaptive per-field width.** The builder computes a signed delta for each of the (up to) 6 fields and tracks the single largest delta magnitude across all of them. That maximum then picks one of three encodings for the *entire* record: if every changed field's delta fits in a signed byte, a 1-byte-per-changed-field form is used; if it fits in a signed word, a 2-byte-per-changed-field form is used; otherwise a full 4-byte-per-changed-field ("long") form is used. Only fields that actually changed are written, flagged by a small bitmask alongside the record. This is a general-purpose narrow-encoding strategy, not a special case — it most plausibly explains this document's existing "Fleet record updates" bullet in §3 (its "compact form" and "fuller form" most likely correspond to the 1-byte and 4-byte tiers here, with a previously-unnoticed 2-byte middle tier), though that identification was not independently re-verified opcode-by-opcode in this pass and should be read as a refinement, not a certainty.
- **Coalescing (extends §2).** Before writing a new record, the builder checks whether the buffer's most-recently-appended record already targets the exact same object (matched by a packed pair of index nibbles carried in both records) *and* used this same family of opcodes. If so, rather than appending alongside it, the builder: decodes the previous record's per-field deltas back out (reversing whichever of the three width encodings it used), rewinds the buffer past that record entirely, adds the old and new per-field deltas together (producing each field's *net* delta across both edits), re-picks the narrowest of the three encodings for the combined magnitude, and writes a single merged record in its place. Repeated edits to the same object's same fields therefore never grow the buffer — they only ever update one record's net effect, re-tiering its byte width up or down as needed.
- **Buffer-append primitive.** A single shared low-level function does the actual buffer write: it packs the record header (opcode in the high bits, payload length in bytes in the low 10 bits — matching §1's record-header layout exactly), checks the running buffer length against the ~32,000-byte cap already described in §2 (flushing/warning before the cap would be exceeded), copies the payload in, and — unless the record's own opcode is a specific always-excluded value — updates the "most recently appended record" tracker that the coalescing check above reads. A record written with that excluded opcode value is therefore never a candidate for coalescing, in either direction.

### 9. The decoder switch enumerated (`FUN_1048_68a8`)

Confirmed by inspection of the exported client: the decoder switch `ship-design-and-components.md`
§10 referenced only by segment (`1048`) is `FUN_1048_68a8` (`stars.exe.export.c:29625`-`30660`, i.e.
roughly 1,035 lines — matching that document's "roughly 1000-line" estimate almost exactly). Reading
through its case bodies resolves the opcodes §3 could previously only group by family (a later pass
completed the enumeration: all 22 case labels are identified, see Open Questions):

- **Opcodes `1`, `2`, and `0x19` share one code path** (`stars.exe.export.c:29691`-`29693` onward)
  that packs a run of changed fields using exactly the three width tiers §8 describes (1-byte,
  2-byte, and a "long"/4-byte form respectively, selected by which of the three opcode values the
  shared case was entered with). This confirms, rather than merely "most plausibly explains," §8's own hedge:
  these three opcodes are the three adaptive-width encodings of the same field-diff family, not
  three unrelated record types.
- **Opcodes `3`, `4`, and `5`** operate on the same 18-byte-stride list this document's §3 already
  describes (confirmed directly: the per-entry stride literal is `0x12` = 18 at every array access in
  these cases) — opcode `3` deletes an entry (or two entries when the top bit of the index word is set), shifting later entries down; opcode `4` inserts an entry (shifting later entries up and zeroing the new slot
  before copying the payload in); and opcode `5` replaces an entry in place (trailing zero bytes of the payload are trimmed by the writer). This is consistent with §3's "delete/insert/replace" framing, now tied to
  concrete opcode numbers for all three (corrected from an earlier reading that took opcode `5` for the delete; the client's waypoint writer in `fleet-movement-scanning-cargo.md` §5 uses them as delete, insert and replace).
- **Opcodes `10` and `0xb`** are narrow single-bit/single-nibble field writes on a fleet-like object
  (opcode `10` XORs one bit of a status word; `0xb` XOR-merges a nibble into a byte within a
  per-entry array) — matching §3's "Bit/nibble toggles" bullet.
- **Opcode `0x1b`** — design create/delete (already fully documented in `ship-design-and-components.md`
  §1).
- **Opcode `0x1e`** — battle-plan write or delete (`:30458`-`30492`; corrected: earlier text called it
  a design component-slot write). It edits the acting player's table of up to 16 battle plans of 36
  bytes each; the field rules are in `ship-design-and-components.md` §2 and the record in its §12.
- **Opcode `0x24`** writes two words (one 4-byte value) into the acting race's 192-byte record at
  `DAT_1128_59ce` (offset `+0xc` from the `DAT_1128_59c2` base used throughout this section, `:30535`-`30540`), only while the host is processing orders (`DAT_1128_078a` bit 1, the
  "host generating" bit, **not** a "networked game" bit as an earlier reading of this document said).
  **Correction, step-6 pass:** it is not a coordinate pair. The destination is the same per-race
  field that new-game commit seeds with a fixed value for computer players (`:51783`), that the client
  copies into `DAT_1128_031c` at load (`:44442`), and that the host file carries back as a 4-byte
  record `0x24` (`:47546`-`47547`); `DAT_1128_031c` gates one client menu item (`:8205`). Its meaning
  beyond "a per-race 4-byte number" is not pursued (it belongs to the identity family this project
  deliberately does not detail).
- **Opcode `0x2e`** copies a short binary payload (at most 26 bytes; longer payloads fail the record, `:30629`)
  into the acting race's record at offset `+0x56` (`:30632`), again only while
  hosting. *(Corrected, code pass: it is not a name or a text.)* That field is the race's **default production
  template**: a flag byte (bit 0 = the leftover-only research setting), an entry count
  (0-12) and up to 12 words of six-bit item type plus ten-bit quantity, the copy the host applies to
  newly colonised or captured planets (`:77899`-`77943`). The orders writer sends the client's
  template slot 0 in this record (count + 1 words) when it differs from the race record's copy
  (`:30987`-`31020`; `production-queue.md` §9, "Templates in full"). **Opcode `0x26`** is the already-resolved 100-entry relationship-write opcode
  (§3's updated bullet): it writes the acting race's row at offset 0x5a32 of the per-race block (192-byte stride) using the identical shared copy routine
  as `0x2e`, with the exact address this document's §3 already confirmed against
  `diplomacy-relations.md`'s own table — recorded here only to fix its position in this switch, not
  as a new finding.
- **Opcode `0x2c`** is a dynamically-reallocated variable-length string field on a fleet object: any
  existing buffer is freed, then a new one is allocated sized exactly to the incoming string's
  length, with the string supplied either as a literal byte copy or (if a length byte is nonzero)
  unpacked through the same generic nibble-decoder `FUN_1040_35a4` that `ship-design-and-components.md`
  §12 documents. This is a strong match for §3's "String/name fields... fleet names" bullet
  specifically (as opposed to planet names or message text, which would be expected to key off a
  different object accessor).
- **Opcodes `0x2a` and `0x2b`** are single-byte field writes on two different object types (a fleet
  accessor for `0x2a`; a distinct accessor, gated by a separate flag-byte check, for `0x2b`) —
  consistent with §3's "Narrow numeric-array diffs" bullet, though the specific fields were not
  individually identified.
- **Opcodes `0x1d`, `0x22`, `0x23`, `0x17`, `0x18`, and `0x25`** were revisited in a later pass and
  traced further; all six now have at least a well-evidenced candidate identity (see below), though
  none are certain enough to fully retire this bullet's original hedge.
- **Opcode `0x25` is the "broadcast a set of currently-selected fleet IDs" opcode** this document's
  §3 already described qualitatively but had not tied to a number: it writes into the same global
  selection buffer (a 514-entry array plus a running count, both read back by unrelated UI-list code
  elsewhere in the executable) that backs the client's own idea of "the current fleet selection." It
  populates that buffer three ways depending on payload shape: from a single fleet id, from a list of
  ids carried in the payload (each individually resolved through the shared fleet-lookup table), or —
  for a fixed 2-word payload — by scanning every fleet at a given map location owned by the current
  race and collecting all of them. This resolves the previously-unassigned half of §3's first bullet
  ("broadcasting a set of currently-selected fleet IDs, up to roughly 510 at once" — the buffer's real
  capacity is 514, confirming that figure almost exactly) to a concrete opcode number.
- **Opcode `0x18` is a fleet-split/fleet-creation opcode.** It resolves one existing fleet by id
  through the same shared fleet-lookup used throughout this switch, takes a full snapshot of that
  fleet's record, and hands the snapshot to a routine that either creates a brand-new fleet record (
  when a sentinel value marks "no destination id yet") or writes the snapshot's cargo/production-list
  fields into an already-existing destination fleet record — the encode-side counterpart of the
  client's Fleet Split command, not the "single selected object" reading this document previously
  guessed for one of this opcode group.
- **Opcode `0x17` is a fleet-to-fleet field-transfer/merge opcode**, distinct from the ordinary
  fleet-record field-diff family (`1`/`2`/`0x19`). It resolves two existing fleets by id (both
  through the same fleet-lookup as `0x18`/`0x25`), requires them to share the same owning race, takes
  a full snapshot of each, then walks a bitmask of up to 16 selected fields and, for each, transfers a
  bounded (clamped to roughly ±32,766) amount between the matching field in each fleet's snapshot —
  additively in one pass, subtractively/capped in the other — before writing the net results back
  through the ordinary field-setter and clearing a pending-order flag on both fleets. This is the
  strongest candidate yet for the fleet-merge "special-ability stats... some take the maximum value
  across the merged ships, others sum with a fixed cap" mechanic `client-ui-dialog-catalog.md` already
  describes qualitatively (its "Merge fleets" note) — now with a concrete opcode, though the exact
  field-to-stat mapping was not individually decoded. **Firmed up:** the full case body
  (`stars.exe.export.c:30120`-`30222`) was read directly this pass; it confirms the two-pass
  add/subtract structure and the up-to-16-field bitmask exactly as described, and additionally shows a
  post-transfer cleanup step (`FUN_1050_6e52`, `FUN_1038_1582`/`FUN_1038_14ec`, `FUN_1038_1db4`) that
  recomputes derived fleet statistics and frees/clears a design slot if all of a hull's 16 component
  counts reach zero — consistent with one fleet's design being fully absorbed into the other's, but the
  exact per-field stat mapping still was not individually decoded.
- **Opcode `0x23` is a minefield-edit opcode.** It resolves an existing object by id through a
  distinct, separately-sorted 56-byte-stride table (looked up by binary search when the table has had
  entries removed, or by direct indexing otherwise — a shape consistent with a dynamically
  created/destroyed object population, unlike planets' fixed table) — not the same table as fleets,
  designs, or the `0x1d` list below — and requires the acting race to own it. It toggles a single bit
  (matching `turn-generation-engine.md`'s note that minefields "are edited via the same order-queuing
  mechanism used everywhere else," and `race-traits.md`'s Space Demolition ability to "remotely
  detonate its own standard minefields" — a strong match for that single bit specifically) and
  rewrites a packed ~24-bit multi-field value (most plausibly the field's type/strength or radius;
  not decoded field-by-field). **Firmed up:** reading the case body directly
  (`stars.exe.export.c:30512`-`30533`) refines "a packed ~24-bit multi-field value" into three
  distinct writes: the single-bit toggle at record offset `0xd` (bit `0x80`, as above), a 10-bit field
  at offset `0x17` (mask `0x3ff`) with an *additional* small XOR-only correction applied to bits 10-13
  of that same field (conditioned on a separately stored byte at offset `0x2f`), and a second,
  independent 10-bit field at offset `0x18` (also mask `0x3ff`). This is most plausibly the minefield's
  radius and strength/type stored as two separate ~10-bit quantities rather than one packed 24-bit
  value, but which offset is which was not determined.
- **Opcode `0x22` is the research-settings order (identified by the step-6 pass; this supersedes the
  "per-race UI preference" guesses below).** It writes, into the acting race's record, a research
  budget percentage (0-100; payload byte 0 out of range fails the record, `:30493`-`30500`) at offset
  `+0x38` (`DAT_1128_59fa`) and a byte at `+0x39` (`DAT_1128_59fb`) whose low nibble (0-5) is the field
  currently being researched and whose high nibble (0-7) is the "after this field" choice (`:30501`-`30510`).
  Those are exactly the values the research buy loop `FUN_10b8_4ce4` reads (`:80059`-`80060`, the
  low nibble as the current field and the high nibble as the next-field option; the percentage is the
  "leftover resources to research" share of `production-queue.md` §8). The earlier reading below was
  formed from the report painter's incidental use of the same bytes. *Earlier text, kept for the
  record:* writes two small per-race fields (a value capped at 100, and a byte split into
  two independently range-checked nibbles, 0-5 and 0-7) into the same 192-byte-stride per-race table
  as `0x24`/`0x26`/`0x2e`, at a pair of offsets distinct from those three. Given the small enumerated
  ranges, this is more plausibly a per-race UI/session preference (e.g. a race icon or color-slot
  index) than an economy or combat value, but the exact field was not identified. **Firmed up, in the
  direction of the UI-preference guess (now superseded):** the two destination fields (`DAT_1128_59fa`/`DAT_1128_59fb`
  within that per-race table) were traced to their only other readers/writers in the executable — a
  report-summary painting routine (`FUN_10d8_49c6`/`FUN_10d8_4a86`, `stars.exe.export.c:89725`,
  `91725`) that temporarily swaps `DAT_1128_59fa` to select which race's production-queue context to
  cost out before restoring it, and a related report row that OR's a nibble into `DAT_1128_59fb`'s
  low bits per the same context. Both readers are UI/report code, not economy or combat logic,
  corroborating (without individually naming) the "per-race UI/session preference" reading.
- **Opcode `0x1d`** writes a dynamically-grown list of small, fixed-size (4-byte) tagged entries onto
  a record found by matching an id plus owning-race pair in yet another distinct table (28-byte
  stride, separate from the fleet, minefield, and design tables identified above or elsewhere in this
  document). This revises the previous "message/text-list" guess: the entries carry small bitfields
  used to match/clear a corresponding old entry against each new one (consistent with typed target
  references, not raw text bytes), making some other per-object "task/reference list" more likely; the
  specific object and field types were not confirmed. **Firmed up:** the case body
  (`stars.exe.export.c:30375`-`30456`) was read directly. It resolves the target record by a *linear*
  scan of a table rooted at `DAT_1128_00b6`/`DAT_1128_4b9c` (matching id, then owning race) — a
  different lookup style from `0x23`'s binary-searched minefield table, consistent with these being
  distinct object populations. An empty entry list (a 2-word payload carrying only the lookup key) is a dedicated **delete**: it frees
  the entry's dynamic buffer (`FUN_1060_04e6`) and zeroes the pointer outright rather than shrinking
  it to zero entries. A non-empty payload grows/reallocates the buffer (`FUN_1060_057e`/`FUN_1060_053e`)
  to fit, then for each new 4-byte entry scans the existing entries for one sharing the new entry's
  masked "type" bits (mask `0x7f0`) and matching two further masked sub-fields (mask `0x1fc00`, and
  mask `0xe` of the high word) — clearing the matched old entry's bits if found, or clearing the *new* entry's own bits if
  no match exists — before the whole new set is copied in and the entry count byte updated. This
  match-and-clear-the-old-one-first shape is consistent with a per-object list of typed references to
  *other* objects (e.g. "who currently has a pending order targeting me"), as the previous pass
  guessed, but the specific object and reference types were still not identified.
- **Resolved by the step-6 pass (§10): the record layer has no compression.** What the two
  sentences below call a candidate "compression step" is unrelated: the per-record transform is the
  keystream cipher of §10.4, and `FUN_1040_35a4` is only the string-field packer used by opcodes `0x1e`
  and `0x2c`. *Earlier text, kept for the record:* the exact compression algorithm for record types with
  high opcode bits set (§1's Open Question)
  was still not analyzed directly, but `FUN_1040_35a4` (see `ship-design-and-components.md` §12) is
  offered as a candidate for the same or a closely related mechanism, since it performs exactly the
  kind of generic nibble-oriented variable-width unpacking that a "compression step" of this kind
  would need — not confirmed identical, since it was only observed being invoked for one string
  field (opcodes `0x1e` and `0x2c`), not from the record-header-level dispatch §1 describes.

**Exhaustively re-checked this pass: there are no further case labels left to trace.** An independent, direct scan of the switch's complete body (`stars.exe.export.c:29625`-`30659`, matching this section's own cited line range exactly) for every `case` label — not just the ones this document had already discussed — finds exactly **22 case labels total, no `default:` label**, and every single one is already named and characterized above: `1`, `2`, `0x19` (the shared width-tier triple), `3`, `4`, `5` (the list-diff triple), `10`, `0xb` (bit/nibble toggles), `0x17`, `0x18`, `0x25` (fleet-transfer/split/selection-broadcast), `0x1b` (design create/delete), `0x1d`, `0x1e` (battle-plan write or delete; first misread as a component-slot write), `0x22`, `0x23` (minefield edit), `0x24`, `0x26`, `0x2a`, `0x2b`, `0x2c` (fleet rename), and `0x2e`. This document's earlier "roughly 20 opcodes... several case labels not reached" framing undercounted slightly (the true total is 22, not "roughly 20") but was otherwise accurate in spirit — the large line count of this switch (~1,035 lines) comes from how much code sits inside a handful of cases (the shared `1`/`2`/`0x19` block alone runs from line `29691` to past `29850`), not from many additional, still-untraced case labels. **This closes the open item: the opcode table is complete as far as case labels go.** What remains open is only the fine-grained field-by-field decoding *within* certain already-identified cases (e.g. `0x22`'s and `0x1d`'s exact field semantics, `0x22` has since been identified as the research-settings order; `0x1d`'s field semantics remain uncertain) — not any further unenumerated opcode. (The record-level "compression step" once listed here does not exist; see §10.4.)

### 10. Header record, file kinds, record cipher and the host's loaders (added by the step-6 pass)

Line numbers are `stars.exe.export.c`; the header field layout and the DOS-level facts were cross-checked against the raw executable. This section completes §1 (record layer), §4 (naming) and §7 (validation), and explains how `turn-generation-engine.md` §1a uses them.

**10.1 Record types seen at the file layer.** The 2-byte record header word gives the type in bits 10-15 and the payload length in bits 0-9. Besides the decoder opcodes of §9 (the order records inside an orders file) the loaders and writers use: type 0 = end marker/trailer; 6 = one player's record (`:44188`); 7 = game/universe definition (`:44077`); 8 = file header; 9 = identity record of an orders file; 40 (`0x28`) = player-to-player message; and in host-to-player files `0x2b` = visible special objects, `0x2d` = 24-byte score-history record per race, `0x24` = the 4-byte per-race number of §9. In the history file (`FUN_1048_84c8`, `:31243`-`31423`) two further client-state types appear: `0x21` (33) = the 392-bit message-type filter bitmap (49 bytes; `client-ui-dialog-catalog.md`, Messages), and, for a computer player only, `0x29` (41) = the AI memory block (the hub table, or personality 4's per-planet packet bookkeeping), split into records of at most 1,023 bytes and read back only when the player's computer flag is set (`ai-opponent-behavior.md` §16). (The planet/fleet record types of a turn file were not enumerated here.)

**10.2 The header record (type 8, 16-byte payload; written by `FUN_1070_55aa`, `:48756`-`48801`).**

| Offset | Size | Content |
|---|---|---|
| 0 | 4 | magic, four ASCII bytes (data offset `0x9dc`) |
| 4 | 4 | game id (compared with `DAT_1128_0070`; for a new game it is the tick count at creation, except in a tutorial game, where it stays fixed, `:51803`-`51808`) |
| 8 | 2 | version word: bits 12-15 major (2), bits 5-11 minor, bits 0-4 revision. This build writes `0x2a60`. Loaders require major 2 and `(word & 0xfe0)` strictly between `0x61f` and `0xa80`; an older file gets dynamic string 1235 (the file was made by an older, incompatible version), a newer one string 714 (the file was made by a newer version and the player must upgrade), `:46535`-`46545` |
| 10 | 2 | turn number (`DAT_1128_0082` at write time) |
| 12 | 2 | bits 0-4: player number 0-15, or 31 for "none" (host and universe files); bits 5-15: an 11-bit salt (`random(2000)` plus the tick count, `:48786`-`48789`) |
| 14 | 1 | file kind (10.3) |
| 15 | 1 | flags: bit 0 (`0x100` of the word) *submitted* (from `078a` bit 4); bit 1 (`0x200`) *in use by a running instance* (from `078a` bit 3); bit 2 (`0x400`) *multi-turn* (later turns were appended; a type-0 trailer holds the newest turn number, 10.6); bit 3 (`0x800`) *game over* (written only into the host file, from `078c` bit 0, `:48794`-`48798`); bit 4 (`0x1000`) a status flag this build writes as 0 and that readers copy (`:46489`, `:30755`-`30760`; meaning not pursued); bits 5-7 the 3-bit per-generation nonce (`DAT_1128_0080` bits 9-11, `:48785`) |

**10.3 File kinds.** The kind byte at header offset 14 equals the "purpose" code of the name builder `FUN_1070_546e` (`:48647`) and of the loader `FUN_1070_2dfc` (`:46421`): **0** universe file `<game>.xy` (`:51811`), **1** orders file `<game>.x<N>` (`:31047`), **2** host file `<game>.hst`, **3** turn file `<game>.m<N>`, **4** history file `<game>.h<N>` (`:31266`), **5** race file (`:95014`, matching §7's tag `5`). `N` is the player number plus one; `<game>` is the game path with its extension stripped. This answers the open question in §7: the kind byte is written for every kind, but only the race loader compares it; the others select their kind by file name and validate game id, turn and (for orders) nonce instead.

**10.4 The record cipher.** All payloads except types 0 and 8 are XORed with a keystream (`FUN_1040_1a08`, `:21818`-`21850`, four bytes per step, the tail bytes from one more step), on write by `FUN_1070_594a` (`:48953`-`48972`) and on read by `FUN_1070_31f4` (`:46673`-`46690`). The keystream is the difference of two multiplicative congruential generators (`FUN_1040_1940`, `:21793`-`21811`: multipliers 40014 and 40692, moduli 2,147,483,563 and 2,147,483,399). Every header record reseeds it (`FUN_1040_18cc`, `:21770`-`21790`): each generator's start value comes from a 64-entry table of 16-bit constants (small primes) held in the seeding routine's own code segment, segment 9 (selector `0x1040`), at offset `0x1476`, not in the data segment at that offset, selected by the salt's two 5-bit halves (salt bits 0-4 and 5-9; bit 10 of the salt decides which of the two generators takes the upper half, entries 32-63, and which the lower half), and `((game id & 3) + 1) * ((turn & 3) + 1) * ((player & 3) + 1) + flag bit 12` outputs are discarded before use. It is an obfuscation keyed by public header fields, not a compression scheme and not a secret.

**10.5 Orders file layout (writer `FUN_1048_80f8`, `:30953`-`31109`).** Header (kind 1); one type-9 record of 17 bytes (a word holding the byte length of the order area followed by a 15-byte identity block; the block's content and what the host does with it are deliberately not described, `turn-generation-engine.md` §1 step 7); the buffered order records (§2); the type-40 message records (each is a 12-byte node prefix followed by text; the loader treats the first four bytes as a link); and a type-0 end marker with no payload. Before writing, if the acting player is not a computer player and the last buffered record is not already a type-46 record, a type-46 (`0x2e`) record is appended when the client's default production template (template slot 0) differs from the copy in the race record (corrected: not a display name; see §9 opcode `0x2e`) (`:30987`-`31020`). Host and turn files end with a type-0 record whose 2-byte payload is the turn counter (`:47735`).

**10.6 The two host-side loaders.** `FUN_1048_7c66` (`:30660`-`30888`) is the orders loader used by turn generation, by `FUN_1070_0502` when a client opens its own game (`:45195`) and by the ready poll's fallback (`:9140`); its checks are listed step by step in `turn-generation-engine.md` §1a (game id; turn not below the host's; nonce; end marker) and it neither checks the kind byte, the header's player field nor the submitted flag. `FUN_1070_2dfc` (arguments: purpose code with flag bits, player, mode; `:46421`-`46560`) is the general validator used for every other open: it builds the file name, opens it (retrying while generating), reads the header and requires type 8, major version 2 and the version range (else dynamic string 13, 1235 or 714); requires the header's player field to equal the expected player (else string 3); then, if no game is loaded yet (`DAT_1128_0070` = 0) it accepts, and otherwise requires the game id to match (else string 29). Purpose 4 (history) then accepts at once. For the others: with flag `0x2000` in the purpose and the multi-turn bit set in the file, it seeks four bytes before the end of the file and takes the turn number from the first word of the record there (a record that is neither type 0 nor 2 bytes long fails the load); a turn number different from the game's gives string 28 (the file is stale), except that a freshly-discarded game (turn 0) adopts the file's turn and nonce; a host file with the in-use bit set (and not while `078a` bit 3 is set) asks the user whether to open it anyway (string 21); while hosting (`078a` bit 1 set, bit 2 clear), a file whose submitted bit is clear is refused silently and remembered in `078c` bit 7; for purpose 1 a nonce mismatch gives string 29. On success it stores the version word in `DAT_1128_0710`, copies header flag bit 12 into `078c` bit 2, and (with flag `0x1000` in the purpose) rewinds the file. Purpose codes seen at its call sites: 0 (universe, `:44074`), 1 (orders: ready poll `:30910`, AI pass `:8319`), 2/3 (host/turn, `:44376`), 4 (history, `:44135`), `0x2003` (turn file, trailer read, `:46578`), and `0x0002` with an explicit `mode` (`:46619`).

**10.7 Header-flag rewriter.** `FUN_1070_569a` (arguments: file kind, player, which flag, new value; `:48808`-`48947`) opens an existing file for update, checks the header the same way, and flips one flag in place: `which` 1 = in-use (`0x200`), 2 = submitted (`0x100`), 4 = multi-turn (`0x400`), 8 = the computer-player flag inside player `player`'s type-6 record (the record is re-enciphered and rewritten). Callers: the manual ready toggle (`:5869`), host open/close (`:8792`, `:8809`), the end of turn generation (step 31), the Host Mode window's change-player-type menu (`:9394`-`9395`, which sets or clears the computer-player flag in the slot record and in the turn and host files, and also inverts the slot's 4-byte number at `DAT_1128_59ce`) and `FUN_1070_45d0` (sets multi-turn on an existing turn file, used by step 39).

## Cross-references

- The 147-byte design record and 16+10 slot layout (§3) match figures independently confirmed in `race-traits.md` and are relevant to `race-designer-ui-and-availability.md`.
- The 18-byte production-queue-shaped list entries (§3) are relevant to the queue-record format described in `production-queue.md`.
- The 100-turn score-history retention window (§3) is relevant to the Score screen described in `client-ui-dialog-catalog.md` and to `victory-conditions.md`'s score-threshold condition.
- The `.r1` race-file save and load paths (§7) read/write the same 192-byte race-data block whose point-cost formula is fully reconstructed in `race-traits.md` §1a; this document does not repeat that formula, only the file read/write mechanism around it.
- The message-text blob mentioned in §6 (full state-dump mode) is backed by the message record store documented in `client-ui-dialog-catalog.md`'s "Messages" section (message type/field encoding, read-bitmap, and Next/Previous filter navigation).
- The delta-record builder (§8) is the mechanism `ship-design-and-components.md` §10 anticipated ("a full write-up belongs with a future save/turn-file specification") when it noted that the design create/delete and component-slot-write opcodes it documents are cases inside this same decoder switch; that document's opcode-level detail on those two specific cases is not repeated here.
- §9's opcode enumeration names the decoder switch `ship-design-and-components.md` §10 only referenced by segment: `FUN_1048_68a8`. §9 also cross-references that document's §12 (`FUN_1040_35a4`, once offered as a candidate "compression step" but now known to be only the string-field packer; the record layer has no compression, §10.4) and confirms §8's hedge that opcodes `1`/`2`/`0x19` are the three adaptive-width tiers of the same fleet-record-diff family.
- The record-payload transform referenced in §1 (now identified as the keystream cipher of §10.4, not a decompression helper) was independently observed being invoked from the `.r1` loader's payload-copy step (§7) as well as from ordinary incoming records, corroborating that it is a fully generic, not turn-file-specific, mechanism.
- §10 is the file-layer companion of `turn-generation-engine.md` §1a (how step 6 loads and applies orders files) and §1 step 37/39 (how the previous files are moved and the next ones written).

## Open Questions

- ~~Several opcodes referenced by the decoder (beyond the ones itemized in §3) were identified only by number, with their purpose not determined — a complete opcode table was not assembled~~ **RESOLVED — the case-label table is now exhaustively complete.** The decoder switch is named (`FUN_1048_68a8`) and every one of its 22 case labels has a documented identity (see §9's new note directly above its "This remains a partial enumeration" sentence, now itself struck through): the `1`/`2`/`0x19` width-tier triple, the `3`/`4`/`5` list-diff triple, `10`/`0xb` bit/nibble toggles, `0x17`/`0x18`/`0x25` fleet-transfer/split/selection-broadcast, `0x1b` design create/delete, `0x1d` and `0x1e` (first read as a component-slot write and a narrower-but-uncertain typed-reference list; `0x1e` is now known to be the battle-plan write or delete, §9), `0x22`/`0x23`/`0x24`/`0x26`/`0x2e` (per-race UI preference, minefield edit, the per-race identity number and player display name pair — corrected by the step-6 pass, see §3 — and the relationship-write opcode), and `0x2a`/`0x2b`/`0x2c` (narrow numeric-array diffs and fleet rename). A direct, exhaustive scan of the full ~1,035-line switch body found no further `case` labels and no `default:` label — the large line count reflects how much code a few cases (chiefly `1`/`2`/`0x19`) each contain, not additional untraced opcodes. What remains open is only the field-level decoding *within* the already-identified `0x1d` case (`0x22` is now known to be the research-settings order, and the compression-step question in the next bullet is resolved) — not any unenumerated case.
- ~~The exact compression/decompression algorithm applied to "extended" record types was not analyzed in detail.~~ **Resolved by the step-6 pass: there is no compression; the record-level transform is a keystream XOR cipher (§10.4).** *Earlier text:* **Partial lead in §9**: `FUN_1040_35a4` (a nibble-oriented variable-width decoder, cross-referenced from `ship-design-and-components.md` §12) is a plausible candidate for the same or a related mechanism, but it was only observed decoding one string field (opcodes `0x1e`/`0x2c`), not invoked from the record-header-level dispatch this Open Question is actually about — so this remains open.
- ~~Whether the 100-entry relationship-write opcode is definitively the same table as `diplomacy-relations.md`'s relationship system~~ **Resolved — confirmed identical**, not merely similarly-shaped. See §3's updated bullet.
- §8's adaptive-width encoding was fully traced for one 6-field fleet-record family (opcodes referenced in §3's "Fleet record updates" bullet, most plausibly but not certainly the same three opcodes); whether the identical builder/coalescing machinery is reused verbatim for other opcode families (e.g. the narrow numeric-array diffs bullet in §3) or is merely structurally similar, independently implemented per opcode family, was not checked call-by-call for every opcode.
- ~~The exact field layout of the 6-field fleet-record diff (which of the 6 fields is mass vs. which are cargo/mineral/colonist types) was not individually identified field-by-field~~ **Resolved, with a hedge — see §8's new paragraph above.** The shared field-setter (`FUN_1050_3cee`) exposes only 5 real fields for the fleet case, not 6: fields 0-2 are the unconditional mineral cargo types, field 3 is gated on the fleet's task-type field reading `7` (most plausibly colonists/"Colonize"), and field 4 is separately capacity-checked (most plausibly fuel). The document's "mass-only compact form" does not appear to be part of this same dispatch and was not traced to a specific opcode.
- **Fully resolved by the step-6 pass (§10.3):** the kind byte is written for every file kind (0 universe, 1 orders, 2 host, 3 turn, 4 history, 5 race); only the race loader compares it. ~~The one-byte "file kind" tag in the shared game-identity header (§7) was confirmed to take one specific value for race files; the corresponding values for turn-order files, host/history files, and other player-state files (§4) were not individually enumerated in this pass.~~ **Substantially narrowed this pass — see §7's new "Follow-up pass" note.** The other file kinds' loader was located (`FUN_1070_2dfc`, `stars.exe.export.c:46421`), and it turns out not to validate a stored file-kind byte at all — it authenticates a loaded file by matching the file's own game-identity fields against the current game plus a caller-supplied expected "purpose" code, never by reading a self-describing tag out of the file the way the race-file loader does. This is a well-evidenced explanation for why no second tag-byte comparison exists to find (the two file families use different authentication mechanisms entirely). The caller-supplied purpose codes are decoded in §10.3/§10.6: they equal the file-kind numbers (0 universe, 1 orders, 2 host, 3 turn, 4 history), and `0x2003` is the turn-file code with the trailer-read flag `0x2000`.
