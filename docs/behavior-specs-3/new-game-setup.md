# New Game Setup and Galaxy Generation

Behavior specification for the new-game creation flow and galaxy/universe generation in the 1995-2000 4X game *Stars!*. This document is derived directly from the exported client's decompiled logic; it is new material, since no prior document in this set covers game setup or galaxy generation. Facts are described in generic terms; no original identifiers or literal code are used.

## Overview

Creating a game follows one of two paths: a **Simplified** single-screen setup, or a **Detailed** three-page wizard (general settings, player-slot assignment, victory conditions). Both paths write into the same underlying game-setup draft, and both ultimately invoke the same galaxy generator once settings are finalized. A separate batch/scripted mode allows a game to be created from an external configuration source without going through the interactive wizard at all.

## Mechanics

### 1. Detailed setup wizard pages

The three detailed-setup pages are confirmed to be sequential pages of one wizard flow (each sets its window title from a shared string combined with its own page number):

1. **General settings** — Galaxy Size (5 mutually-exclusive options), Star Density (4 options), a third 4-option setting (plausibly "Starting Distance" between players, by analogy with widely-known Stars! setup screens, though this was not independently confirmed), and at least 7 independent boolean game-option flags whose individual meanings could not be determined (no label text survives in the analyzed material). These option flags are consumed directly by the galaxy generator (§3) — for example, one flag affects whether certain player types receive an extra starting planet, another affects a mineral-concentration bonus, and another affects how randomization is seeded.
2. **Player-slot assignment** — a grid of per-slot rows, each encoding a small state (open / default-unassigned-or-computer-controlled / a specific race assigned / a relation-table-linked assignment) plus a sub-index identifying which race or preset. See `client-ui-dialog-catalog.md`'s "Slot/equipment editor" family for the shared UI patterns this reuses.
3. **Victory conditions** — see `victory-conditions.md`.

### 2. Simplified setup

A single-screen path that auto-selects a reasonable player count scaled to the chosen galaxy size (roughly 2 players for the smallest galaxy size, up to 4 for a small galaxy, and 12-15 for a large one), using weighted random selection within that range rather than a single fixed default. It also seeds the victory-conditions year-gate meta-setting proportional to galaxy size (see `victory-conditions.md` §1).

### 3. Galaxy generation

Once settings are finalized, a single galaxy-generation routine runs:

- **Galaxy diameter** = `(gallaxySizeSetting + 1) * 400`, where the size setting is 0-4 — producing the diameters 400/800/1200/1600/2000 light-years for the five galaxy-size options.
- **Star count** is derived from `diameter² / 5000`, further adjusted by the chosen density setting, and hard-capped at **999** stars regardless of the formula's raw output.
- **Star placement** over-generates roughly 8/7 more candidate positions than needed, applies rejection sampling against a minimum-separation distance, and — gated by one of the boolean option flags from §1 — runs an optional relaxation/anti-clumping pass afterward to reduce visual star clustering.
- **Star naming** draws from a fixed pool of 999 unique names with a duplicate-prevention bitmap.
- **Home-world mineral concentrations** are randomized to a value between 100 and 299 (inclusive) for each of the three minerals.
- **Per-player starting fleet composition** is determined by a numeric "player type" code (values 0-9 observed, presumably corresponding to Human plus several built-in AI personality/difficulty presets — see `ai-opponent-behavior.md`), which selects which starting-ship templates get copied into that player's initial fleet.
- **Second home planet.** Certain player types, on non-smallest galaxies, receive a second home-world-tier planet placed via a nearest-valid-position search (up to 100 attempts within galaxy bounds) — an asymmetric starting bonus tied to player type and galaxy size rather than a universal rule.

### 4. Batch/scripted setup

An external key/value configuration source can drive game creation without the interactive wizard. Recovered validated value ranges for this format (their exact real-world meaning inferred by position/range, not independently confirmed against UI labels):

| Key | Range | Step |
|---|---|---|
| 1 | 20 – 100 | 5 |
| 2 (paired values) | 8 – 26 and 2 – 6 | — |
| 3 | 1000 – 20000 | — |
| 4 | 20 – 300 | 10 |
| 5 | 10 – 500 | 10 |
| 6 | 10 – 300 | 10 |
| 7 | 15 – 900 | 10 |
| (flags) | 7 boolean toggles | — |
| Player count | 1 – 16 | — |
| Per-player (paired values) | 0 – 6 and 0 – 4 | — |

The per-player paired values resolve to an index into a fixed table of pre-made AI race/personality templates (indexed by race-template choice × personality/difficulty choice). These ranges line up closely with the victory-condition scaling table in `victory-conditions.md` §1, suggesting this configuration format is largely a scriptable proxy for the same settings the interactive wizard pages expose.

## Cross-references

- `victory-conditions.md` documents the third wizard page and the runtime evaluation of the settings it configures.
- `ai-opponent-behavior.md` documents the personality-dispatch codes referenced by the "player type" concept in §3 and §4 above.
- `client-ui-dialog-catalog.md`'s "New-session flow" entry already describes the Simplified-vs-Detailed structure abstractly; this document adds the concrete parameter set, ranges, and the galaxy-generation algorithm itself.

## Open Questions

- The exact meaning of the third General-settings radio group (guessed as "Starting Distance") and of the 7+ boolean game-option flags was not confirmed — no label text survives in the analyzed material.
- The precise semantics of the numeric "player type" codes 0-9 (which specific code corresponds to Human versus which built-in AI personality) were not confirmed.
- Whether the batch/scripted configuration format is fully equivalent to the interactive wizard, or supports settings the wizard does not expose (or vice versa), was not established.
