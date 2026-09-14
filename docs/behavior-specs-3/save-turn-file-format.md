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

A dedicated loader reads an incoming stream either from a bundled internal resource (used for pre-made scenarios) or from an open file/stream, validating a magic/version and game-identity header before trusting any of its content. Applying a full buffer of records replays each one through a shared decoder in sequence, short-circuiting if any individual record fails to apply.

### 6. Full state-dump mode

Beyond incremental per-turn deltas, the same record format is used to write a **full snapshot** of everything one player is currently allowed to see about another race: a planet header record, every visible fleet (using either the compact or full fleet-record form depending on stack size), a message-text blob, other races' visible minefields and designs, and the score-history snapshot described above (§3) — again chunked using the same large-blob opcode where needed.

## Cross-references

- The 147-byte design record and 16+10 slot layout (§3) match figures independently confirmed in `race-traits.md` and are relevant to `race-designer-ui-and-availability.md`.
- The 18-byte production-queue-shaped list entries (§3) are relevant to the queue-record format described in `production-queue.md`.
- The 100-turn score-history retention window (§3) is relevant to the Score screen described in `client-ui-dialog-catalog.md` and to `victory-conditions.md`'s score-threshold condition.

## Open Questions

- Several opcodes referenced by the decoder (beyond the ones itemized in §3) were identified only by number, with their purpose not determined — a complete opcode table was not assembled.
- The exact compression/decompression algorithm applied to "extended" record types was not analyzed in detail.
- Whether the 100-entry relationship-write opcode is definitively the same table as `diplomacy-relations.md`'s relationship system, versus a similarly-shaped but distinct per-race table, was not conclusively proven.
