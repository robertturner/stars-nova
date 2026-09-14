# Combat Resolution

Behavior specification for the tactical battle system in the 1995-2000 4X game *Stars!*, written for a clean-room reimplementation. All facts below are restated in original wording from public community research (primarily the long-standing *starsfaq.com* "Stars! FAQ" site, whose "Guts of the Battle Engine" article is the most detailed known public reverse-engineering writeup of the battle simulator, plus corroborating strategy articles). No content was derived from the original binary or any decompilation artifact. Where sources are silent, contradictory, or only inferred from anecdotal play reports, this is called out explicitly under "Open Questions" rather than asserted as fact.

## Overview

A *Stars!* battle is a fully automatic, deterministic-per-inputs simulation that runs once per turn processing at every map location where hostile fleets/starbases coincide — there is no real-time player input during the fight itself. Before the turn is generated, each player pre-configures reusable "Battle Plans" per fleet (a primary/secondary target-type preference, a list of races considered legitimate enemies, and a movement "tactic"). When the turn processes, the game engine:

1. Detects every location with two or more mutually hostile races present and builds a battle instance there.
2. Places every participating ship into the battle as part of a "token" (a stack of identical-design ships) on a 10x10 tactical grid.
3. Runs up to 16 rounds, each consisting of a movement sub-phase (governed by each token's chosen movement "tactic" and an attractiveness-driven target selection) followed by a firing sub-phase (governed by an initiative-ordered weapon-slot queue and a targeting/accuracy/damage model).
4. Ends the battle early if only one race remains, or if all remaining races have no hostile orders toward one another; otherwise it stops at the 16-round cap.
5. Produces mineral salvage from destroyed ships and, separately, a chance for survivors to gain partial tech levels from destroyed enemy designs (governed by a different subsystem, only summarized here).

Source: [Guts of the Battle Engine — starsfaq.com](http://starsfaq.com/battleengine.htm)

## Mechanics

### 1. What triggers a battle

A battle is generated at a location when two or more fleets (or a fleet and a starbase) belonging to different races are stacked there, **and** at least one of them carries orders to attack the other's race — the ship types involved are irrelevant to eligibility. A race's fleet that is present but has no attack order against anyone else there, and that nobody else there has orders to attack, sits the battle out entirely (and forfeits any tech-gain opportunity that battle might otherwise offer). Practically, "legitimate enemy" is symmetric: if any other race present has hostile orders against you (even with unarmed ships), you are automatically a legitimate target for them regardless of your own orders.

Source: [Guts of the Battle Engine](http://starsfaq.com/battleengine.htm)

### 2. The battle grid and tokens

- The tactical board is a 10x10 grid of squares.
- Every ship becomes part of a "token" (stack) with other ships that share the exact same design; a single fleet with multiple ship designs contributes one token per design, and splitting ships into a second fleet before battle is a way to force a second token of the same design.
- Multiple tokens may occupy the same square.
- There's a hard cap of 256 tokens per battle across all participants. If the cap would be exceeded, tokens from the fleets with the highest fleet ID numbers are dropped first; token slots are otherwise allocated fairly, split evenly per race, with unused shares redistributed to races that need more.

Source: [Guts of the Battle Engine](http://starsfaq.com/battleengine.htm)

### 3. Round structure and movement

Each battle is capped at **16 rounds**. Every round has two ordered sub-phases: movement, then firing.

**Movement speed and sequencing.** Each token has a speed rating letting it move 0–3 squares per round. A fractional bonus square is added on a schedule tied to the round number: a 1/4 bonus grants one extra square on round 1 and then every 4th round thereafter (round 5, 9, 13); a 1/2 bonus grants a bonus square every other round starting on round 1; a 3/4 bonus grants a bonus square on 3 of every 4 rounds. Within a round, movement resolves in speed tiers: all tokens capable of 3 squares of movement take their first step, then all tokens capable of 2+ squares take a step (this is the second step for speed-3 tokens), then all tokens with at least 1 square remaining take a final step. Within each of these steps, heavier tokens generally move before lighter ones, but if the weight difference between two tokens is under roughly 20% there is a proportionally increasing chance the lighter token acts first instead — i.e., ties are not absolute, they are weighted-random near parity.

**Movement targeting (the 6 battle "tactics").** Each fleet's Battle Plan sets one of six AI behaviors that is re-evaluated every time a token is due to move a square:

- **Disengage** — if any enemy is within its own firing range of you, move to a strictly farther square; if you can't increase distance, hold distance; if you can't even hold distance, move randomly; if no enemy is in range of you, move randomly. Successfully retreating off the board requires accumulating 7 squares of movement under this order.
- **Disengage if Challenged** — behaves like Maximise Damage until the token actually takes damage, then switches to Disengage behavior for the rest of the battle.
- **Minimise Damage to Self** — move away from threats exactly as Disengage would when in enemy range; otherwise, close in on the best available target without moving toward the enemy.
- **Maximise Net Damage** — find the most attractive primary target (falling back to secondary targets, see targeting below); if any weapon is out of range, close the distance; once all weapons are in range, move so as to maximize damage-dealt divided by damage-received (in practice: hold at maximum range if your weapons outrange theirs, move randomly while stayed in range if ranges match, or close to point-blank if you're the shorter-ranged beam combatant).
- **Maximise Damage Ratio** — identical logic to Maximise Net Damage, but only the single longest-ranged weapon on the ship is considered when deciding movement.
- **Maximise Damage** — close distance until every weapon is in range of the most attractive target; beam-armed ships continue closing all the way to point-blank range (beam damage falls off with range, see below); missile/torpedo-armed ships, once in range, move randomly among squares that keep them in range.

A documented quirk: starbases get a +1 bonus to weapon range (and to minesweeping rate) but cannot move, and the movement AI does not account for that range bonus when *other* ships are trying to disengage from a starbase — a ship trying to flee a range-6-missile starbase will move to distance 7 believing itself safe while still actually in range.

Source: [Guts of the Battle Engine](http://starsfaq.com/battleengine.htm)

### 4. Targeting logic

A Battle Plan's targeting configuration has four parts: a **primary target type**, a **secondary target type**, a list of races considered legitimate enemies, and the movement tactic described above. When a token looks for something to shoot (or move toward), it first looks for the most "attractive" enemy token of a type listed as its primary target; only if no primary-type targets exist does it consider secondary-type targets. Target types not listed as either primary or secondary are never fired upon by that token, even if that ignored ship is itself shooting back.

**Correction, verified against the exported client:** the "list of races considered legitimate enemies" is not an independently stored per-race checklist. It is a single small category value with five possible settings: none; every race with an Enemy relationship; every race with an Enemy *or* Neutral relationship ("everyone except my Friends"); all races; or exactly one specific race. For three of these five settings, which races actually count as enemies is resolved live against the separate inter-race relationship table at the moment orders are evaluated, not stored per plan — see `diplomacy-relations.md`. Changing a relationship can therefore change who a fleet on an unmodified Battle Plan will engage. The Battle Plan record itself is a fixed 36-byte structure; one byte's low/high nibbles hold the primary/secondary target type (matching the "up to 15 types each" shape implied above), and a separate byte's low nibble holds the movement tactic, confirmed to use only values 0-5 — directly corroborating the six-tactic list.

**Attractiveness formula.** Every enemy token has an attractiveness score used both to decide where a ship moves and which enemy it shoots first, and the score is recomputed as the battle progresses (since armor/shields deplete). The general shape, credited to community researcher Art Lathrop's testing:

```
Attractiveness = Cost / APN
Cost = Boranium_cost + Resource_cost   (of the ship design; ironium/germanium excluded)
```

`APN` ("Attack Power Needed", sometimes called `eff_dp`) is weapon-type specific and represents roughly how much punishment the target can soak up against that weapon type:

- **Beam weapons, non-sapper:**
  `APN = (Armor + Shields) / (0.9^n) * RangeModifier`
- **Beam weapons, sapper** (shield-only damage weapons):
  `APN = Shields / (0.9^n) * RangeModifier`
  where `n` = number of beam deflectors fitted, and `RangeModifier = 1 - 0.1 * (Range / MaxRange)` (this range term is the researcher's best-effort inference, not confirmed with certainty).
- **Torpedoes and missiles**, where target Shields ≥ target Armor:
  `APN = Armor * 2 / Accuracy`
- **Torpedoes and missiles**, where target Shields < target Armor:
  `APN = Shield * 2 / Accuracy + (Armor - Shield) / (Accuracy * WeaponType)`
  where `WeaponType = 1` for standard torpedoes and `2` for capital-ship missiles, and `Accuracy` is the attacker's already-fully-modified (post-computer, post-jammer) hit chance against that specific target, expressed 0–1.

A lower APN (i.e., a "softer" target relative to its cost) yields higher attractiveness and gets shot first. This is why cheap, lightly-defended "chaff" ships are extremely attractive targets (high Cost/APN is wrong direction — cheap ships have low Cost, but their APN is even lower, driving attractiveness up when heavily unshielded/unarmored relative to cost) and why adding shields or jamming meaningfully drops a design's priority as a target. The formula does not model the game's separate "one missile can only ever contribute to killing one whole ship" salvo-accounting nuance (see Damage Calculation), which is part of why fielding large numbers of cheap unshielded chaff tokens is an effective tactic distinct from simply having low attractiveness.

Source: [Targeting Order in Battles — starsfaq.com](http://starsfaq.com/articles/sru/art201.htm) (mirrored in the same site's ["Guts" reference, §4.14](http://www.starsfaq.com/advfaq/guts2.htm))

### 5. Initiative and firing order

After the movement sub-phase, every token fires. Firing is resolved **weapon-slot by weapon-slot**, not ship by ship: all weapons occupying the same equipment slot on a token fire together as one shot. Slots are ordered by total initiative — hull base initiative + bonuses from computers fitted + the weapon's own initiative — with the **highest total initiative firing first**. If two slots (belonging to different tokens) tie on total initiative, the shorter-ranged weapon fires first; if they are still tied, the engine randomly decides a firing priority between the two ships the first time the tie occurs, and that priority is then kept fixed for the rest of the battle. Damage is resolved and applied immediately after each individual slot's shot, before the next slot in the queue fires — so a token destroyed earlier in the firing order never gets to fire its own not-yet-resolved slots that round.

Because each shot targets whatever is currently the most attractive in-range target at the moment it fires (falling back from the token's primary movement target to whatever primary/secondary-type target is actually in range), a token that closed in on one target during movement may end up shooting a different, closer one if its intended target drifted out of range.

One additional documented quirk, observed for the endgame Battleship hull specifically: its weapon slots do not fire in a simple positional order but in the sequence *top-6, bottom-6, top-2, bottom-2, center-4* (referring to the hull's slot layout) — cited here as an example that per-hull slot firing order is not necessarily "left to right" and should be verified per-hull against play data rather than assumed uniform.

Sources: [Guts of the Battle Engine](http://starsfaq.com/battleengine.htm); [Guts §4.14.1, Battleship slot firing order](http://www.starsfaq.com/advfaq/guts2.htm)

### 6. Damage calculation

**Per-shot base damage.** For a firing weapon slot:

```
ShotDamage = WeaponsInSlot * ShipsInToken * WeaponDamagePerHit
```

**Beam weapons — range dissipation and modifiers.** A beam weapon's damage falls off linearly with the range it's fired at, losing up to 10% of its damage at the weapon's maximum range (e.g., a range-2 beam does full damage at range 0, ~5% less at range 1, and ~10% less at range 2). Damage is further modified downward by the target's beam deflectors and, per other strategy sources, upward by the firer's energy capacitors (fitting these is documented as effective, though this research did not turn up the exact per-capacitor percentage — see Open Questions). Beam deflectors stack as:

```
DeflectedDamage = UndeflectedDamage * (0.9 ^ n)   where n = number of deflectors on the target
```

**Shields before armor.** All incoming damage (beam or missile) is applied to a token's pooled shield points first; only once the token's entire shield pool for that stack is exhausted does further damage reach armor.

**Torpedo/missile hit resolution.** Each individual missile/torpedo in a shot is resolved as an independent hit/miss check against an accuracy percentage (see below).
- A **miss** still deals 1/8 of its damage to the target's shields only (armor is unaffected by a miss).
- A **hit** applies up to half its damage to shields, with the remainder going to armor (once shields for the stack are depleted, naturally all damage from subsequent hits goes to armor).
- **Capital missiles** specifically deal **double damage to armor** for any portion of their damage that lands after the target's shields are already fully depleted — ordinary torpedoes do not get this doubling.

**Accuracy.** A missile/torpedo's chance to hit is a function of the weapon's own base accuracy, the firing ship's computers (which raise accuracy), and the target's jammers (which lower accuracy). The exact internal formula was not confirmed by any source found in this research (see Open Questions), but real numeric behavior has been documented by community testing and is directly useful for validating an implementation:
- A weapon with 20% base accuracy and no computer support reaches 44% accuracy for the attacker when a single "Battle Super Computer" is fitted.
- The same attacker, firing on a target fitted with a single "Jammer 20" component, sees accuracy fall — e.g. an unmodified 20%-base attacker drops to 16% against a Jammer-20 target, while the 44%-with-computer attacker drops to 28% against the same Jammer-20 target. (Both figures come from the same worked example; they show jammers and computers are not simply additive percentage points against each other — see the source article's algebra.)
- Jammers show strongly diminishing returns as more are stacked on a single ship: one community-run test against a 7-computer-boosted torpedo attacker found accuracy dropping from 98% (0 jammers) to 93% (1), 86% (3), 83% (4), 79% (6), and only 78% (7) — the first jammer contributes far more than the seventh.
- High-accuracy standard torpedoes are documented as much harder to jam down to low accuracy than capital missiles are, for a given amount of jamming — capital missiles rely more heavily on computer support and are correspondingly more vulnerable to jammers.

**Whole-ship kills within a token.** After a shot's total armor damage is computed, the number of *whole ships* destroyed in the target token is `floor(TotalArmorDamageFromThisShot / CurrentArmorPerShipInToken)`, where "current armor per ship" accounts for cumulative damage already suffered by the stack (`TotalArmor * remaining_undamaged_fraction`). Any remaining (non-lethal) damage from the same shot is then spread evenly across the surviving ships in the token. Internally, a token's cumulative armor damage is tracked in units of 1/512 of the stack's total armor (rather than as an exact hit-point figure, the way shield pools are tracked exactly), and this quantization is always rounded in the damage's favor (rounds up). This is exploitable: a design split into many small tokens, each individually easy to land at least one hit on, can cumulatively take more effective damage from many weak missile-slot hits than the same total ship count in one token would, since every slot that lands a hit contributes at least ~0.2% of that token's armor regardless of how small the actual per-missile damage roll was.

**Firing continues** slot-by-slot in initiative order (see §5) until every weapon slot currently in range has fired, then the round ends and the next round's movement phase begins.

Sources: [Guts of the Battle Engine](http://starsfaq.com/battleengine.htm); [Guts §4.7, Beam Deflectors](http://www.starsfaq.com/advfaq/guts2.htm); ["Frigates vs. Cruisers" — worked accuracy numbers](http://starsfaq.com/articles/sru/art104.htm); ["When Not to Use Max Computers" — jammer-stacking table](http://starsfaq.com/articles/sru/art172.htm)

### 7. How a battle ends

A battle stops at whichever of these happens first:

1. **Round cap** — 16 rounds have elapsed.
2. **Last race standing** — only one race still has ships/starbases present.
3. **Mutual non-hostility** — two or more races remain present, but none of them has hostile orders toward any of the others still present (this can happen mid-battle if the only race a given fleet was hostile to has already been wiped out).

A token attempting to flee under the Disengage tactic (see §3) must accumulate 7 squares of movement to actually leave the battle board; ships that don't reach that threshold before the battle otherwise ends simply remain present (and, if the battle ends by round cap with hostiles of multiple races still alive, presumably fight again next turn at the same location, since the underlying trigger condition is unchanged).

**Aftermath.** Destroyed ships leave salvage equal to 1/3 of the total mineral cost of everything destroyed in the battle; if the battle happened over a planet the salvage is deposited on that planet, otherwise it's left as a decaying deep-space mineral concentration (each mineral type decays 10%, or 10kT, whichever is larger, per year, in deep space; planet-side salvage does not decay). Separately, any race that had at least one ship survive the battle (by surviving to the end or by successfully retreating) becomes eligible for a chance to gain partial tech levels based on the enemy tech present in ships destroyed during the fight — this is a distinct subsystem from combat resolution itself and is out of scope for this document.

Source: [Guts of the Battle Engine](http://starsfaq.com/battleengine.htm)

### 8. Verification against the exported client

A pass over the game's decompiled turn-generation logic (summarized fully in `turn-generation-engine.md` §2) independently confirms several of the above facts directly from the executable, and identifies the boundary of what could and could not be checked this way:

- **The 16-round cap is confirmed directly** — the battle-resolution routine's round loop is an explicit, hardcoded bound of 16 iterations, matching this document's §7 claim exactly.
- **The 10×10 grid is confirmed independently twice** — both in the battle-replay viewer's own grid-drawing routine (which draws exactly 10 gridlines per axis) and in its click-hit-testing code (which clamps both axes to 0-9). Each token's board position is stored as a single byte with one 4-bit column and one 4-bit row value, consistent with (though not a proof of) a grid no larger than 10 per side.
- **The battle-replay ("VCR") viewer is confirmed to be pure playback.** It reads a pre-computed, round-by-round event log (each entry: a token id, a "beam fired" or "missile fired" flag, and a raw damage/delta value) and only ever animates and displays these already-decided outcomes — it performs no targeting, accuracy, or damage computation of its own anywhere in its code. The actual per-shot math happens earlier, inside the turn-generation battle engine that builds this log in the first place.
- **A per-design cloak-percentage lookup table with 18 entries is confirmed to exist**, corroborating that cloaking affects combat outcomes (this document previously had no mention of cloaking at all), though the table's individual percentage values could not be recovered.
- **The 256-token cap and per-race fair-allocation rule (§2) were not independently confirmed or contradicted** — the token-count field observed is a single byte (consistent with a cap in that range) but the specific 256 figure and the allocation logic were not located in the segments examined.
- **The exact targeting/"attractiveness" formula (§4), accuracy formula, and armor-quantization scheme (§6) were not independently confirmed or contradicted.** The turn-generation battle engine's targeting and damage-application code is real and present, but was not decoded to a level suitable for direct comparison against the specific formulas in §§4 and 6, which remain sourced only from the public FAQ material this document was originally built from.
- **Post-battle resource handling is tied into the same production-planning logic the AI uses for its own economy** (see `ai-opponent-behavior.md` §6) — after each round's damage resolves, a shared routine evaluates whether destroyed-ship minerals should be pulled toward nearby production needs, though this is a different code path from the salvage/decay mechanic described in §7 above and the two were not reconciled against each other.

---

### Worked Examples

The following examples illustrate the mechanics above with round numbers chosen for clarity. **The specific weapon/armor/shield values used are illustrative, not verified canonical in-game tech-tree numbers** — no source consulted during this research reproduced an authoritative table of exact stock weapon stats (see Open Questions). The mechanics being demonstrated (initiative ordering, shield-then-armor depletion, beam range falloff, torpedo hit/miss/shield-split, capital-missile double damage) are the documented parts; the numbers are a stand-in until real tech-tree data is sourced or entered separately.

#### Example 1 — Single beam ship vs. single beam ship (no shields)

- **Ship A**: 1 weapon slot, beam weapon dealing 100 dp at range 0, max range 2 (so ~5%/10% dissipation at range 1/2), hull+computer+weapon initiative total = 8. Armor 150, no shields, no deflectors.
- **Ship B**: 1 weapon slot, beam weapon dealing 80 dp at range 0, max range 1 (so ~10% dissipation at range 1), initiative total = 5. Armor 120, no shields, no deflectors.
- Both are each other's only legitimate primary target; assume they close to range 0 by round 2 and hold there (Maximise Damage tactic, both beam-armed).

Round 1 (movement only — both are still closing, no weapon in range yet): no firing.

Round 2 (both at range 0):
- Firing order: Ship A (initiative 8) fires before Ship B (initiative 5).
- Ship A's shot: `100 dp * (1 - 0) = 100 dp` (range 0, no dissipation) applied straight to Ship B's armor (no shields to absorb first). Ship B: 120 - 100 = 20 armor remaining.
- Ship B's shot (survives, still in the queue since it wasn't destroyed before its turn): `80 dp * (1 - 0) = 80 dp` to Ship A's armor. Ship A: 150 - 80 = 70 armor remaining.

Round 3 (both still at range 0):
- Ship A fires first again: 100 dp vs. Ship B's remaining 20 armor → Ship B's single ship is destroyed (100 ≥ 20, one whole ship's worth of armor exceeded). Ship B's token is removed from the board before it can fire back this round.
- Battle ends here since Ship B's race has no ships/starbases left at this location (last-race-standing condition), assuming no other tokens were present.

This demonstrates: initiative determining who fires first each round, beam damage applying directly to armor when no shields are present, and a token being destroyed before it can return fire in the same round.

#### Example 2 — Torpedo ship vs. shielded target

- **Ship C**: 3 torpedoes in one slot, each dealing 40 dp per hit, torpedo accuracy (already including C's computers) = 60%. Initiative total = 6.
- **Ship D**: Armor 150, Shields 90 (shields ≥... actually shields < armor here so torpedo APN formula's second branch would apply for targeting purposes — not needed for this worked shot, just noted for consistency). Initiative total = 3 (Ship D is unarmed for this example, to isolate the torpedo resolution).

Round 1, Ship C fires its 3-torpedo salvo at Ship D (range in torpedo range, no dissipation modeling for torpedoes — only beams dissipate with range per §6):
- Each of the 3 torpedoes is resolved independently at 60% hit chance. Suppose (for a concrete illustration) 2 hit and 1 misses.
- The missed torpedo deals `40 * 1/8 = 5 dp` to shields only. Shields: 90 - 5 = 85.
- Each hit torpedo deals up to half its damage to shields and the rest to armor while shields remain: `20 dp to shields, 20 dp to armor` per hit. Two hits: shields take 40 more (85 - 40 = 45 remaining), armor takes 40 (150 - 40 = 110 remaining).
- End of round 1: Ship D has 45 shields, 110 armor remaining, un-destroyed.

Round 2, same salvo composition (60% each), suppose this time all 3 hit:
- Each hit splits half-to-shields/half-to-armor while shields last: 20 dp to shields each. Ship D's 45 remaining shields absorb 45 of the 60 dp directed at shields (2.25 torpedoes' worth); the overflow (15 dp) that shields can't fully absorb, plus the direct-to-armor half of each hit, all lands on armor. Concretely: total shot damage = 120 dp (3 x 40); shields can only take 45 more before they're gone, so 45 dp is absorbed by shields (shields now at 0) and the remaining 75 dp goes to armor. Armor: 110 - 75 = 35 remaining.
- Ship D survives round 2 with 0 shields, 35 armor.

Round 3, same salvo, suppose 2 of 3 hit (shields already at 0, so all hit damage now goes straight to armor; the 1 miss still only ever affects shields, and there are none left to affect, so it has no effect):
- 2 hits x 40 dp (now fully to armor, since there's no shield pool left to split with) = 80 dp to Ship D's 35 remaining armor → Ship D's ship is destroyed.

This demonstrates: independent per-torpedo hit/miss resolution, the 1/8-damage-to-shields-only rule for misses, the roughly-half-to-shields split for hits while shields remain (with overflow correctly falling through to armor once the shield pool is exhausted mid-shot), and eventual whole-ship destruction once armor is exceeded.

#### Example 3 — Capital missile vs. a shielded target, illustrating the post-shield double-damage rule

- **Ship E**: 1 capital-missile slot, 2 missiles, each dealing 60 dp per hit, accuracy 50%.
- **Ship F**: Armor 100, Shields 40.

Round 1: Both of Ship E's 2 missiles resolve at 50% each; suppose both hit.
- Total hit damage = 120 dp. Shields (40) absorb their normal share first: since shields (40) < armor (100), and shields are being depleted, the shield pool of 40 is consumed. The doubling rule only applies to the portion of damage that lands *after* shields are fully depleted — so of the 120 dp, enough is first accounted against the 40-point shield pool (using the same up-to-half-per-hit-to-shields logic as ordinary torpedoes while shields remain: 60 dp of the 120 is shield-eligible, but only 40 of that is actually available to absorb, the rest overflows), and any damage that ends up landing on armor after the shield pool hits zero is doubled. Working through it: shields absorb 40 dp total (fully depleting them); the remaining 80 dp of the shot, which would have hit armor at normal rate, is instead doubled to 160 dp against armor (per the capital-missile-vs-depleted-shields rule). Ship F's 100 armor is far exceeded (160 ≥ 100) → Ship F's ship is destroyed in a single salvo.

This demonstrates the capital-missile-specific "double damage to armor once shields are down" rule, and shows why capital missiles are documented as capable of one-shotting shield-reliant designs once their shield pool is exhausted, in contrast to ordinary torpedoes which never get this doubling (compare Example 2, where the same raw damage numbers would not have destroyed the ship without capital-missile status).

## Open Questions / Uncertainties

- **Exact accuracy formula.** No source fetched during this research stated the precise closed-form equation combining base weapon accuracy, computer bonus, and jammer effect (e.g., whether it's additive, multiplicative, or something else, and whether it's clamped). The starsfaq.com "Guts" reference explicitly says the correct rounding/derivation was posted separately by a community member (William Butler) but that article's text was not located. Only empirical before/after numbers from community testing were found (see §6) — these should be used to validate a candidate formula, not treated as a spec themselves. **Recommendation: treat the exact formula as unverified until independently re-derived from more empirical hit/miss data, or until the underlying community article is located.**
- **Energy capacitor beam-damage bonus.** Community strategy articles state that "energy capacitors" meaningfully increase beam weapon damage, but no source found here gives the exact percentage or stacking rule (unlike deflectors, whose `0.9^n` formula is explicitly documented).
- **Beam APN range modifier.** The article proposing `RangeModifier = 1 - 0.1*(Range/MaxRange)` inside the targeting-attractiveness formula explicitly flags this term as an inference the author had not been able to test directly, distinct from the (separately well-documented) beam range-dissipation rule that reduces actual damage dealt. Whether attractiveness truly incorporates range at all is not confirmed.
- **Battleship weapon-slot firing order generality.** The documented top-6/bottom-6/top-2/bottom-2/center-4 firing order was reported for one specific hull (Battleship). Whether every hull has its own idiosyncratic positional firing order among same-initiative slots, or whether this is an edge case only relevant when multiple slots on the *same hull* tie on initiative, is unclear from available sources.
- **What happens to un-retreated ships when the 16-round cap is hit.** Sources describe the round cap as ending the battle but do not explicitly state whether all remaining hostile fleets simply stay stacked at that location (and would fight again the following turn, since nothing about the trigger condition changed) or whether some other resolution applies. This document assumes the former as the most consistent reading, but it is an inference, not a directly documented statement.
- **Whether "one missile can only ever count toward killing one ship" is a real distinct rule.** The targeting-attractiveness article asserts the attractiveness formula "doesn't take into account the one missile one kill rule" as the explanation for chaff's effectiveness, implying such a rule exists in the damage/kill-accounting model, but no source found here spells out its mechanics in detail beyond that aside. The whole-ship-kill division described in §6 (total salvo armor damage / current per-ship armor) was the most detailed kill-accounting description located; how it interacts with the "one missile one kill" aside is not fully reconciled by the sources found.
- **Exact canonical weapon/armor/shield numeric stat tables** (e.g., actual dp, range, and initiative values for every weapon, and base armor/shield dp for every hull and defensive component at each tech level) were not captured from any source in this research pass — the official Player's Guide PDF's detailed combat chapters (referenced in its own table of contents as "The Guts of Combat", "Movement, Initiative and Firing in Battle", "Armor, Shields and Damage") could not be extracted as readable text with the tools available in this session. These numbers should be sourced separately (e.g., from an accessible copy of the manual's tech-level tables or a community-maintained tech spreadsheet) before finalizing implementation constants; the worked examples above intentionally used clearly-labeled placeholder numbers instead of guessing at real ones.

## Sources

- [Guts of the Battle Engine — The Stars! FAQ (starsfaq.com)](http://starsfaq.com/battleengine.htm) — primary source for battle triggering, grid/tokens, round/movement structure, the six movement tactics, initiative firing order, and damage/shield/armor resolution.
- [Guts (Advanced/Technical FAQ), §4.7 Beam Deflectors, §4.14 Targeting, §4.14.1 Battleship slot order — starsfaq.com](http://www.starsfaq.com/advfaq/guts2.htm)
- [Stars! Advanced and Technical FAQ, table of contents — starsfaq.com](http://www.starsfaq.com/advfaq/contents.htm)
- [Targeting Order in Battles (Target = Attractiveness), by Art Lathrop — Stars!-R-Us article, starsfaq.com](http://starsfaq.com/articles/sru/art201.htm) — attractiveness/APN formulas.
- ["Frigates vs. Cruisers...which are better?" — Stars!-R-Us article, starsfaq.com](http://starsfaq.com/articles/sru/art104.htm) — worked real accuracy numbers for computers/jammers vs. capital missiles and torpedoes.
- ["When should you NOT use the maximum number of computers?" by Robert Croson, Jr. — Stars!-R-Us article, starsfaq.com](http://starsfaq.com/articles/sru/art172.htm) — jammer-stacking diminishing-returns data, capital missile vs. torpedo jamming resistance, energy capacitor/beam-ship notes.
- [STARS! The Premiere Space Strategy Game — Player's Guide (official manual PDF, hosted at archive.org)](https://ia800508.us.archive.org/14/items/manual_Stars/Stars.pdf) — table of contents confirms official chapter structure ("The Guts of Combat," "Movement, Initiative and Firing in Battle," "Armor, Shields and Damage," "Battle Plans"); full chapter text could not be extracted with available tooling in this session, so it was not used as a factual source beyond confirming section titles/organization.

Not used as sources (attempted but inaccessible during this research session): `wiki.starsautohost.org` (blocked by an active bot-detection challenge page at fetch time) and `web.archive.org` (fetch tool declined to retrieve archive.org Wayback Machine pages in this environment).
