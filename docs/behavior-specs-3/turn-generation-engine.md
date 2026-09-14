# Turn Generation Engine

Behavior specification for the "Generate New Turn" pipeline in the 1995-2000 4X game *Stars!* — the batch process that runs once per turn, after every player's orders are collected, to advance the game state and produce the next turn's files. This document is derived directly from the exported client's decompiled turn-processing logic; no public source documents the exact phase ordering or the mechanics below, so this is presented as new material rather than a correction to an existing specification. Facts are described in generic terms; no original identifiers or literal code are used.

## Overview

Turn generation is a single, strictly ordered master routine. It does not resolve all of one player's actions and then move to the next player in a naive way — instead, it runs a fixed sequence of *phases*, each phase sweeping across every relevant object (fleet, planet, race) once, before the next phase begins. This phase ordering matters: a fleet that is destroyed in the battle phase, for example, can no longer be acted on by phases that run afterward.

## Mechanics

### 1. Phase order

The master per-turn routine executes, in this order:

1. **Open and validate submitted order files** for every player; abort with an error if the turn file cannot be read.
2. **Randomize AI/player processing order** for the turn (a full reshuffle of the player index list, not a fixed slot order).
3. **Load and validate each player's submitted turn file** in that randomized order, with progress reported to the player generating the turn.
4. **Detect and flag duplicate/conflicting player identity records** (e.g., name/password collisions between two player slots).
5. **Tech-field "reveal" pass** — for every player, any technology that has become newly visible (e.g., due to scanning an enemy design using it) is flagged as known.
6. **Fleet-flag reset and starbase-reveal pass.**
7. A general per-fleet recompute pass (capability/statistics refresh).
8. **Hull-upgrade cascade** — up to 8 repeated passes propagating any automatic "fleet composition promoted to a newer type" changes until the fleet list is stable.
9. **Combat-detection and resolution pass** — every location with a multi-race fleet stack not already flagged is checked for a hostile-race conflict (see `combat-resolution.md`); if found, the battle-resolution engine (§2 below) runs for that location. This phase is followed immediately by the fleet-meets-foreign-colony exchange pass (§4 below).
10. **Diplomacy/relations decay** — every ordered pair of races has its stored relationship-aggression counter decremented toward neutral, clamped at its bounds; crossing a threshold triggers a "war declared" notification to both parties. (See `diplomacy-relations.md`.)
11. **Economic/production turn pass** — remote-mining and waypoint-task execution across every fleet, described in `production-queue.md` and `fleet-movement-scanning-cargo.md`.
12. **Random map-feature/event pass** (first of two identical passes at steps 12 and 16) — see §5.
13. **Fleet movement execution** — the actual per-fleet movement resolution for the turn (warp travel, fuel consumption).
14. **Home-planet-related fleet-flag pass.**
15. **Further economic passes**: mass-packet/minefield processing (§3), colonization-tie-break resolution, research allocation (all described in their respective sections/specs below).
16. **Second random map-feature/event pass.**
17. **Starbase refuel pass** — fleets docked at a friendly starbase, or carrying ramscoop-type engines, have their fuel adjusted: ramscoop engines contribute a flat 200 fuel per engine while docked/in transit, offset against a mass/cargo-driven burn rate.
18. **Second fleet-flag-reset/scan pass**, also clearing each fleet's first waypoint slot once reached.
19. **Further economic passes and an autosave/checkpoint operation.**
20. **Turn/year counter increment** — the master turn counter advances here, roughly two-thirds of the way through the overall turn-generation sequence, not at the very start or end.
21. **Per-player turn-file generation** — builds each player's next-turn output file and notifies them it is ready.
22. **A per-race numeric computation**, executed twice per player against two different record tables, with the result squared — plausibly related to score or ranking, though this was not confirmed (see Open Questions).
23. **Cleanup** — frees turn-processing scratch memory, restores UI state.

### 2. Battle resolution engine

Combat is resolved by a dedicated routine invoked once per location with a detected hostile-race conflict. Its structure:

- Two growable, persistent "battle log" record pools are ensured to exist (allocated on first use).
- A scratch table tallying per-race, per-design-slot ship-stack quantities is built from the fleet ring at that location (a same-location "linked stack" of every fleet present), including the defending planet's starbase if it is a valid combat participant.
- The full combatant roster is built by flattening every ship stack present — one aggregate record per (race, design) pair with quantity > 0 — computing each aggregate's derived combat statistics (armor/shield/cloak/jamming totals, etc.) via shared per-design stat functions.
- **The battle runs for up to 16 rounds** — an explicit, hardcoded loop bound. This directly confirms the round cap already documented (from public sources) in `combat-resolution.md`.
- Within each round, a per-ship "rate"/initiative value is computed with rounding that depends on the round number's parity (odd/even), then combatant firing order is reordered by a shared "priority" computation. Targeting scans distances/ranges across up to 3 weapon-range brackets per shot.
- Damage is logged into the growable event log as it's applied (not computed all at once), and after the round's shots resolve, a resource-salvage step calls into the production-planning subsystem (`ai-opponent-behavior.md` §6-adjacent logic) to decide whether destroyed-ship minerals should be pulled toward nearby friendly production needs.
- After the battle concludes, a message/outcome-selection routine picks from roughly 20 distinct message codes describing the aftermath (destroyed participants, salvage available, etc.) per race present.

**This confirms the 16-round cap directly from the executable, independent of the public FAQ material `combat-resolution.md` was originally built from.** The specific targeting formula ("attractiveness"/APN), accuracy formula, and damage-quantization math described in that document were **not** independently confirmed or contradicted here — the decompiled battle engine's per-shot targeting logic is real and present, but its exact arithmetic was not fully decoded at a level suitable for direct comparison against the published formulas.

Separately, the battle-replay ("VCR") viewer that players use to watch a completed battle is confirmed to be **pure play-back**: it reads the round-by-round event log built above (token position, a "beam fired" or "missile fired" flag, and a raw damage/delta value per event) and only ever *displays* pre-decided outcomes — it performs no targeting, accuracy, or damage computation of its own. The viewer's own grid-drawing and hit-testing code independently confirms the battle board is exactly a 10×10 grid, with each token's position packed as one 4-bit column and one 4-bit row value.

### 3. Minefields and mass packets

Processed once per turn as part of the economic passes:

- **Mass-packet decay in flight**: a percentage of a packet's mineral cargo is lost each turn, scaled by the packet's warp-speed tier (roughly 10%/25%/50% for increasing speed tiers), and halved for a specific racial trait. On arrival (or when flagged for detonation), a packet's remaining cargo is delivered to (or impacts) planets within its blast radius.
- **Minefield regrowth and collision**: each turn, minefields regenerate some mine-unit strength, then two collision passes check every free-flying fleet and every fleet stationed at/orbiting a planet against every enemy minefield in range: a squared-distance test against the field's radius, with damage/mine-loss scaled by field "type" (a heavier field type inflicts roughly a third the normal mine-loss-to-damage ratio of an ordinary field), decrementing the field's strength and possibly destroying it if depleted. Both affected races are notified.
- **Minefields are edited via the same order-queuing mechanism used everywhere else** — a minefield-window command issues a turn order that is applied during this pass, rather than mutating the minefield immediately.

### 4. Colonization simultaneous-arrival and fleet/colony exchange

- **Simultaneous colonization tie-break.** When multiple fleets from different races arrive at the same uninhabited location in the same turn, the arrival with the largest accumulated population/quantity wins the colonization race; an exact tie between the largest two contenders results in **no one** colonizing that turn. All races present are notified of the win/lose/tie outcome.
- **Fleet-meets-foreign-colony exchange.** Once per turn, for every fleet sitting in orbit of a planet it does not own, where the diplomacy/relations system (`diplomacy-relations.md`) permits a "meeting" and the fleet is neither cloaked nor hostile, a probabilistic exchange of colonists/minerals/fuel occurs between the fleet and the planet. The resulting notification message differs depending on whether the colony belongs to the fleet's own race or a foreign one — the game maintains two parallel sets of outcome messages for this distinction.

### 5. Random turn events

Once per race per turn, gated behind a roughly 50%-plus base chance, the game rolls for **at most one** random empire-wide event from two independent tables:

- A **13-entry table** of percent-chance-weighted events, each with its own enable flag and per-race "already triggered" tracking so a given event fires for a given race only once.
- A **6-entry table** of counter-vs-threshold events that, on success, generate and accumulate a numeric reward.

Only one event total fires per race per turn (a flag prevents both tables from firing the same turn for the same race). Concrete, gameplay-recognizable events identified among these include:

- **A "comet strike"-style event** (roughly 1-in-20 base chance per eligible turn): randomly shifts an eligible planet's three environment axes (Gravity/Temperature/Radiation) by magnitudes in roughly the 50-350 range, and separately adjusts mineral concentration.
- **A simpler, related environmental-shift event** using the same 1-in-20 gate but a single randomized delta rather than three.
- **A "new mineral deposits discovered"-style event**: chance scales with the game's current year, bumps a mineral-concentration value up by roughly 5-20 (capped near the top of the concentration scale).
- **A "Mystery Trader"-style wandering-ship spawn**: gated to no earlier than roughly turn 39, with further modulo-100/128 "special turn" windows or an even-turn-only fallback; spawns a new fleet-like object at a random galaxy position with a partially tech-gated component loadout (higher-tier components suppressed in early appearances, becoming more likely to appear as the game year advances), and broadcasts its appearance to every race.

### 6. Mineral Alchemy auto-conversion (Alternate Reality)

Distinct from the manually-queued "Mineral Alchemy" production item documented in `production-queue.md`, races with the Alternate Reality primary trait receive an **automatic, non-queued** per-turn conversion: any leftover planetary resources beyond a threshold are converted directly into minerals at a race-specific ratio (`resources * ratio / 100`, where `ratio` is a per-race stored byte), with any local shortfall optionally transferred in from a linked planet.

### 7. Research allocation

Once per turn, per race, accumulated research resources are applied to whichever field(s) the player has selected (see `research-tech-tree.md`), advancing tech levels and firing a level-up notification. Any resource surplus left over after an initial allocation pass is redistributed in a second pass, so a turn's research spending is not strictly capped by the exact cost of the next level in the primary field — genuine overflow carries within the same turn into the next field or the next level.

## Cross-references

- The 16-round battle cap is now confirmed at the code level (§2); see `combat-resolution.md` for the previously-documented (web-sourced) targeting/damage formulas, which remain neither confirmed nor contradicted by this analysis.
- Minefield and mass-packet mechanics (§3) are new material relative to `fleet-movement-scanning-cargo.md`, which does not currently describe minefield decay/collision resolution in detail.
- The relations-decay mechanic (phase 10, §1) belongs with `diplomacy-relations.md`.
- The colonization tie-break and fleet/colony exchange mechanics (§4) extend `fleet-movement-scanning-cargo.md` and `population-growth.md` respectively.
- Random turn events (§5) and Mineral Alchemy auto-conversion (§6) are new subsystems with no prior documentation; `production-queue.md` should note that Alternate Reality's automatic conversion is a *different* mechanism from the manually-queued Mineral Alchemy item it already describes.

## Open Questions

- The exact per-race numeric computation at phase 22, squared and computed against two different record tables, may relate to score or ranking but was not confirmed — see `scoring-and-reports.md` if a scoring-specific analysis has been done separately; this document does not assert a conclusion.
- The precise targeting/accuracy/damage arithmetic inside the battle-resolution engine (§2) was not decoded to a level that could confirm or refute the specific APN/attractiveness formula, beam-falloff percentages, or armor-quantization scheme already described in `combat-resolution.md`.
- Exact real-world identity of several message codes referenced throughout (colonization outcomes, minefield damage notices, random-event flavor text) could not be determined — no string/text resources survive in the analyzed material.
- Whether the two "random event" tables (13-entry and 6-entry) contain any events beyond the four identified above was not fully catalogued.
