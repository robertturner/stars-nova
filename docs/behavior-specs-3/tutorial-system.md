# New-Player Tutorial / Advisor Sequencer

Behavior specification for the built-in new-player tutorial system in the 1995-2000 4X game *Stars!*. This document is derived directly from the exported client's decompiled logic; it is new material, since no prior document in this set covers the tutorial. Facts are described in generic terms; no original identifiers or literal code are used.

## Overview

A scripted, turn-gated hint sequencer drives a persistent "coach" overlay during a player's first several dozen turns, waiting for the player to perform an expected action before advancing to the next step. It is implemented as a state machine layered on top of the ordinary game UI, separate from the tutorial window's own message-routing code (`client-ui-dialog-catalog.md`'s "Tutorial surface" entry describes the outward-facing window; this document describes the underlying sequencer that drives it).

## Mechanics

### 1. Lesson/step structure

Progress is tracked as a **lesson index** (observed range 0 through roughly 29) and, within a lesson, a **step index** that always advances in multiples of 8 (observed up to roughly 568, i.e. about 15 sub-steps per lesson). The lesson index appears tied to (and possibly doubles as) the turn counter, since lesson-gating logic elsewhere in the game checks it against a turn threshold of 30.

A debug/design-mode override flag exists that, when set, makes nearly every condition-check helper in the sequencer automatically report its expected condition as already satisfied — consistent with a development/testing shortcut rather than anything reachable through normal play.

### 2. Advancement conditions

A large family of "has the expected game/UI state been reached" condition-checker functions gates step advancement: whether a particular planet or fleet is currently selected, whether a specific dialog page is open, whether a production-queue slot contains an expected item, whether a specific research field is selected, and a general per-lesson "visited" bitset. One dedicated dispatcher maps specific (lesson, step) pairs to hard-coded expected production-queue item identifiers — i.e., certain tutorial steps require the player to have queued one specific kind of component or ship, not just "any" production action.

### 3. UI presentation

- The tutorial overlay's paint routine draws up to **8 tabs** (a breadcrumb-style progress bar across lessons), highlighting the currently active one.
- On first show, the dialog buckets the available screen width into 4 size classes (thresholds around 800/1024/1280 pixels) to choose its own layout.
- A debounced popup mechanism caps any single repeated hint message at **3** occurrences, to avoid nagging the player with the same message indefinitely if they don't perform the expected action.
- Skipping the tutorial before turn 30 is gated behind an explicit confirmation prompt.
- Turning the tutorial off temporarily saves and restores roughly 26 bytes of UI/mode state (a "demo mode" swap) and toggles one associated menu item's checked state.

### 4. Auto-advance

A dedicated routine can auto-advance through lessons automatically (without waiting for player input) until either player input becomes genuinely required or the lesson index exceeds its maximum bound. This same routine is called from at least one other UI surface (the Score dialog) as a generic "notify the tutorial system something happened" hook, suggesting the tutorial sequencer listens for state changes broadly across the client rather than only from its own window.

## Cross-references

- `client-ui-dialog-catalog.md`'s existing "Tutorial surface" entry describes the outward-facing window behavior (opening, navigating, closing without side effects); this document adds the underlying step-gating mechanism.
- The production-queue item-format details referenced by the per-step advancement dispatcher (§2) are consistent with the record layout described in `production-queue.md` and `save-turn-file-format.md`.

## Open Questions

- Whether the lesson index is genuinely identical to the turn counter, or merely correlates with it, was not conclusively established.
- The exact content of each of the roughly 30 lessons (what each teaches, and which specific production items or UI states each step expects) could not be determined beyond the structural mechanism, since no string/text content survives in the analyzed material.
