# Diplomacy and Inter-Race Relations

Behavior specification for the inter-player relationship system in the 1995-2000 4X game *Stars!*. This document is derived directly from the exported client's decompiled logic; it is new material, not a correction to any existing specification, since no prior document in this set covers diplomacy. Facts are described in generic terms; no original identifiers or literal code are used.

## Overview

Every ordered pair of races (race A's opinion of race B, distinct from race B's opinion of race A) has exactly one stored relationship value, editable by the player and adjustable automatically by gameplay events. The relationship value gates whether two races' fleets can peacefully "meet" at a shared location, and is the basis for how a Battle Plan's "legitimate enemies" setting resolves into an actual list of hostile races during combat.

## Mechanics

### 1. Relationship states

Exactly **three** relationship states exist, editable via a small dialog offering three mutually-exclusive choices. The states are stored as one byte per ordered (race, other-race) pair. Based on how the stored value gates other systems' behavior, the three values correspond to **Neutral** (the default), **Friend**, and **Enemy** — though the exact on-screen labels could not be confirmed (no text/string data survives in the analyzed material).

- A newly created race's relationship row toward every other race initializes to the default (Neutral) value.
- The relationship table is referenced from roughly twenty distinct places across the game's logic — colonization/meeting eligibility, Battle Plan enemy resolution, AI aggression decisions, and the turn-generation relations-decay pass (`turn-generation-engine.md` §1, phase 10) all consult it.

### 2. Relationship changes are queued orders

Changing a relationship is submitted as a queued turn order (the same generic order-queuing mechanism used for fleet waypoints, production-queue edits, and other player actions) rather than mutating the relationship table immediately — the change takes effect at the next turn generation, alongside every other player's submitted orders.

### 3. Automatic relationship decay

Once per turn (`turn-generation-engine.md` §1, phase 10), every ordered pair's stored relationship aggression counter decrements toward neutral, clamped at its bounds. If decrementing crosses a defined threshold (i.e., the relationship formally worsens into a war state), both parties are sent a "war declared" notification. This means relationships are not purely player-set-and-forget: some relationship dimension is capable of drifting on its own over time, separate from the player's explicit Friend/Neutral/Enemy selection.

### 4. Relationship gates "meetings" between fleets

A fleet sitting in orbit of a foreign planet, or co-located with a foreign fleet, only triggers cooperative interactions (colonist/mineral/fuel exchange with a foreign colony, described in `turn-generation-engine.md` §4) if the relationship between the two races permits a "meeting" — a dedicated eligibility check reads a design's relation-category value and cross-references the stored relationship-pair table to decide whether the interaction is allowed.

### 5. Battle Plan "legitimate enemies" resolution

A Battle Plan's enemy-selection field (see `combat-resolution.md`) is a single small numeric category, not a per-race checklist as might be assumed from a plain "list of races considered legitimate enemies" description. The observed categories are:

- **None** — no race is targeted regardless of relationship.
- **Every race with an Enemy relationship** to the plan's owner.
- **Every race with an Enemy or Neutral relationship** (i.e., "everyone except my Friends").
- **All races**, regardless of relationship.
- **Exactly one specific race**, chosen individually.

So for three of these five categories, which races actually count as "enemies" for that Battle Plan is derived live from the relationship table at the moment orders are evaluated, rather than being an independent list stored per plan. Changing a relationship can therefore change who a fleet on an unmodified Battle Plan will engage, without the plan itself being edited.

## Cross-references

- `combat-resolution.md` should be read together with this document for Battle Plan enemy-category resolution (§5 above) — the existing description of "a list of races considered legitimate enemies" is more precisely a small enum resolved against this relationship table, not an independently-stored roster.
- `turn-generation-engine.md` §1 (phase 10) and §4 describe where relationship decay and relationship-gated fleet/colony interactions occur within the per-turn processing order.
- `client-ui-dialog-catalog.md`'s existing "Inter-owner relations" entry is consistent with this document's findings (a selected other owner plus a restricted-choice relationship control); this document adds the concrete state count (three) and the mechanism (queued order, not immediate write).

## Open Questions

- The exact on-screen labels for the three relationship states were not confirmed (inferred as Neutral/Friend/Enemy from gating behavior, not from any visible string).
- The exact decay rate, threshold, and bounds of the automatic relationship-aggression counter (§3) were not fully quantified.
- Whether any other systems beyond the ones identified here (meetings, Battle Plans, AI aggression) consult the relationship table was not exhaustively catalogued.
