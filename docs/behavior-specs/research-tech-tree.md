# Research and Tech Tree Mechanics

Clean-room behavior specification, compiled entirely from public community
documentation (fan FAQs, the Stars!AutoHost wiki, and a GameFAQs strategy
guide) and restated in original wording. No game code, executable, or
disassembly was consulted. See **Sources** at the end for every URL used,
matched to the facts it supports. Where sources disagreed or a mechanic
could not be pinned down, this is flagged explicitly under **Open
Questions** rather than presented as settled fact.

## Overview

Stars! organizes all technology into six independent **fields**:

- Energy
- Weapons
- Propulsion
- Construction
- Electronics
- Biotechnology

Each field has its own integer **tech level**, running from 0 (the
starting value for most races) up to a maximum of 26. Raising a field's
level is what unlocks better hulls, components, weapons, armor, shields,
scanners, terraforming, and other race abilities — the "tech tree" is
really six separate ladders, with individual rungs (levels) gating
individual unlocks, and some unlocks gated behind a *combination* of
levels in two or three different fields at once.

Research is paid for with the same **resources** that a planet would
otherwise spend on factories, mines, terraforming, defenses, and ships.
Every colony produces resources every year from its population and its
operating factories; whatever isn't spent on that colony's own
construction queue can instead be funneled into the empire's shared
research effort. There is a single empire-wide "next-field(s) to
research" setting and a single "percent of resources devoted to
research" setting — research is not managed per-planet, only production
is.

The cost of buying the next level in a field grows steeply (a
Fibonacci-like curve for roughly the first dozen levels, then a flatter
climb), is further scaled by a per-field, per-race cost setting chosen at
race design time ("cheap" / normal / "expensive"), and also scales
gently with how much total technology the empire has already
accumulated across *all* fields combined.

Besides straightforward research spending, players can also acquire
tech levels from other empires — by scrapping, destroying in battle, or
invading and capturing enemy ships/planets that are more advanced in a
field than the player is.

## Mechanics

### 1. Generating resources (the fuel for research)

A colony's yearly resource output is controlled by five race-design
settings (chosen once, at race creation, each within the listed range):

| Symbol | Meaning | Range | Notes |
|---|---|---|---|
| A | Colonists needed per 1 resource | 700 – 2500 | lower = more resources per colonist |
| B | Resources produced per 10 operating factories | 5 – 15 | |
| C | Resources needed to build one factory | 5 – 25 | |
| D | Factories operable per 10,000 colonists | 5 – 25 | |
| G | Resources needed to build one mine | 3 – 15 | (mineral-side settings F/H are not relevant to research) |

A colony's resources for the year are:

```
resources = floor(population / A) + floor(operableFactories / 10) * B
```

where `operableFactories = min(factoriesBuilt, population/10000 * D)`.

A commonly cited illustrative default is A=1000 (1 resource per 1,000
colonists), matching the community shorthand "1 resource per 1000 pop
plus factory output" [gamefaqs.gamespot.com]. Only one of the two
production channels (population, factories) is needed for research
purposes — a fully "-f" (factory-less) race still produces resources
purely from population.

Because these five values are race-design choices, two races with
identical population can have very different resource (and therefore
research) output. The Alternate Reality PRT is a documented special
case: it has no planetary population/factories in the normal sense and
instead grows its planetary resource output as its **Energy** tech
level rises [wiki.starsautohost.org/wiki/Custom_Race_wizard].

### 2. From resources to research points

Resources allocated to research convert to research progress **1:1** —
there is no separate "research point" currency; the same numbers that
buy factories/mines/ships buy tech levels, just accounted against a
different budget line. Two settings control how many of a colony's
resources reach research in a given year:

- **Research percentage** (0–100%, empire-wide): the target share of
  total empire resource income earmarked for research before/alongside
  planetary construction.
- **Per-planet "contribute only leftover resources to research"
  checkbox**: when checked for a colony, that colony ignores the global
  research percentage entirely and instead sends research only whatever
  resources are left over after its own production queue (factories,
  mines, terraforming, defenses, ships) is fully funded for the year.

A widely repeated piece of community strategy advice observes that,
functionally, **a colony always spends any leftover resources on
research regardless of the percentage slider** — so setting the global
research percentage to 0% while leaving "contribute only leftover
resources" unchecked on every planet produces the same result as
checking that box everywhere: every planet builds everything it can
first, and only the remainder feeds research
[starsfaq.com/articles/sru/art82.htm]. Raising the percentage above 0%
(without the leftover-only checkbox) instead *reserves* a slice of a
colony's resources for research even when that colony still has useful
things it could build — deliberately trading short-term colony growth
for faster tech.

### 3. Cost curve for tech levels

The authoritative community derivation ("Guts of research costs",
credited to Bob Martin) gives the formula:

```
totalCost = (baseCost[level] + totalLevels * 10) * costFactor
```

- `baseCost[level]` — a fixed table, keyed only by the level being
  purchased (see below), identical for every field.
- `totalLevels` — the sum of the empire's current tech levels across
  **all six fields combined** (available on the in-game Score screen).
  This term makes every subsequent level, in every field, gradually
  more expensive as the empire's overall tech investment grows.
- `costFactor` — set per field at race-design time:
  - `0.5` if that field is set "Costs 50% less" ("cheap")
  - `1.0` if left at the normal setting
  - `1.75` if that field is set "Costs 75% extra" ("expensive")
- If the game parameter **"Slow Tech Advance"** is enabled for the
  game, the whole `totalCost` is doubled.

[starsfaq.com/advfaq/guts1.htm]

Base cost table (identical for all six fields):

| Level | Base Cost | Level | Base Cost |
|---|---|---|---|
| 1 | 50 | 14 | 18,040 |
| 2 | 80 | 15 | 22,440 |
| 3 | 130 | 16 | 27,050 |
| 4 | 210 | 17 | 31,870 |
| 5 | 340 | 18 | 36,900 |
| 6 | 550 | 19 | 42,140 |
| 7 | 890 | 20 | 47,590 |
| 8 | 1,440 | 21 | 53,250 |
| 9 | 2,330 | 22 | 59,120 |
| 10 | 3,770 | 23 | 65,200 |
| 11 | 6,100 | 24 | 71,490 |
| 12 | 9,870 | 25 | 77,990 |
| 13 | 13,850 | 26 | 84,700 |

The sequence approximates a Fibonacci progression through level ~12,
then flattens into roughly linear growth. Taking every one of the six
fields from level 0 to level 26 at normal cost, starting from an
otherwise-untouched empire, totals **4,185,240** resources (this figure
already folds in the `totalLevels * 10` surcharge along the way)
[starsfaq.com/advfaq/guts1.htm].

**Race-design cost settings.** Each of the six fields is independently
set to normal, "Costs 50% less," or "Costs 75% extra" during race
creation; this is a fixed racial trait for the whole game, not a
per-turn choice. A race can also check "All 'Costs 75% extra' research
fields start at Tech 3" (Tech 4 instead, for a Jack-of-All-Trades race),
which immediately grants free levels in every field so flagged, for a
flat race-design point cost regardless of how many fields qualify — so
the more fields marked "expensive," the more this flat-fee option is
worth taking [wiki.starsautohost.org/wiki/Custom_Race_wizard]. Community
guidance repeatedly notes that a first "cheap" field is a good value in
race-design-point terms, a second is markedly worse value, and the
efficient move is usually to pair a "cheap" field with an "expensive"
one so the point costs roughly offset (nicknamed "3.5 cheap techs" by
the community) [wiki.starsautohost.org/wiki/Race_Design],
[gamefaqs.gamespot.com].

Two lesser racial traits directly interact with cost:

- **Bleeding Edge Technology**: newly-reachable tech initially costs
  double; the cost drops back to normal once the empire's level in
  every prerequisite field exceeds the requirement by at least one
  level. It also changes component miniaturization from 4%/level
  (capped 64%) to 5%/level (capped 80%)
  [wiki.starsautohost.org/wiki/Custom_Race_wizard].
- **Generalized Research** — see section 4 below; it does not change
  `totalCost`, only how a turn's research budget is spread across
  fields.

### 4. Allocating a turn's research budget across fields

By default, **all** of a turn's research resources go to a single
field at a time — the one the player has currently selected as the
research target (or, with the "lowest field" auto-target option, the
game auto-selects whichever of the six fields currently has the lowest
level and points the whole budget there).

The lesser racial trait **Generalized Research** changes this: only
half (50%) of the turn's research budget is applied to the
currently-selected field, and 15% of the budget is additionally applied
to *each* of the other five fields. Because 50% + 5×15% = 125%, this
trait yields more total research throughput than a focused strategy for
the same resource spend, at the cost of being unable to "rush" a single
field to unlock something urgently
[wiki.starsautohost.org/wiki/Generalized_Research],
[wiki.starsautohost.org/wiki/Custom_Race_wizard]. One strategy-guide
author's independent tally of "50% + 5×15%" likewise reaches 125%, even
though the in-game help text is reported to say 115% — the two
community sources disagree on the label though not on the arithmetic
(see Open Questions) [gamefaqs.gamespot.com].

The Super Stealth PRT has an unrelated, passive way to gain resources
in *all six* fields simultaneously every year: it gains, in each field,
resources equal to half the average that all races (including itself)
spent in that field that turn, as long as at least one other race
exists in the game [wiki.starsautohost.org/wiki/Custom_Race_wizard].

### 5. Leftover / carryover of research progress

Community sources describe research spending as accumulating toward
the cost of the *next* level in a field, turn over turn, until the
threshold in the cost table (section 3) is met. Nothing in the sources
consulted suggests that progress is ever discarded mid-level — a colony
that contributes a small amount of research in one turn and more in a
later turn is described as building toward the same next-level target
over time (e.g., the "contribute only leftover resources" strategy
explicitly relies on dribbling in whatever a colony can spare, turn
after turn, until a level is eventually bought)
[starsfaq.com/articles/sru/art82.htm]. Whether resources in *excess* of
the exact amount needed to complete a level are banked forward toward
the following level (rather than simply lost for that turn) is treated
here as an **assumption** consistent with the community's descriptions,
not a fact any source states in so many words — see Open Questions.

### 6. Tech trading (acquiring levels from other empires)

Independent of the resources spent on research, a player can pick up a
single tech level per turn from another empire, via any one of:

1. **Scrapping**, at one of the receiving player's own starbases, a
   fleet built with a component requiring higher tech than the receiver
   currently has.
2. Having an **armed ship present in a battle** where an enemy ship
   with superior tech in some field is destroyed.
3. **Invading** a planet belonging to a player who leads in one or more
   fields.

For each qualifying event, there is first a flat 50% chance that
nothing at all is learned. If that 50% check passes, then for each tech
field in which the source (scrapped/destroyed/invaded) party is
strictly ahead, there's a further 50% chance of learning *that* field,
so the chance of learning at least one field given `n` fields of
advantage is:

```
P(learn) = 0.5 * (1 - 0.5^n)
```

Only one tech level, from one field, from one source, can be gained
per turn regardless of how many qualifying events occur; multiple
qualifying events in the same turn each roll independently, so playing
more of them (e.g. scrapping several separate fleets, or running
several invasion sites at once) raises the chance that *at least one*
succeeds, following `1 - (1 - P(learn))^k` for `k` independent
attempts. Turn order matters: scrapping resolves first, then
waypoint-zero invasions, then battles, then waypoint-one invasions
[starsfaq.com/advfaq/guts1.htm].

Two organized multiplayer techniques exploit this: a "wolf/sheep" ring
where allies deliberately destroy obsolete ships in battle to spread
tech gains among several allies at once, and a "tech trade by invasion"
loop where two allies repeatedly swap ownership of one low-population
planet each turn, each invasion carrying its own independent chance of
a tech gain for the invader [starsfaq.com/advfaq/guts1.htm].

### 7. Prerequisites for hulls, components, and other unlocks

Most items require a single field/level pair (e.g., "Construction 3"
unlocks the Destroyer Hull); a number of the more interesting items
require two or three fields to each reach a minimum level
simultaneously. Selected documented breakpoints
[wiki.starsautohost.org/wiki/Chapter_6:Early_Resource_Management]:

- **Energy 1** (+ Biotechnology 1): basic ±3% temperature terraforming.
- **Energy 2**: first minelayer; with Propulsion 3, the Maneuvering Jet;
  with Propulsion 6, the first ram scoop engine (Radiating-Hydro Ram
  Scoop).
- **Energy 3** (+ Electronics 7, Biotechnology 2): first penetrating
  scanner.
- **Weapons 5** (+ Electronics 6): first LBU installation-killing bomb;
  (+ Biotechnology 7): first Smart Bomb.
- **Weapons 10** (+ Propulsion 2): first torpedo with better than 50%
  base accuracy.
- **Propulsion 5** (+ Construction 5): first stargate.
- **Construction 3**: Cargo Pod, Destroyer Hull, Medium Freighter Hull.
- **Construction 4**: Privateer Hull; Space Dock starbase (requires
  also having chosen the Improved Starbases trait); (+ Electronics 2):
  Robo-Mini-Miner (requires Advanced Remote Mining).
- **Electronics 7** (+ Energy 3, Biotechnology 2): first scanner with
  penetrating range (unavailable to races with the No Advanced Scanners
  trait).
- **Biotechnology 4** (+ Energy 2): minelaying ability for non-Space
  Demolition races.

This is only a representative subset focused on early/mid-game
breakpoints; the full unlock table for every hull, engine, weapon,
armor, shield, and scanner is considerably larger and was not
exhaustively catalogued for this document. See Open Questions.

### Worked Example A — single field, early game, normal cost

Assume a colony with population 500,000, A = 1,000 colonists/resource,
100 operating factories at B = 10 (10 resources per 10 factories),
producing:

```
resources = 500,000/1,000 + (100/10)*10 = 500 + 100 = 600 / year
```

The empire has all six fields at level 0 (`totalLevels = 0`) and is
researching **Energy** (normal cost, `costFactor = 1.0`), with the
research percentage set high enough that 150 resources/year reach
research.

- **Year 1**: 150 available.
  - Level 1 cost = (50 + 0×10) × 1.0 = **50**. Spend 50, 100 remain.
  - `totalLevels` is now 1 (Energy is 1, everything else 0).
  - Level 2 cost = (80 + 1×10) × 1.0 = **90**. Spend 90, 10 remain.
  - `totalLevels` is now 2.
  - Level 3 cost = (130 + 2×10) × 1.0 = **150**. Only 10 available —
    not enough; the 10 are banked toward level 3.
  - End of Year 1: **Energy 2**, with 10/150 banked toward Energy 3.
- **Year 2**: another 150 arrives, plus the 10 banked = 160 available
  toward the 150 needed for level 3.
  - Level 3 completes, spending 150; 10 resources are left over.
  - `totalLevels` is now 3.
  - Level 4 cost = (210 + 3×10) × 1.0 = **240**. The remaining 10 bank
    toward it.
  - End of Year 2: **Energy 3**, with 10/240 banked toward Energy 4.

### Worked Example B — cost-factor and Generalized Research interaction

An empire has taken **Generalized Research**, made **Weapons** "cheap"
(costFactor 0.5), and made **Biotechnology** "expensive" (costFactor
1.75); Energy, Propulsion, Construction, and Electronics are all
normal cost. Current levels: Energy 5, Propulsion 5, Construction 5,
Electronics 5, Biotechnology 5, Weapons 8 — so `totalLevels = 5×5 + 8 =
33`. The player has selected **Weapons** as the primary research
target, with a 400-resource/year research budget.

Generalized Research splits the 400 as: 200 (50%) to Weapons, and
60 (15%) to each of the other five fields (Energy, Propulsion,
Construction, Electronics, Biotechnology) — 200 + 5×60 = 500 total
resource-equivalent of research applied, 125% of the 400 spent.

- **Weapons** (8→9, cheap): cost = (2,330 + 33×10) × 0.5 = (2,330 +
  330) × 0.5 = **1,330**. The 200 allocated only covers 200/1,330 of
  the level.
- **Energy** (5→6, normal): cost = (550 + 330) × 1.0 = **880**. The 60
  allocated covers 60/880 ≈ 6.8% of the level.
- **Biotechnology** (5→6, expensive): cost = (550 + 330) × 1.75 =
  **1,540**. The same 60 allocated covers only 60/1,540 ≈ 3.9% of the
  level — visibly less progress than Energy received for the identical
  resource contribution, purely because of the 1.75× cost multiplier.

This illustrates why "expensive" fields are usually chosen for
techs a race cares least about: under Generalized Research's flat
side-allocations, an expensive field converts the same resources into
proportionally less progress every single turn.

### Worked Example C — multi-planet "leftover resources only" accumulation

Two colonies are both set to "contribute only leftover resources to
research," per the community strategy of leaving the global research
percentage irrelevant and letting fully-developed planets fund research
automatically [starsfaq.com/articles/sru/art82.htm]. The empire is
researching **Construction** (normal cost), currently at level 3, with
all other fields at 0, so `totalLevels = 3`.

- **Colony A**: generates 300 resources this year; its production queue
  (more factories, mines) still has 250 resources' worth of useful work
  queued, so only 300 − 250 = **50** resources are leftover for
  research.
- **Colony B**: generates 150 resources; it is already fully built out
  (queue empty), so all **150** resources are leftover for research.
- **Empire research income, Year 1**: 50 + 150 = 200.

Construction level 4 costs (210 + 3×10) × 1.0 = **240**. The 200
available is short by 40; it is banked toward level 4.

- **Year 2**: Colony A's production queue has advanced further and now
  only needs 180 resources of the same 300 it produces, leaving 120
  for research; Colony B still contributes its full 150 (still nothing
  else to build). Empire research income = 120 + 150 = 270.
  Combined with the 40 banked from Year 1: 310 available against the
  240 needed.
  - Construction reaches **level 4**, consuming 240; 70 resources are
    left over.
  - `totalLevels` becomes 4.
  - Construction 5 costs (340 + 4×10) × 1.0 = **380**. The 70 leftover
    resources bank toward it.
- End of Year 2: **Construction 4**, with 70/380 banked toward
  Construction 5, achieved without the player ever manually touching
  the research percentage slider on either colony.

## Open Questions / Uncertainties

- **Exact overflow/carryover rule.** No source consulted states in
  precise terms whether resources spent on research in excess of the
  exact amount needed to complete a level are carried forward to the
  next level in that field, or are instead lost for that turn. The
  worked examples above assume carryover, consistent with general
  community descriptions of gradual, turn-by-turn accumulation, but
  this specific edge case is an inference, not a confirmed rule.
- **Point in a turn at which `totalLevels` is evaluated.** The "Guts of
  research costs" formula defines `totalLevels` as the empire's current
  total across all fields, but does not say whether, within a single
  turn, a field that levels up partway through resource allocation
  immediately raises `totalLevels` for the *next* field's cost
  calculation that same turn, or whether `totalLevels` is fixed for the
  whole turn and only updates at turn-end. The worked examples in this
  document assume immediate updates (consistent with how the original
  FAQ's own weapons-cost examples reconcile against the base-cost
  table), but this was not independently confirmed by a second source.
- **115% vs. 125% for Generalized Research.** The Stars!AutoHost wiki
  and a GameFAQs strategy guide both independently compute 50% + 5×15%
  = 125% total research yield for the Generalized Research trait, but
  the guide's author notes that the in-game help text is reported to
  say 115%. No source consulted resolves this discrepancy; it may be a
  documentation error in the original game, a rounding/labeling
  convention not captured by either wiki, or a misremembering by the
  guide's author.
- **Precise default values for the resource-generation constants.**
  Sources give a valid *range* for each race-design economic setting
  (e.g., colonists-per-resource 700–2500) and cite various example
  "defaults" (1,000 colonists/resource; 10 or 15 resources per 10
  factories; 9 or 10 resources to build a factory) that are not fully
  consistent with one another across sources — likely because different
  authors are describing different predefined races (e.g. the
  "Humanoid" starting race) rather than a single wizard-neutral
  default. This document treats 1,000/10/10/10 as a reasonable
  illustrative baseline for worked examples, not a verified universal
  default.
- **Full hull/component prerequisite table.** Only a representative
  subset of tech-gated unlocks (focused on early/mid-game breakpoints)
  was catalogued here from a single strategy-guide chapter. A complete,
  field-by-field prerequisite table for every hull, engine, weapon,
  armor, shield, and scanner in the game was not compiled and would
  need a dedicated pass through the in-game Technology Browser data or
  an equivalent community reference (e.g. a hull/component spreadsheet)
  not retrieved for this document.
- **"Slow Tech Advance" and other game-parameter interactions.** The
  source formula states this parameter doubles `totalCost`, but no
  source consulted described how it interacts with `costFactor` or
  `totalLevels` beyond a flat doubling of the final result — assumed
  here to apply after the `costFactor` multiplication, consistent with
  the formula's own ordering, but not independently verified.

## Sources

- [Stars! Advanced and Technical FAQ — Table of Contents](http://www.starsfaq.com/advfaq/contents.htm)
- [Stars! Advanced and Technical FAQ — "Guts!" (bombing, tech trading §4.2, research costs §4.3)](http://www.starsfaq.com/advfaq/guts1.htm)
- ["The ultimate way of managing research" by Andrei Romanov — Stars!-R-Us Article](http://www.starsfaq.com/articles/sru/art82.htm)
- [Generalized Research — Stars!wiki](https://wiki.starsautohost.org/wiki/Generalized_Research)
- [Race Design — Stars!wiki](https://wiki.starsautohost.org/wiki/Race_Design)
- [Custom Race wizard (View Race Help) — Stars!wiki](https://wiki.starsautohost.org/wiki/Custom_Race_wizard)
- [Chapter 6: Early Resource Management — Stars!wiki (Stars! Strategy Guide)](https://wiki.starsautohost.org/wiki/Chapter_6:Early_Resource_Management)
- [Population Management — Stars!wiki](https://wiki.starsautohost.org/wiki/Population_Management)
- [Tech analysis: weapons Vs anything else, by Nick Bennett (5th April 2001) — Stars!wiki](https://wiki.starsautohost.org/wiki/Tech_analysis:_weapons_Vs_anything_else_by_Nick_Bennett_-_5th_April_2001)
- [Stars! Strategy Guide (PC), by "plague006" / Mars Jenkar — GameFAQs](https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043)
