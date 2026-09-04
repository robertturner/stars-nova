# Race Traits: Primary Racial Traits, Lesser Racial Traits, and Race Customization

## Overview

Every race in *Stars!* is built in the "Custom Race Wizard," a six-step process that produces a
self-contained set of modifiers consumed by every other subsystem in the game. The wizard is a
**zero-sum points economy**: the designer starts from a neutral baseline, and every choice either
spends or refunds "advantage points" (commonly called RW points, for Race Wizard). The design is
only valid to save/play if the running point total is zero or greater at the end of the six steps.
Leftover positive points are not wasted — they convert directly into starting resources.

A race is defined by three layers, all of which this document covers:

1. **Primary Racial Trait (PRT)** — exactly one of ten mutually exclusive archetypes. This is the
   single biggest lever in race design: it changes which hulls/components exist, which formulas
   apply to the race, and often overrides a rule that applies to everyone else.
2. **Lesser Racial Traits (LRTs)** — zero or more of fourteen optional modifiers, each independently
   priced (positive for a net benefit, negative/refunding for a net drawback, or mixed).
3. **Sliders** — habitability tolerance per environment axis (Gravity, Temperature, Radiation),
   colonist growth rate, economic efficiency (colonists/resource, factory and mine cost/output/
   upkeep), and per-field research cost multipliers. Every slider position has an RW point cost or
   refund.

The PRT/LRT/slider choices captured here are **inputs** consumed elsewhere: they parameterize the
formulas that belong to the population-growth, production-queue, research-tech-tree, and
combat-resolution specs being written in parallel. This document names those touch points but does
not restate their internal formulas.

Sources consulted are listed at the end; most of this material comes from the Stars!AutoHost
community wiki (which mirrors the game's own in-application help text) and two long-standing
player-written strategy guides.

## Mechanics

### 1. The advantage-points economy

- The wizard shows a running "unused advantage points" counter that must stay at or above zero to
  save the race. Small conveniences cost a few points; powerful abilities cost many; disadvantages
  refund points. [wiki: Custom Race wizard]
- **Leftover points at save time** convert to immediate starting resources, split across five
  possible sinks the player chooses among: 10 kT of surface minerals per point (weighted toward
  whichever mineral the homeworld is poorest in), 1 extra mine per 2 points, 1 extra factory per 5
  points, 1 extra defense installation per 10 points, or +1% concentration of the homeworld's
  scarcest mineral per 3 points. [wiki: Custom Race wizard; GameFAQs Strategy Guide PG1-00]
- **LRT stacking penalty:** picking more than four LRTs makes each additional one progressively more
  expensive (including negative ones, which normally refund points), and lopsided picks (four or
  more negative LRTs with no positive ones, or vice versa) carry an extra point penalty. The exact
  formula for this escalation was not found in any accessible source — see Open Questions.
  [wiki: Lesser racial traits]
- Because the interaction between any one setting and the final point total is race-specific
  ("one part art, one part science" per the GameFAQs guide), most individual point costs below
  should be read as **illustrative, source-reported data points**, not a complete authoritative
  price list — see Open Questions for the scope of what is and is not documented publicly.

### 2. Primary Racial Traits (choose exactly one)

The ten PRTs split loosely into "economic" (forgiving, growth/tech oriented) and "war/toy" (combat
hardware oriented) camps, plus Alternate Reality, which is structurally unlike the other nine.
[wiki: Primary racial traits]

| PRT (abbrev.) | Core mechanical identity | Key numeric modifiers |
|---|---|---|
| **Hyper Expansion (HE)** | Rapid, disposable colonization | Colony growth rate is **2x** the slider value (up to 40% effective from a 20% slider); maximum population per planet is **halved** relative to a race with the same habitability; cannot build Stargates; exclusive Mini-Colonizer and shape-shifting Meta Morph hulls, "Settler's Delight" free-Warp-6 engine, Flux Capacitor (+20% beam damage). |
| **Super Stealth (SS)** | Universal cloaking + passive research | All ships/starbases carry an inherent **75% cloak**; travels minefields **1 warp faster** than the posted safe limit; passively gains research equal to **half the galaxy-wide average spend** in each field (as long as another race exists) added on top of its own budget; exclusive theft-capable scanners (Pick Pocket, Robber Baron) and stealth hulls (Rogue, Stealth Bomber). |
| **War Monger (WM)** | Offense-specialist | Weapons cost **25% less**; gets a **half-square movement bonus** in battle; colonists fight better on defense/invasion; instantly learns the exact design of any enemy ship once scanned; exclusive Battle Cruiser/Dreadnought hulls and Gatling Neutrino Gun/Blunderbuss weapons. Trade-off: **cannot build minelayers or lay minefields**, and is restricted to SDI/Missile-Battery planetary defenses only. |
| **Claim Adjuster (CA)** | Terraforming specialist | Terraforming is **free and instantaneous** every year up to current tech (reverts if the planet changes hands); can degrade or improve *other* players' planets from orbit (Orbital Adjuster, Retro Bomb); planets it holds long-term randomly drift **+1%** toward its ideal on a vital stat, permanently. Widely regarded as the strongest PRT economically and frequently banned/handicapped in community games. |
| **Inner Strength (IS)** | Defensive specialist | Planetary defenses cost **40% less**; colonists defend better and heal faster; colonists aboard freighters keep reproducing (at half rate) and beam down surplus population; exclusive Super Freighter, Croby Sharmor combined armor/shield, Jammer torpedo-deflectors. Trade-off: weapons cost **25% more**, and it has no access to Smart/Neutron/Enriched-Neutron/Peerless/Annihilator bombs. |
| **Space Demolition (SD)** | Minefield specialist | Sole access to essentially all mine types and both dedicated mine-laying hulls; own minefields **decay at 1%/planet/year** versus **4%/planet/year** for everyone else's; can remotely detonate its own standard minefields; crosses enemy minefields **2 warps faster** than the safe limit; minefields double as non-penetrating scanners. No offensive/defensive combat bonus of its own. Commonly cited as netting the most starting advantage points of any PRT. |
| **Packet Physics (PP)** | Mass-driver specialist | Starts with a second homeworld-tier planet (non-tiny universes) and Mass Driver tech up to level 13; mineral packets are cheaper/smaller, carry a built-in penetrating scanner, and can terraform the target planet on arrival (50% chance of +1% to one stat, plus a small chance per 100 kT of mineral not caught); can sense every player's packets in flight. |
| **Interstellar Traveller (IT)** | Stargate specialist | Starts with two Stargate-equipped planets; Stargates cost **25% less**, can eventually be built with **no range/mass limit**, can carry cargo through gates without it counting against the gate's mass limit, and gate-overrun is less likely to destroy the ship. Trade-off: Mass Drivers are only half as effective at catching incoming packets, are worse at flinging them, and its own flung packets always decay regardless of speed. |
| **Alternate Reality (AR)** | Lives on starbases, not planets | Population lives aboard starbases (culminating in the huge Death Star hull) rather than on the planet surface; maximum population is set by **starbase size**, not planet habitability; can remote-mine its own planets; resource production is driven by a **PRT-specific formula keyed to Energy tech level** rather than the standard population/habitability formula (see §5); starbases cost 20% less (does not stack with Improved Starbases). Trade-off: cannot build any planetary installations, and loses **3% of any in-transit fleet's population per year** of travel. |
| **Jack Of All Trades (JOAT)** | Generalist / beginner-friendly | Starts at **tech level 3** in all six research fields (see §6); maximum population per planet is **+20%**; Scout/Frigate/Destroyer hulls get a built-in scanner scaling with Electronics tech. Considered the most forgiving PRT to design and play but with no standout economic or military edge. |

[wiki: Primary racial traits; wiki: Custom Race wizard; wiki: Race Design; GameFAQs Strategy Guide PG2-01 through PG2-10 and ANR-00]

### 3. Lesser Racial Traits (choose zero or more, individually priced)

LRTs are grouped by the wiki into "positive" (net beneficial, so always worth taking if the point
cost is acceptable), "negative" (net drawback, taken only to recoup points), and a few with mixed
effects.

| LRT (abbrev.) | Category | Effect |
|---|---|---|
| **Improved Fuel Efficiency (IFE)** | Positive | Ships use **15% less fuel**; unlocks the Fuel Mizer and Galaxy Scoop engines; starting Propulsion tech **+1** level. |
| **Total Terraforming (TT)** | Positive | Planetary hab values can be nudged **±3%** immediately at colonization; ordinary (non-Claim-Adjuster) terraforming can eventually reach **±30%**; terraforming resource cost is **30% lower**. |
| **Advanced Remote Mining (ARM)** | Positive | Unlocks 3 extra mining hulls and 2 extra mining robots; starts with 2 Midget Miners already built. Mutually exclusive in effect with OBRM (see below). |
| **Improved Starbases (ISB)** | Positive | Unlocks the light-ship-capable Space Dock and the heavy Ultra Station starbase designs; starbases cost **20% less** and carry an inherent **20% cloak**. |
| **Ultimate Recycling (UR)** | Positive | Scrapping a fleet at a starbase recovers **90%** of its minerals (plus some resources) instead of the default; scrapping at a bare planet recovers about half that rate. |
| **Mineral Alchemy (MA)** | Positive | Converting resources into minerals (alchemy) is roughly **4x** more efficient than the baseline rate available to every race. |
| **Generalized Research (GR)** | Negative/mixed | Only **half** of the resources allocated to the player's chosen research field are actually applied there; **15%** of the total additionally splashes onto *each* of the other five fields. (The game's own help text describes this as a 115% aggregate return; naive arithmetic on "50% + 5×15%" gives 125% — the community FAQ that documents this flags the same discrepancy without resolving it. See Open Questions.) |
| **No Ram Scoop Engines (NRSE)** | Negative | All free-fuel "ram scoop" engines above Warp 4 become unavailable; in exchange, the race gains the Interspace-10 engine (safe travel at Warp 10 without a high Propulsion level). |
| **Cheap Engines (CE)** | Negative/mixed | Engines cost **50% less** to build and starting Propulsion is **+1** level, but at Warp 7–10 (sources differ on whether the threshold is Warp 6 or Warp 7 — see Open Questions) there is a **10%** chance per turn the engines simply fail to engage that year. One source lists this LRT as worth "up to 80" advantage points. |
| **Only Basic Remote Mining (OBRM)** | Negative/mixed | Restricts remote mining to the basic Mini-Miner hull/robot only, but raises maximum population per planet by **+10%**. Overrides ARM if both are somehow selected. |
| **No Advanced Scanners (NAS)** | Negative/mixed | No penetrating scanners are available, but every conventional (non-penetrating) scanner gets **double** its listed range. |
| **Low Starting Population (LSP)** | Negative | Starting colony population is **30% lower** than the default. |
| **Bleeding Edge Technology (BET)** | Negative/mixed | Any tech level not yet reached by *every other player* costs **2x** the normal research cost, dropping back to normal cost only once the field is at least 1 level ahead of every other player's; in exchange, miniaturization of component costs/mass progresses **5% per tech level up to an 80% cap**, versus the default **4% per level up to 75%**. |
| **Regenerating Shields (RS)** | Mixed | All shields are **40% stronger** than their listed rating and regenerate **10%** of their strength between rounds within a single battle; armor is worth only **50%** of its listed rating. |

Commonly documented pairings: **IFE + NRSE** (buys into the efficient Fuel Mizer engine while
letting NRSE's refund offset IFE's cost) and **ISB + RS** (favors small, shield-tanked early
warships that rarely carry armor anyway). [wiki: Lesser racial traits; GameFAQs Strategy Guide
PG3-01 through PG3-14; wiki: Chapter 3: Building a Monster Race]

### 4. Habitability and growth-rate sliders

Each race sets an independent tolerance sub-range on three environment axes, each of which has a
game-wide absolute range:

- **Gravity:** 0.12g – 8.00g
- **Temperature:** −200°C – 200°C
- **Radiation:** 0 mR – 100 mR

The designer narrows or widens the band on each axis (trading colonizable-planet frequency for RW
points — a narrower band refunds points; a wider one costs them), and radiation is the one axis
whose galaxy-wide distribution is uniformly random rather than centered, so sliding the radiation
band toward an edge is generally regarded as "free" advantage points relative to sliding gravity or
temperature (whose actual planet distribution is weighted toward the middle of the absolute range).
A race may instead declare **full immunity** on one axis (its value on that axis is always treated
as 100% regardless of the planet's real reading); this is expensive — one accessible source puts a
single immunity at **725 advantage points**, and a second immunity at **1,448 points total** (i.e.,
about 723 more on top of the first). [wiki: Chapter 3: Building a Monster Race]

**Per-axis habitability contribution and the immunity multiplier.** The three axis values combine
multiplicatively into an overall 0–100% planet value for a given race (the exact formula and its
edge cases belong to the population-growth spec; this document only notes how PRT/LRT/immunity
choices feed into it). A widely cited illustrative example: a planet that reads 50% of "ideal" on
each of the three axes for a no-immunity race multiplies out to roughly **0.5 × 0.5 × 0.5 ≈ 12%**
overall habitability (the source notes real in-game values fall off a bit faster than a clean
product, so the true number could be somewhat lower). Adding immunity on just one axis pins that
axis at 100%, turning the same planet into **1.0 × 0.5 × 0.5 = 25%** — roughly double, for a single
725-point purchase — and the same doubling effect applies to how much a fixed terraforming budget
can move the final percentage. [wiki: Chapter 3: Building a Monster Race]

**Growth-rate slider.** The maximum colonist growth-rate slider itself runs from **1% to 20%** (an
Hyper Expansion race effectively doubles whatever is chosen, up to a 40% ceiling, per §2). Community
convention ties the choice to race archetype rather than to a hard rule: "Hyper-Growth" designs
generally sit at 16–19% with wide habitability (roughly 1-in-4 to 1-in-6 planets colonizable);
"Hyper-Production" designs run a slightly lower 16–17% with narrow habitability (1-in-6 to 1-in-10)
and heavier factory investment; "-f" (factory-less) builds push as high as 20%; Alternate Reality
races commonly run lower, around 13–14%; and tri-immune "monster" builds (only really viable on AR
or HE) push growth rate as low as 4–5% to afford the immunity cost. [wiki: Race Design; wiki:
Chapter 3: Building a Monster Race]

The growth-rate slider value, the HE ×2/½-population modifiers, the OBRM +10%/LSP −30% population
modifiers, and the per-planet habitability percentage derived above are all **inputs** to the
per-planet population growth formula maintained by the population-growth spec; this document does
not restate that formula.

### 5. Economic (production) sliders

Step 5 of the wizard exposes the same variables that parameterize the standard resource/factory/mine
formulas (owned by the production-queue spec): colonists needed per resource point (range roughly
700–2,500, in steps of 100), resources produced per 10 factories (5–15), resources needed to build
one factory (5–25), factories operable per 10,000 colonists (5–25), a checkbox that cuts 1 kT of
Germanium off each factory's build cost, and the mirrored set of controls for mines (output per 10
mines 5–25, resources per mine 3–15, mines operable per 10,000 colonists 5–25). Representative point
costs reported by players: tightening colonists-per-resource from 1000 to 900 costs roughly 200
points; a factory-build cost of 9 costs about 20 points, 8 about 80, and 7 about 180; the Germanium
discount checkbox is a flat 58 points; a mine-build cost of 3 costs about 43 points versus about 189
for 2. [GameFAQs Strategy Guide PG5-00; wiki: Chapter 3: Building a Monster Race]

**PRT override — Alternate Reality's production formula.** AR does not use the standard
population/habitability-driven resource formula at all. Instead, per the same community strategy
guide, its planetary resource output follows:

```
Resources = HabitabilityValue x sqrt(Population x EnergyTechLevel / EfficiencyCoefficient)
```

where `EfficiencyCoefficient` is the same Step-5 "factory efficiency" dial every race sets, and
`EnergyTechLevel` substitutes for the role Energy tech plays nowhere else in the standard formula.
This is the clearest example in the source material of a PRT literally swapping out a formula owned
by another subsystem (production) rather than just multiplying its output. AR's related scanning
range formula is also distinct: `ScanRange = sqrt(Population / 10)`. [GameFAQs Strategy Guide
ANR-00]

### 6. Research-cost sliders

Each of the six tech fields (Energy, Weapons, Propulsion, Construction, Electronics, Biotechnology)
is independently set to Cheap (50% of normal cost), Normal (100%), or Expensive (175%). A
community-documented approximation of the underlying per-level cost formula (owned in full by the
research-tech-tree spec; repeated here only to show where the slider plugs in) is:

```
totalCost = (baseLevelCost + totalLevelsAcrossAllFields x 10) x costFactor
costFactor = 0.5 (cheap) | 1.0 (normal) | 1.75 (expensive)
```

doubled again if the game was started with the "Slow Tech Advance" option. `baseLevelCost` follows a
roughly Fibonacci-like curve for the first dozen levels (e.g., level 1 = 50, level 5 = 340, level 10
= 3,770, level 20 = 47,590) before flattening out. [starsfaq.com: "Guts of Research Costs"]

A separate checkbox, "all Expensive fields start at tech level 3" (level 4 instead, for a JOAT race),
costs a **flat 60 points** regardless of how many fields are actually set to Expensive, and is
generally only worth taking once at least three fields are Expensive. [GameFAQs Strategy Guide
PG6-00]

**Where PRT/LRT choices modify this:**

- **JOAT** starts every field at tech level 3 for free (see §2), which is a direct substitute for
  the early-game cost the formula above would otherwise charge.
- Several PRTs grant specific starting tech levels outright (see table below), again bypassing the
  formula for those initial levels.
- **IFE** and **CE** each add +1 to starting Propulsion specifically.
- **BET** (see §3) doubles the effective `costFactor` for any level not yet reached by every other
  player in the game, and separately changes the miniaturization curve that the production-queue
  spec uses to discount component cost/mass by tech level.
- **Super Stealth**'s passive research-sharing ability (§2) adds resources to a field from outside
  the normal per-planet resource pool entirely, rather than changing the cost formula.

**Starting tech levels granted by PRT** (Energy / Weapons / Propulsion / Construction / Electronics
/ Biotechnology), as tabulated on the wiki's Race Design page:

| PRT | Ener | Weap | Prop | Cons | Elec | Bio |
|---|---|---|---|---|---|---|
| HE | – | – | – | – | – | – |
| SS | – | – | – | – | 5 | – |
| WM | 1 | 6 | 1 | – | – | – |
| CA | 1 | 1 | 1 | 2 | – | 6 |
| IS | – | – | – | – | – | – |
| SD | – | – | 2 | – | – | 2 |
| PP | 4 | – | – | – | – | – |
| IT | – | – | 5 | 5 | – | – |
| AR | 1 | – | – | – | – | – |
| JOAT | 3 | 3 | 3 | 3 | 3 | 3 |

[wiki: Race Design]

Note this table conflicts in two places with the prose descriptions found elsewhere for the same
PRTs — flagged explicitly in Open Questions below rather than silently reconciled.

## Worked Numeric Examples

### Example A — Immunity's effect on a single planet's habitability value

Take an illustrative planet that reads 50% of "ideal" on Gravity, Temperature, and Radiation for a
given race (this is a simplified teaching example from the source, not necessarily achievable by a
real in-game planet).

- **No immunity:** 0.5 × 0.5 × 0.5 = **0.125 → ≈12% habitability value.**
- **Same race, Gravity immunity purchased (725 points):** 1.0 × 0.5 × 0.5 = **0.25 → 25%
  habitability value** — roughly double, for a fixed 725-point spend.
- **Same race, all three axes immune (1,448 points):** 1.0 × 1.0 × 1.0 = **100% on every green
  planet** — every colonizable planet becomes maximally productive, at the cost of enough points
  that growth rate and/or economic sliders typically have to be scaled back sharply to stay
  non-negative (community guides note tri-immune growth rates as low as 4–5%, versus the 16–19%
  typical of a wide-habitability, no-immunity design). [wiki: Chapter 3: Building a Monster Race]

### Example B — Hyper Expansion vs. Jack Of All Trades at the same slider settings

Suppose both a Hyper Expansion race and a Jack Of All Trades race set the growth-rate slider to
**15%** and would otherwise support a maximum population of 1,000,000 on a fully ideal (100%
habitability) homeworld.

| | Effective growth rate | Effective max population (100% world) |
|---|---|---|
| Baseline (no PRT modifier) | 15% | 1,000,000 |
| **Hyper Expansion** | 15% × 2 = **30%** | 1,000,000 × 0.5 = **500,000** |
| **Jack Of All Trades** | 15% (unmodified) | 1,000,000 × 1.2 = **1,200,000** |

HE reaches its (lower) population cap roughly twice as fast per-planet as the baseline would, then
stalls — which is exactly why HE designs are built around colonizing many small planets rather than
a few large ones, while a JOAT (or an OBRM racial pick, which adds a further +10% on top of any
PRT's number) leans the opposite way, favoring fewer, larger colonies. [wiki: Primary racial traits;
wiki: Custom Race wizard]

### Example C — Research-cost multiplier stacking (Normal vs. Expensive vs. Expensive+BET)

Using the approximate cost formula from §6, `totalCost = (baseLevelCost + totalLevels×10) ×
costFactor`, and reading level 10's base cost as 3,770 (from the starsfaq.com table) at a point in
the game where the race's summed tech levels across all fields is still small enough to ignore the
`+totalLevels×10` term for illustration:

- **Normal (costFactor 1.0):** 3,770 resources to reach level 10.
- **Cheap (costFactor 0.5):** 3,770 × 0.5 = **1,885 resources.**
- **Expensive (costFactor 1.75):** 3,770 × 1.75 = **6,597.5 → ≈6,598 resources.**
- **Expensive + Bleeding Edge Technology**, while the race is not yet ahead of every opponent in
  that field: BET doubles the effective cost on top of the slider, so 6,598 × 2 ≈ **13,195
  resources** — roughly **3.5x** the Normal-slider cost for the same nominal tech level, entirely
  from stacking two race-design choices (an Expensive research slider and the BET LRT) rather than
  from the underlying per-level curve itself.

This illustrates why guides uniformly recommend offsetting every Cheap field with a matching
Expensive field (each refunds/costs a comparable number of RW points), and why BET is generally
paired only with races that intend to stay at or near the tech frontier rather than trailing it.
[starsfaq.com: "Guts of Research Costs"; wiki: Lesser racial traits; GameFAQs Strategy Guide
PG6-00]

### Example D — Two documented, fully-specified trait combinations (for scale/sanity-checking)

Two worked race builds appear in the community strategy guide's "monster race" chapter, useful as
end-to-end sanity checks against whatever numbers a clean-room implementation produces:

- **"Hyper-Growth" build:** PRT Claim Adjuster; LRTs Improved Fuel Efficiency, No Ram Scoop Engines,
  Only Basic Remote Mining, Improved Starbases, Low Starting Population; habitability narrowed to
  roughly 1-in-9 planets colonizable pre-terraform (widening toward 1-in-4 once mid-tier terraform
  tech is reached, thanks to CA's free/instant terraforming); growth rate slider **19%**; colonists
  per resource 1,000; factories needing 8 resources to build; mines costing 3; research set
  Expensive on five fields and Cheap on Weapons, with the "starts at 3" checkbox checked; the design
  still had 7 leftover points (converted to starting surface minerals).
- **"Hyper-Production" build:** PRT Jack Of All Trades; LRTs Improved Fuel Efficiency, No Ram Scoop
  Engines, Improved Starbases, No Advanced Scanners; full Gravity immunity with a narrow
  Temperature/Radiation band; growth rate slider **15%**; colonists per resource 2,500 (the wide
  end of the range, trading early sluggishness for a very high late-game per-planet ceiling —
  reportedly up to ~3,980 resources on a maxed, fully-terraformed world); mines costing 4; research
  Expensive on four fields, Normal on Electronics (to keep JOAT's built-in scanner improving), Cheap
  on Weapons, with the "starts at 4" checkbox checked and the design landing at exactly 0 leftover
  points.

[wiki: Chapter 3: Building a Monster Race]

## Open Questions / Uncertainties

- **No documented absolute point-cost table.** Every point-cost figure in this document is a
  data point reported by a player-written guide for one specific setting change on one specific
  race-in-progress, not an authoritative price list. We found no publicly documented, complete
  table mapping every PRT choice, every LRT choice, and every slider position to its exact RW point
  cost. Reproducing the wizard's true point economy precisely will likely require either finding a
  more complete source or empirically reverse-engineering the curve from many observed race designs
  (community-shared race files), independent of the original program's internals.
- **LRT stacking-penalty formula is unknown.** Sources agree that choosing more than four LRTs, or
  choosing four-or-more of only one sign (all positive or all negative), triggers an escalating
  point penalty, but no source gives the actual formula or schedule.
- ~~**War Monger's starting Weapons tech level is inconsistent across sources.**~~ **RESOLVED by
  direct empirical testing (2026-09-03):** ran the actual game (Stars! v2.70j / JRC3, via otvdm on
  a clean-room-legitimate copy with a community-donated free serial), created a custom race with
  the War Monger PRT, and read its in-game Race Wizard description text directly: *"You start the
  game with a knowledge of Tech 6 weapons and Tech 1 in energy and propulsion."* This confirms the
  Race Design wiki table's **Weapons 6** (not the 5 reported by the Custom Race Wizard help-text
  reproduction, GameFAQs guide, and other secondary sources — those were wrong, or describing a
  different patch). Energy 1 / Propulsion 1 confirmed as already agreed. Screenshot preserved at
  `docs/ui-reference/wizard-war-monger-description.png`.
- ~~**Claim Adjuster's starting Construction tech level is uncertain.**~~ **RESOLVED by direct
  empirical testing (2026-09-03):** created a custom Claim Adjuster race (no LRTs, default
  sliders) in the actual game and opened the in-game Research screen (Commands > Research, F5) to
  read starting tech levels directly off the "Technology Status" panel: **Energy 1, Weapons 1,
  Propulsion 1, Construction 2, Electronics 0, Biotechnology 6.** This confirms the Race Design
  wiki table's **Construction 2** entry — it was not a transcription error, the prose sources
  (Custom Race Wizard help text, GameFAQs guide) simply omitted it. Screenshot preserved at
  `docs/ui-reference/research-screen-claim-adjuster-starting-tech.png`.
- **Generalized Research's advertised return percentage doesn't match its own arithmetic — PARTIALLY
  investigated by direct testing (2026-09-03), still not fully resolved.** We read the actual
  in-game Custom Race Wizard description text for Generalized Research directly (Stars! v2.70j /
  JRC3): *"Only half of the resources dedicated to research will be applied to the current field of
  research; however, 15% of the total will be applied to all other fields."* This is the verbatim
  primary-source wording — but it does **not** state an aggregate percentage (no "115%" or "125%"
  figure appears anywhere in this wizard screen), and it is itself ambiguous between "15% to each of
  the other five fields" (125% aggregate, the interpretation this spec and the wiki use elsewhere)
  and "15% split across all other fields collectively" (65% aggregate). We did not find the disputed
  "115%" figure in this screen; it may live in a different in-game help topic (e.g. the .hlp file's
  Generalized Research entry, not the wizard's inline description) not checked in this pass, or may
  be a misremembering in the secondary source that originally reported it. Screenshot preserved at
  `docs/ui-reference/wizard-generalized-research-description.png`. Still open: confirm the per-field
  15% reading (vs. split-total) and locate/rule out the 115% figure with a multi-turn research test
  comparing actual resource-to-tech-level conversion across fields.
- **Cheap Engines' warp threshold is inconsistently reported.** One source (the wiki's dedicated CE
  article) states the 10%-failure-to-engage penalty applies at Warp 7, 8, 9, or 10; the GameFAQs
  guide and the Custom Race Wizard help text both say "speeds in excess of Warp 6." We were unable
  to determine which is authoritative.
- **The exact per-planet habitability formula (how the three 0–100% axis scores combine, and how a
  planet's raw Gravity/Temperature/Radiation reading maps to its 0–100% axis score in the first
  place) is intentionally left to the population-growth spec.** The "0.5 × 0.5 × 0.5" example used
  above is explicitly flagged by its own source as simplified/illustrative rather than the literal
  in-game formula, and we did not find a fully specified, sourced version of the real formula during
  this research pass; the population-growth spec should treat this as a gap to fill from its own
  sources, not assume the simplified product model is exact.
- **No confirmed value for AR's `EfficiencyCoefficient` bounds or Death Star build cost**, and no
  second independent source corroborating the AR-specific production formula in §5 — it comes from a
  single strategy guide. Worth corroborating against another source before treating it as certain.
- ~~**PP's "Mass Driver tech up to level 13" was implemented as Energy tech level 4.**~~ **RESOLVED
  by cross-referencing this project's own component data (2026-09-05):** "level 13" refers to the
  mass-driver component's own tier number, not a raw Energy tech level — this codebase's `Mass
  Driver`-family components are named `Mass Driver 5/6`, `Super Driver 7/8/9`, and `Ultra Driver
  10/11/12/13`, and `components.xml` lists `Ultra Driver 13` (the tier the spec's "up to level 13"
  refers to) as requiring **Energy tech 24** to unlock, not 4 (which only reaches `Mass Driver 5`).
  Fixed in `GameInitialiser.cs`. This is a data cross-reference, not a live-game re-verification
  like the War Monger/Claim Adjuster entries above — worth confirming against the real game if it's
  ever reachable again.
- **The entire "distinct starting fleet/planet-count per PRT" system is effectively unimplemented,
  beyond Hyper Expansion's 3x colony ship count and (as of 2026-09-05) Packet Physics/Interstellar
  Traveler's second planet.** `ServerState/NewGame/StarMapInitialiser.cs` contains a `switch` over
  every PRT listing what it *should* start with as ships (an armed scout for War Monger, two
  shielded scouts for Packet Physics, two mine layers for Space Demolition, a destroyer and a
  privateer for Interstellar Traveler, two scouts/a medium freighter/a mini miner/a destroyer for
  Jack Of All Trades, a distinct "orbital construction colony ship" for Alternate Reality, an
  orbital terraforming ship instead of a normal colony ship for Claim Adjuster, etc.) — but the
  entire `switch` is inside a `/* ... */` comment block and has never executed. Every race
  currently starts with exactly the same one scout + one colony ship + one starbase (three for
  HE's colony ships specifically), regardless of PRT. This is a substantially larger feature than
  the single second-planet fix above — it needs the specific hull/component loadout for each
  PRT's bonus ships confirmed (most are only sketched as one-line comments here, not sourced the
  way the tech-level numbers above are) before it can be implemented with the same confidence.

## Sources

- Stars!AutoHost community wiki, "Primary racial traits" — https://wiki.starsautohost.org/wiki/Primary_racial_traits (accessed via Wayback Machine snapshot)
- Stars!AutoHost community wiki, "Lesser racial traits" — https://wiki.starsautohost.org/wiki/Lesser_Racial_Traits
- Stars!AutoHost community wiki, "Race Design" — https://wiki.starsautohost.org/wiki/Race_Design
- Stars!AutoHost community wiki, "Custom Race wizard" — https://wiki.starsautohost.org/wiki/Custom_Race_wizard
- Stars!AutoHost community wiki, "Chapter 3: Building a Monster Race" — https://wiki.starsautohost.org/wiki/Chapter_3:Building_a_Monster_Race
- Stars!AutoHost community wiki, "Cheap Engines" — https://wiki.starsautohost.org/wiki/Cheap_Engines
- starsfaq.com, "Guts" FAQ section 4.3, "Guts of Research Costs" — http://starsfaq.com (Guts page, section 4.3)
- Mars Jenkar / plague006, *Stars! Strategy Guide* v1.11, GameFAQs — https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043
- General web search results summarizing the above wiki pages (used to locate primary pages; not quoted directly): search queries run against wiki.starsautohost.org and related community sites.

Note on access: wiki.starsautohost.org served an interstitial/anti-bot challenge when fetched
directly during this research session; all wiki content above was retrieved through Wayback Machine
snapshots of the same pages (https://web.archive.org/web/*/https://wiki.starsautohost.org/wiki/*),
which mirror the live wiki's content.
