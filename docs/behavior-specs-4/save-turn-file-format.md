# Save and Turn-File Format

Behavior specification for the on-disk turn-order and save-file protocol in the 1995-2000 4X game *Stars!*. This document is derived directly from the exported client's decompiled logic; it is new material, since no prior document in this set covers file formats. Facts are described in generic terms; no original identifiers or literal code are used.

## Overview

Every turn, each player's client writes a compact, opcode-tagged log of every change the player made (order edits, design changes, renamed objects, and so on) to a per-player turn-order file. The host reads every submitted file, applies each recorded change, resolves the turn (see `turn-generation-engine.md`), and writes back a new per-player state file for the following turn. The same record-stream format is used in both directions (player-to-host and host-to-player), and also for a full "here is everything you're allowed to see" state dump rather than only incremental deltas.

## Mechanics

### 1. Record stream format

The stream is a flat sequence of variable-length records. Each record begins with a 2-byte header: the low 10 bits encode the **payload length in words** (capping any single record's payload at 1,023 words), and the high bits encode a **record-type code** (an opcode). A record whose opcode has certain high bits set is passed through an additional compression/decompression step before its payload is interpreted; otherwise the payload is read as-is.

### 2. Local change buffer and coalescing

While a player is editing orders during their turn, changes are accumulated into a local buffer (capped at roughly 32,000 bytes) rather than written to disk immediately. Appending a new change record first checks whether the buffer's most recent record already targets the same object; if so, the previous record is rewound and replaced rather than appended alongside it — i.e., **edits to the same object are coalesced into their net effect**, not stored as a full edit history. This buffer is what gets flushed to the actual turn-order file when the player ends their turn.

### 3. Observed opcode families

Recovered opcodes and their apparent purpose (numeric values are internal and omitted; grouped here by function):

- **Object selection/active-context changes** — which object is currently selected, and broadcasting a set of currently-selected fleet IDs (up to roughly 510 at once).
- **Design create/delete** — creating or deleting a ship or starbase design. Design slots are confirmed as **16 regular hull designs plus 10 starbase designs per race (26 total)**, each design record occupying a fixed 147-byte layout.
- **List diffs (delete/insert/replace)** — applied to two different kinds of variable-length list: an 18-byte-per-entry production-queue-shaped list, and a 9-int-per-entry fleet waypoint/task list. The same three opcodes are reused for both list types, disambiguated by an accompanying target-object id.
- **Bit/nibble toggles** — small single-bit or single-nibble field changes (e.g., a checkbox-style option).
- **Narrow numeric-array diffs** — 1-byte, 2-byte, or length-tagged encodings chosen to minimize wire size for small changes to per-field numeric arrays.
- **Fleet record updates** — a compact form (mass-only) and a fuller form (cargo plus waypoint data), the fuller form's cargo section carrying 3 unconditional mineral/colonist fields plus 1 additional field sent only if it changed.
- **String/name fields** — planet names, fleet names, message text, and similar are capped at **31 characters**.
- **Relationship changes** — a 2-byte write into a **100-entry per-race table**, consistent with (though not independently proven identical to) the diplomacy relationship table described in `diplomacy-relations.md`.
- **Home-system coordinates and player display names** — gated to apply only in networked (non-hotseat) games.
- **Score-history snapshot** — a per-turn score record, retained only for the most recent 100 turns (older entries are not written/retained).
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
- Reads one leading record and validates it as a game-identity/version header — the **same generic header record already documented in §5** as gating any incoming stream, not a race-file-specific invention. Two fields are checked: a version-range field (required to fall within a specific narrow numeric band) and a one-byte "file kind" tag, which must equal a specific fixed value distinguishing a race file from every other file kind that flows through this same generic reader (turn-order files, host/history files, etc. presumably carry their own distinct tag values, none of which were enumerated in this pass).
- Reads a second record and requires it to carry a specific record-type tag, then copies its full payload out as the 192-byte race-data block.
- Reads a third record (the trailing 2-word checksum) and recomputes the same XOR checksum over the just-read 192-byte block, using the identical checksum routine the save path uses; a mismatch aborts the load and no race data is accepted.
- Only on a full match does it copy the 192-byte block into the live race-data buffer that both the save path and the rest of the race-design system read from.

This resolves the open question from the prior pass definitively as **(a) found**: `.r1` loading exists, is driven by the same generic record-stream reader/header-validation machinery as every other file this client reads (confirming §5's mechanism applies uniformly, including to race files), and adds its own file-kind tag plus the same 192-byte-block-then-checksum framing the save path writes — the read and write sides were independently confirmed to agree on record order and content. The on-disk order is therefore **data block first, checksum second** (not checksum-first as the prior pass's save-only inspection guessed — that guess is corrected here now that the read side's record-by-record validation order is visible).

### 8. Building a change record: field-level diffing, adaptive width, and coalescing (turn-order delta-record protocol)

Confirmed by inspection of the exported client: beneath the opcode/record-stream layer already described in §1-§3, there is a distinct, generic mechanism responsible for actually *building* a change record from an in-game edit before it is ever appended to the buffer. This is the "turn-order delta-record protocol" — it is not a separate file format, but the encode-side counterpart to the record stream, and it is what §2's "coalescing" behavior and §3's "narrow numeric-array diffs" bullets were referring to at a level of detail neither had previously been traced to.

- **Trigger.** A shared field-setter function is the single entry point interactive UI code calls whenever a fleet's mass/cargo-like fields are changed (confirmed for a 6-field fleet record: it is handed both a pointer to the fleet's currently-stored field values and a pointer to the proposed new values). It compares old vs. new field-by-field; if every field is unchanged, nothing is recorded. If any field differs, it hands the two field-value sets to the delta-record builder described below, then overwrites the stored fields with the new values. The same shared "append a record" primitive underlies every opcode family in §3, not only this one, but this fleet-field case is the one traced in full end-to-end.
- **Encoding: adaptive per-field width.** The builder computes a signed delta for each of the (up to) 6 fields and tracks the single largest delta magnitude across all of them. That maximum then picks one of three encodings for the *entire* record: if every changed field's delta fits in a signed byte, a 1-byte-per-changed-field form is used; if it fits in a signed word, a 2-byte-per-changed-field form is used; otherwise a full 4-byte-per-changed-field ("long") form is used. Only fields that actually changed are written, flagged by a small bitmask alongside the record. This is a general-purpose narrow-encoding strategy, not a special case — it most plausibly explains this document's existing "Fleet record updates" bullet in §3 (its "compact form" and "fuller form" most likely correspond to the 1-byte and 4-byte tiers here, with a previously-unnoticed 2-byte middle tier), though that identification was not independently re-verified opcode-by-opcode in this pass and should be read as a refinement, not a certainty.
- **Coalescing (extends §2).** Before writing a new record, the builder checks whether the buffer's most-recently-appended record already targets the exact same object (matched by a packed pair of index nibbles carried in both records) *and* used this same family of opcodes. If so, rather than appending alongside it, the builder: decodes the previous record's per-field deltas back out (reversing whichever of the three width encodings it used), rewinds the buffer past that record entirely, adds the old and new per-field deltas together (producing each field's *net* delta across both edits), re-picks the narrowest of the three encodings for the combined magnitude, and writes a single merged record in its place. Repeated edits to the same object's same fields therefore never grow the buffer — they only ever update one record's net effect, re-tiering its byte width up or down as needed.
- **Buffer-append primitive.** A single shared low-level function does the actual buffer write: it packs the record header (opcode in the high bits, payload length in words in the low 10 bits — matching §1's record-header layout exactly), checks the running buffer length against the ~32,000-byte cap already described in §2 (flushing/warning before the cap would be exceeded), copies the payload in, and — unless the record's own opcode is a specific always-excluded value — updates the "most recently appended record" tracker that the coalescing check above reads. A record written with that excluded opcode value is therefore never a candidate for coalescing, in either direction.

## Cross-references

- The 147-byte design record and 16+10 slot layout (§3) match figures independently confirmed in `race-traits.md` and are relevant to `race-designer-ui-and-availability.md`.
- The 18-byte production-queue-shaped list entries (§3) are relevant to the queue-record format described in `production-queue.md`.
- The 100-turn score-history retention window (§3) is relevant to the Score screen described in `client-ui-dialog-catalog.md` and to `victory-conditions.md`'s score-threshold condition.
- The `.r1` race-file save and load paths (§7) read/write the same 192-byte race-data block whose point-cost formula is fully reconstructed in `race-traits.md` §1a; this document does not repeat that formula, only the file read/write mechanism around it.
- The message-text blob mentioned in §6 (full state-dump mode) is backed by the message record store documented in `client-ui-dialog-catalog.md`'s "Messages" section (message type/field encoding, read-bitmap, and Next/Previous filter navigation).
- The delta-record builder (§8) is the mechanism `ship-design-and-components.md` §10 anticipated ("a full write-up belongs with a future save/turn-file specification") when it noted that the design create/delete and component-slot-write opcodes it documents are cases inside this same decoder switch; that document's opcode-level detail on those two specific cases is not repeated here.
- The record-payload decompression helper referenced in §1 was independently observed being invoked from the `.r1` loader's payload-copy step (§7) as well as from ordinary incoming records, corroborating that it is a fully generic, not turn-file-specific, mechanism.

## Open Questions

- Several opcodes referenced by the decoder (beyond the ones itemized in §3) were identified only by number, with their purpose not determined — a complete opcode table was not assembled.
- The exact compression/decompression algorithm applied to "extended" record types was not analyzed in detail.
- Whether the 100-entry relationship-write opcode is definitively the same table as `diplomacy-relations.md`'s relationship system, versus a similarly-shaped but distinct per-race table, was not conclusively proven.
- §8's adaptive-width encoding was fully traced for one 6-field fleet-record family (opcodes referenced in §3's "Fleet record updates" bullet, most plausibly but not certainly the same three opcodes); whether the identical builder/coalescing machinery is reused verbatim for other opcode families (e.g. the narrow numeric-array diffs bullet in §3) or is merely structurally similar, independently implemented per opcode family, was not checked call-by-call for every opcode.
- The exact field layout of the 6-field fleet-record diff (which of the 6 fields is mass vs. which are cargo/mineral/colonist types) was not individually identified field-by-field; the cargo-related side effects observed during replay (§8/§5) strongly suggest at least one field triggers a live mineral/colonist stockpile adjustment on the object gaining or losing cargo, but the exact field-to-quantity mapping was not confirmed.
- The one-byte "file kind" tag in the shared game-identity header (§7) was confirmed to take one specific value for race files; the corresponding values for turn-order files, host/history files, and other player-state files (§4) were not individually enumerated in this pass.
