# Victory Conditions

Behavior specification for game-ending victory conditions in the 1995-2000 4X game *Stars!*. This document is derived directly from the exported client's decompiled logic — both the new-game setup screen that configures these conditions and the turn-generation routine that evaluates them once per turn. No prior document in this set covers victory conditions; this is new material. Facts are described in generic terms; no original identifiers or literal code are used.

## Overview

A game can be configured, at setup time, with a set of **10 configurable victory items**: 7 independently-toggleable boolean conditions (each pairing an enabled flag with a numeric threshold), one paired numeric value that rides alongside one of the 7 conditions without its own toggle, one derived "how many of the enabled conditions must be simultaneously true" meta-setting, and one "minimum game year before victory can be declared" meta-setting. Every turn, the turn-generation engine evaluates these against each race's current standing and declares victory once the configured bar is cleared.

## Mechanics

### 1. Configuration (new-game setup)

The setup screen backing this system exposes exactly **7 checkboxes**, each toggling one victory condition on or off, plus an adjustable numeric magnitude for each (steppable via a spinner control, one step at a time or five steps at once via Page Up/Down). Internally, each condition is stored as a single byte: the top bit is the enabled flag, the low 7 bits are a raw magnitude value clamped against a per-condition maximum.

The raw magnitude for each condition is transformed into its real-world threshold by a condition-specific scaling formula. Reconstructed from the code's control flow (not from any visible label text, so treat the *meaning* column as informed inference rather than a confirmed fact):

| Condition # | Scaling formula | Approximate range | Likely meaning |
|---|---|---|---|
| 1 | (raw + 4) × 5 | ~20 – 100 | Percentage of planets owned |
| 2 | raw + 8 | ~8 – 26 | Tech level required in a number of fields |
| 3 | raw + 2 | ~2 – 6 | Number of fields required at that tech level (paired with condition 2; not independently toggleable) |
| 4 | (raw + 1) × 1000 | 1000 – 20000 | Score exceeds this value |
| 5 | (raw + 2) × 10 | — | Numeric threshold (exact meaning not confirmed) |
| 6 | (raw + 1) × 10 | — | Numeric threshold (exact meaning not confirmed) |
| 7 | (raw + 1) × 10 | — | Numeric threshold (exact meaning not confirmed) |
| 8 | (raw + 3) × 10 | — | Numeric threshold (exact meaning not confirmed) |
| — (derived) | min(raw, count of currently-enabled conditions among 1-7 excluding condition 3) | 1 – 7 | Number of the enabled conditions that must be true simultaneously for anyone to win |
| — (year gate) | (raw + 3) × 10, seeded proportional to galaxy size | — | Minimum game year before a victory can be declared at all |

Condition 3 deliberately has no checkbox of its own — it is a secondary numeric value that only matters when condition 2 (tech level required) is enabled, representing "in how many fields" that tech level must be reached.

A "Simple New Game" setup path auto-seeds the year-gate meta-setting proportional to the chosen galaxy size (larger galaxies get a longer minimum-year floor before victory can trigger), rather than leaving it at a fixed default.

### 2. Runtime evaluation

Once per turn, the turn-generation engine evaluates every enabled condition against every race's current standing, using the same per-condition storage the setup screen writes to (confirming the setup screen and the runtime check share one underlying data structure, not two independently-maintained copies). For each race, it counts how many enabled conditions that race currently satisfies; a race meeting at least the required count (the derived meta-setting above) becomes eligible to win, provided the game year has also reached the year-gate meta-setting. When a sole eligible leader emerges (or a tie is detected), all races are notified.

## Cross-references

- The Score screen documented in `client-ui-dialog-catalog.md`'s "Score display" entry shows a 9-category per-player comparison and per-turn history, but the exact score-value computation underlying victory condition 4 (Score exceeds N) was not independently traced to that screen's data in this analysis — the two subsystems likely read the same underlying per-player score record, but this was not directly confirmed. See Open Questions.
- `new-game-setup.md` covers where the victory-condition setup screen sits within the overall new-game wizard flow.
- `turn-generation-engine.md` §1 lists the overall per-turn phase order this evaluation fits into (not separately itemized as its own numbered phase above, since its exact position in the master sequence was not pinpointed during this analysis).

## Open Questions

- The exact real-world meaning of conditions 5, 6, 7, and 8 (only their numeric scaling was recovered, not a label or description) — plausible candidates include "own N planets," "field N capital ships," and "exceed the second-place race's score by N%," but none of these are confirmed.
- Whether the Score screen's per-player score computation is the same value victory condition 4 reads, or a separately-maintained figure, was not confirmed.
- The exact position of the victory-condition evaluation within the master turn-generation phase order (`turn-generation-engine.md` §1) was not pinpointed to a specific phase number.
