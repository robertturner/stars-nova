# Computer (AI) Opponent Behavior

Behavior specification for the built-in computer-controlled empires in the 1995-2000 4X game *Stars!*. Unlike the other specifications in this set, this document is derived directly from the exported client's own decompiled turn-processing logic rather than from public community research — no public source documents how the built-in AI actually decides what to do, so this is the first specification of its kind for this subsystem. Facts here describe the algorithm's observable shape (decision order, thresholds, formulas) in generic terms; they do not use any of the original identifiers, variable names, or literal code from the binary.

## Overview

Every non-eliminated computer-controlled empire is processed once per turn by a single "AI turn" driver, dispatched to one of several **personality drivers** based on a small numeric code stored on that player's record (the built-in AI "type"). Each personality driver runs the same broad decision sequence — refresh threat assessment, decide fleet cargo/colonization/mining actions, decide planet-level construction, then hand off to two subsystems shared by every personality (a starbase-management pass and a general per-planet production/resource-allocation pass). What differs between personalities is a set of tuning constants (probabilities, distance thresholds, aggressiveness multipliers) baked into each driver, not the overall shape of the decision tree.

At least **seven** built-in personality slots exist (numbered 0 through 7, with one slot unused/reserved and one essentially inert/passive), though the exact number of these that map to named, selectable difficulty or personality options in the game's setup screens was not established.

## Mechanics

### 1. Personality dispatch

A per-player field's top bits select which personality driver processes that player's turn. Each driver is a large (300-950 "step") routine sharing this skeleton:

1. Compute turn-scaled tuning constants — several thresholds scale continuously with the game's current turn/year number (not in discrete difficulty "eras"), generally becoming more aggressive/expansive as the game goes on.
2. Refresh an assessment of nearby threats (enemy fleet strength, economy) using shared helper routines.
3. Iterate every fleet the AI owns, deciding per fleet whether to: transport cargo/minerals to a shortfall location, redirect toward a colonization or invasion target, scrap, or hold.
4. Iterate every planet the AI owns, deciding whether to queue a starbase, adjust defensive posture, or trigger an "opportunistic build" scan (queuing a currently-affordable ship design or component upgrade with some random retry chance).
5. Call the shared starbase-management pass (§3) and the shared production-allocation pass, common to every personality.

One personality slot is a near-total no-op (it only runs the two shared passes, no independent fleet/planet decisions) — the most passive built-in AI available. Another slot is entirely disabled (its driver function does nothing at all).

### 2. Colonization and fleet-destination selection

Target selection is built from a small set of generic, reusable search primitives that walk the planet/fleet lists computing squared distance from a reference point and applying a caller-supplied acceptance test:

- **"Nearest match"** — returns the closest object for which the caller's predicate is non-zero.
- **"Best match"** — returns the object maximizing the predicate's numeric return value, ties broken by distance.

Concrete predicates observed include: ownership filters ("not mine"), an "economy strong enough to spare a colonizer" gate (a threshold on summed cargo/mineral fields, tuned per personality), and habitability-based scoring functions.

**Colonization-target scoring.** A candidate planet's score (roughly 0-100) combines a habitability estimate with a distance bonus using a fixed set of concentric range bands (squared-distance thresholds at roughly 50, 100, 150, and 200 light-years) — closer candidates score higher. Once scored, acceptance is **probabilistic**: a random roll out of 100 must fall under the candidate's score, so even a strong candidate is not always chosen immediately and a marginal one is occasionally still picked. A separate "too eager" flag on some candidates adds an additional flat 25% chance of random rejection specifically, to add jitter and avoid every AI fleet rushing the single best-looking planet simultaneously.

**Late-game colonization becomes distance-conservative.** After roughly turn 59, a candidate's distance to the AI's *nearest already-owned colony* is checked against three graduated thresholds and probabilistically rejected the farther out it is: certain rejection beyond ~350 light-years, a 50% rejection chance beyond ~300, and a 50% rejection chance beyond ~250. This does not apply in the early game.

**Random exploratory colonization** uses reservoir sampling over an expanding search radius when no strong candidate is found nearby, so early scouting fleets don't get stuck idle simply because nothing scores well yet.

### 3. Starbase management (shared across all personalities)

After a personality driver finishes its own fleet/planet logic, it always calls one shared "starbase AI" pass that iterates every AI-owned starbase:

- If any of a starbase's three mineral stockpiles falls critically low (below roughly 700 units) while a nearby friendly planet holds a healthy surplus (above roughly 700), a transfer order is queued to move the shortfall mineral there. If no single planet is critically low, any overall surplus is instead split three ways across the three mineral types, weighted by distance to the nearest source.
- Separately, each starbase decides whether to build a Stargate/orbital structure, redirect colonists toward a low-population candidate planet, or fall back to a scrap/consolidation action.
- A combat-readiness check gates whether a fleet uses an aggressive or a cautious attack-order profile: it requires the fleet be farther than roughly 14 units from its target, hold more than roughly 499 cargo/fuel, and — if every one of the fleet's three weapon-slot counts individually exceeds 15 — escalates to the more aggressive profile.

### 4. Threat assessment and defense/minefield decisions

- A base **threat rating** (roughly 4-6) is computed per nearby enemy presence, with random jitter and a possible +1 bump gated on a racial-trait check, cached per-target.
- The AI sweeps its own multi-ship fleets, builds a scratch list of nearby enemy or unclaimed strength indicators, and computes an aggregate threat code feeding the rating above.
- A dedicated defense-need evaluator (the most fully worked-out of the threat functions) is skipped entirely for passive personalities, or once a global aggressiveness flag is off past roughly turn 30. Otherwise it recommends building defensive minelaying/countermeasure capacity only if accumulated threat stays at or below `(raceTraitValue * 20) + 10`, and existing minefield unit count stays at or below `(ownedPlanetCount * 4) / 5`; below a threat level of 5 it instead flips a 50/50 coin on whether to build anyway.

### 5. Automated mineral/cargo transport

A dedicated freighter-routing routine sums a fleet's cargo hold, avoids double-assigning a delivery target another friendly fleet is already servicing that turn, and searches for the nearest reachable planet needing that cargo type, falling back to a random pick if nothing scores above a cutoff (roughly 180 light-years squared). A companion value-per-time-of-arrival scoring function normalizes its result by a constant of 25 — consistent with the game's warp-squared ("distance covered scales with the square of warp speed") movement rule, since 25 = 5², suggesting the routine is implicitly comparing routes as if traveled at warp 5.

### 6. Production-queue insertion by the AI

The AI's per-planet production advisor is a fixed decision chain tried in order each turn: a defense-percentage-target advisor, a mass-driver/mineral-packet advisor, two further advisors for other component categories, and a fallback advisor if none of the above queue anything; a young colony additionally triggers an auto-load-cargo check. This chain always concludes by calling a shared resource-allocation routine that auto-fills the currently active production item's cost from available resources, spilling any excess into a secondary item slot.

Concrete observed thresholds:
- **Defense-percentage advisor**: the chance of the AI acting to close a gap toward its target defense percentage decreases the longer the gap has persisted, and drops to a flat 5% chance once the planet is already at or above its target.
- **Mass-driver/mineral-packet advisor**: requires a qualifying starbase component, combined mineral stockpile of at least 3,000, a minimum population score of at least 10, and only fires on a 25% random roll; queued quantity is capped at 25, with a secondary gate at 20,000 minerals.
- Two further, less-decoded advisors for other component categories cap their queued quantity at 4.
- **Resource-output percentage-discount**: net resource output is reduced by `resources * traitByte / 100` for some races, where `traitByte` is a per-player signed value — gated so it only applies when a particular global game-option flag is off. This is a race-trait-driven mechanic distinct from, and in addition to, the population/factory resource formula documented in `production-queue.md`.
- **Auto-load colony cargo**: triggers once projected resource surplus exceeds 49, provided a matching stocked component exists.
- **Production-queue insertion cap**: a build-order is refused outright once its queued-item count exceeds 200 (a hard structural limit, independent of the per-item 1,023-unit quantity cap documented in `production-queue.md`).

### 7. Ship auto-design pipeline

Distinct from the human-facing race-design and ship-design UI (`race-designer-ui-and-availability.md`), the AI maintains its own automated design-refresh pipeline:

- A new design is assembled from a base hull template plus researched-component picks (choosing the best currently-available tech level for each slot), then committed to a free slot in either the empire's 16-slot regular-design table or a separate 10-slot table the AI uses for its own bookkeeping.
- **Refresh scheduling** uses turn-age thresholds: a design is flagged for replacement once it reaches roughly 35 turns old (moderate urgency) or 50 turns old (high urgency), with the AI's very first (starting) hull class getting a longer, roughly 26-turn grace period before it's eligible for replacement at all. Past turn 49, the single most-outdated design category across two five-category groups is chosen for replacement each turn, but only if its age has reached at least 40 turns.
- A design flagged as flatly obsolete (a stored flag bit) is replaced immediately regardless of the age thresholds above.

**Cross-segment confirmation.** A re-verification pass over segment 31 (`FUN_10F0_*`, previously characterized as "mostly battle engine and AI production planning" — see `combat-resolution.md` §8 for the full re-check) independently located the slot-filling half of this pipeline: a function there iterates a design's component slots and calls the shared race/item-availability resolver documented in `client-interface.md`'s "Trait-gated control validation" section (the same validator used by the human-facing Ship/Starbase Designer) to pick the best currently-available component for each slot — confirming "choosing the best currently-available tech level for each slot" above at the code level, and confirming the AI design pipeline reuses the human-facing availability rules rather than its own separate gating logic.

### 8. Fleet turn-processing order is randomized

The list of fleets an AI empire owns is reshuffled (Fisher-Yates style, using the same bounded random-number generator used throughout the game) before each turn's fleet-processing pass, unless a specific global flag suppresses it. This means the *order* in which an AI's fleets receive their per-turn decisions is not stable or predictable from one implementation's fleet-array ordering to another's — any clean-room implementation reproducing bit-for-bit AI behavior needs to reproduce this shuffle, not just the decision logic itself.

**Pinned to a specific function by a later segment-31 sampling pass.** The shuffle is a plain, textbook Fisher-Yates: for each position (from a total count down to 1), it draws a bounded random index over the not-yet-shuffled prefix and swaps that slot into place using a fixed-size temporary buffer, over a single global array of 29-byte records. This independently confirms the mechanism's shape (not just its existence) at the code level, though the specific array's game-visible identity (fleets specifically, versus some other 29-byte-record table this shuffle is also reused for) was not re-derived from structure alone in this pass.

**Correction, from an exhaustive fourth pass over segment 31 (see `combat-resolution.md` §8's segment-31 note): this specific function's array is the per-battle token table, not an AI empire's owned-fleet list.** The function takes no parameters — it always shuffles the same two globals, which a neighboring function in the same segment populates immediately beforehand as the just-built battle-token array (one 29-byte record per token, for the current battle only), and its record count is capped at exactly 256, matching `combat-resolution.md` §2's token cap. Because the function is hard-wired to this one array, it cannot be a general-purpose "shuffle any 29-byte-record list" routine reused elsewhere for AI fleet-processing order — so the claim above, that *this* code path is what randomizes an AI empire's per-turn fleet-processing order, should be treated as unconfirmed rather than pinned. It remains true that *some* mechanism reshuffles AI fleet-processing order each turn (see the surrounding paragraph), and it may still use an identically-shaped Fisher-Yates helper elsewhere in the codebase, but the concrete function traced into segment 31 is now known to serve battle-token tie-break ordering (most likely underpinning `combat-resolution.md` §5's "randomly decides a firing priority... kept fixed for the rest of the battle" rule) rather than fleet-turn-processing order specifically. This paragraph supersedes the shuffle-mechanism attribution in the paragraph above; the general "fleets are reshuffled before per-turn processing" claim itself is not retracted, only its evidentiary link to this particular function.

### 9. Colonizer commitment and design tech-upgrade budgeting, confirmed by inspection of the exported client

Distinct from the colonization-target scoring in §2 (which decides *which* candidate planet to pursue), a separate per-turn AI routine decides, for a candidate star already near an AI-owned fleet, whether and how to actually commit that fleet as a colonizer:

- A fleet's carried cargo is checked against a **5,000-unit threshold**: below it, the fleet is treated as too small to found a colony and receives a lesser fallback order instead; at or above it, the fleet is colonizer-capable.
- For colonizer-capable fleets, the assigned ship design's 6 tech-field levels (one byte per field, each capped at the game's known maximum of **26**) are summed and bucketed against the ladder **59 / 71 / 84 / 95 / 108** to decide how ambitious a design upgrade is worth pursuing, further modulated by how much of the 5,000+ cargo threshold is exceeded.
- A companion routine computes, non-destructively, **what it would cost in research resources to raise a specific design's tech-field levels up to a target** (saving and restoring the current levels around the calculation) — i.e. a "what would this upgrade cost" query used to judge affordability before committing, reusing the same per-level research-cost mechanism documented in `research-tech-tree.md`.
- The candidate star must also be within roughly **100 units (squared-distance 10,000)** of whatever resource/production source is funding the decision, and the resource budget available for the upgrade is capped at **5,000**, or **3,500** for one specific design category.
- Every commitment/rejection reason is recorded against a persistent per-decision bitmask (used to avoid re-deciding the same case identically every turn) and mapped to one of several internal log/message codes — the exact codes could not be tied to visible game messages, but the mechanism (a "why did the AI choose this" audit trail) is confirmed.

This is a genuinely separate subsystem from §2's colonization-target scoring — it operates *after* a target and fleet already exist, deciding commitment and design-upgrade budget rather than picking the target planet itself.

### 10. Stale-slot sweeps and pool redistribution (supporting utilities)

Two smaller, confirmed utility mechanisms support the AI's per-field bookkeeping (the same fixed-size per-field slot arrays referenced in §6-7): one sums a slot range's values (capped at 32,000) while flagging any slot whose "last touched" turn is older than a caller-supplied threshold, optionally purging a stale-and-empty slot outright; the other redistributes a 16-slot shared pool into a matching owned fleet whenever a particular per-race stat drops below **501**, repeating until the stat recovers. The concrete real-world meaning of the redistributed pool and the race stat being monitored could not be determined from structure alone.

## Open Questions

- The exact mapping from the numeric personality-dispatch codes (0-7) to any named, selectable AI-difficulty or personality option surfaced in the game's setup UI was not established — no string/label data survives in the analyzed material to confirm this.
- Which specific named hull/component categories the various "opportunistic build" and production-advisor component-category ranges correspond to (e.g., which real weapon or defense component a given numeric category code represents) could not be confirmed without access to the game's string/data tables.
- The exact rounding and edge-case behavior of several of the AI's percentage/cargo-allocation formulas (auto-fill toward a production item's cost, freighter routing's value-per-arrival-time score) is understood in shape but was not hand-verified bit-for-bit.
- Whether the two least-decoded production advisors correspond to specific, nameable game systems (their component-category ranges were identified numerically but not matched to known component families) is unresolved.
