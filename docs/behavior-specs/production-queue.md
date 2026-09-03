# Production Queue and Resource Allocation

Behavior specification for planetary production — how population and minerals become factories, mines, defenses, terraforming, and ships — in the 1995-2000 4X game *Stars!*, written for a clean-room reimplementation. Facts below are restated in original wording from public sources: the official *Stars! Player's Guide* (the published manual, read here from an OCR text transcription of the scanned book hosted at archive.org — this is the printed player-facing documentation, not any decompiled or disassembled artifact), the long-standing community site *starsfaq.com* ("Stars!-R-Us" article archive), and an unofficial HTML mirror of the original *Stars! Official Strategy Guide* chapters. Some additional figures are corroborated only through search-engine snippets of the *wiki.starsautohost.org* community wiki, whose pages could not be fetched directly in this research session (the site's Cloudflare bot-check blocked automated retrieval); those figures are marked as lower-confidence and re-listed under Open Questions. No content was derived from the original binary or any decompilation/disassembly artifact.

## Overview

Every inhabited planet has exactly one production queue: an ordered work list processed top to bottom, once per year (turn). Two independently-tracked economic quantities feed it:

- **Resources** — an abstract unit of "work," generated each year by (a) the planet's population and (b) any operating factories. Resources pay for everything: factories, mines, defenses, terraforming, ships, and research.
- **Minerals** (Ironium, Boranium, Germanium) — physical stockpiles extracted from the planet by mines (or received via freighters/mineral packets), consumed by items that need them (factories need Germanium; most ships need some mix of all three; mines, defenses, and terraforming typically need only resources).

Each year, the game walks the queue from top to bottom, applying that year's resource and mineral income to whichever item is at the head of the queue until it either completes, is blocked by a mineral shortfall, or the planet runs out of resources for the year; any resources still unspent after satisfying the queue flow into research rather than being wasted. Items above a certain threshold of completion retain their invested progress across turns — nothing is "banked" back into a generic pool, but a specific item's percent-complete carries forward until it either finishes or is deleted (which forfeits the investment). Auto-build entries (open-ended "up to N" orders for Mines, Factories, Defenses, Mineral Alchemy, and Terraforming) behave differently from ordinary queued items: they never block the queue and are simply skipped in a year they can't be executed.

Sources: [STARS! Player's Guide, Ch. 6 "Planets" and Ch. 7 "Production"](https://archive.org/details/manual_Stars); [Chapter 6: Early Resource Management — Stars! Official Strategy Guide mirror](http://www.deepsky.com/~gpeters/stars/www.anrokima.de/Strategie_Handbuch/ssg/ssg06.htm)

## Mechanics

### 1. Resources from population

A colonist's economic productivity is a race-design setting, described in the *Player's Guide* as "resources per colonist": it defines how many colonists it takes to generate one resource per year, independent of factories. The manual's own worked description of the default setting is:

> "One resource is generated each year for every 1000 [colonists]"

with the same page describing the most favorable end of that design slider as roughly one resource per 700 colonists (the manual's OCR text renders this figure ambiguously as "seven," which in context — paired with the well-documented 1000-colonist default and the game's known race-wizard slider range — is almost certainly "700"; treated here as likely but not fully certain, see Open Questions). Formally, for a race using the default setting:

```
population_resources = floor(population / 1000)
```

This is the *only* resource source that requires no built infrastructure — it is what lets a freshly-colonized world (with zero factories) start producing anything at all.

Source: [STARS! Player's Guide, Ch. 7, "Conditions That Affect Production" / View Race page 5](https://archive.org/details/manual_Stars); corroborated by [Chapter 6: Early Resource Management — Stars!wiki (via search index)](https://wiki.starsautohost.org/wiki/Chapter_6:Early_Resource_Management)

### 2. Resources from factories

Factories are described in the manual as working like "virtual colonists": once built they produce resources every year at no ongoing cost, and unlike colonists they don't need anything to keep running. Quoting the manual directly:

> "Factories, along with people, create resources used to build items such as ships, mines, defenses and more factories... For a typical race, you can double the number of resources generated per year by building factories."

> "Factories cost 4 kT of germanium to build or, if you selected Factories Cost 1 kT Less when defining your race, factories cost 3 kT of germanium. No minerals other than germanium are used."

The resources-per-factory rate, the resource cost to build one, and the germanium cost are all race-design settings with a default and a race-customizable range. The default, confirmed directly from the strategy-guide mirror's worked description, is a 1:1 output ratio and a 10-resource build cost:

> "Since new factories will cost 10 resources, it will take a ten-years for a factory to produce a copy of itself" / "With the default settings, 10 factories produce 10 resources per year. That gives you one resource per factory."

So, by default:

```
factory_build_cost      = 10 resources + 4 kT Germanium   (or 3 kT with the "Factories Cost 1 kT Less" race option)
factory_output_per_year = 1 resource / operating factory
```

The manual's own "excel at production" checklist (View Race page 5) gives the favorable extreme of the customizable range: factories that cost only 5 resources (plus 1 kT less germanium) and that each produce up to 1.5 resources/year (worded as "every 10 factories produce 15 resources"), which is also consistent with the *Official Strategy Guide*'s worked "Monster Race" example using settings like "10 produce 12" or up to 15 output with a 7-9 resource cost.

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Factories) and Ch. 7 (View Race page 5)](https://archive.org/details/manual_Stars); [Chapter 2: Basic Race Design — Official Strategy Guide mirror](http://www.deepsky.com/~gpeters/stars/www.anrokima.de/Strategie_Handbuch/ssg/ssg02.htm); [Chapter 3: Building a Monster Race — Official Strategy Guide mirror](http://www.deepsky.com/~gpeters/stars/www.anrokima.de/Strategie_Handbuch/ssg/ssg03.htm)

### 3. Operable factories and mines (the population cap)

A planet can physically *contain* more factories or mines than its current population can *run*. The manual describes a dedicated readout for exactly this distinction:

> "The Minerals on Hand tile shows you the current number of mines and factories operating on a planet, and the maximum number of mines and factories the current population can operate."

The maximum-operable figure scales with population in steps of 10,000 colonists — another race-design setting ("factories/mines operable per 10,000 colonists"), whose favorable extreme the manual gives directly:

> "Colonists may operate 25 factories" / "Every 10,000 colonists can operate 25 mines" [worded as the best-case end of the design slider]

By community convention the unmodified default for both is 10 per 10,000 colonists (this specific default number was not found verbatim in the primary sources fetched for this research — see Open Questions — but it is the figure consistently cited across multiple community strategy sources and is consistent with the internal symmetry of the other default figures confirmed directly above). Formally:

```
operable_factories_cap = floor(population / 10000) * factories_per_10k_setting   (default setting = 10)
operable_mines_cap     = floor(population / 10000) * mines_per_10k_setting       (default setting = 10)
operating_factories    = min(built_factories, operable_factories_cap)
operating_mines        = min(built_mines,     operable_mines_cap)
```

Factories or mines built beyond the current cap are not destroyed or wasted — they simply sit idle (produce nothing) until population growth raises the cap enough to bring them online. This "grow into your infrastructure" dynamic is explicitly used as a strategy in community guides, e.g. one long-standing strategy article describes deliberately letting a homeworld's population climb until "the factories that pop can operate (858)" before changing its build orders.

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Mines/Factories sidebar)](https://archive.org/details/manual_Stars); ["How to Get Over 25,000 Resources by 2450" by Jason Cawley — starsfaq.com](http://starsfaq.com/articles/25k_by_2450.htm)

### 4. Minerals from mines

Mines extract Ironium, Boranium, and Germanium independently, based on each mineral's own surface concentration on that planet (0–100%, tracked separately per mineral). Mining decreases concentration over time (asymptotically — the manual states a planet never truly runs out, extraction just gets very slow near 1% concentration), and every player's home/starting world is guaranteed never to drop below 30% concentration in any mineral, for the life of the game, regardless of who owns it:

> "You never run out of minerals on a planet, you just decrease the concentration until it reaches 1%..." / "The mineral concentration on your home, or starting world never drops below 30."

Like factory output, "mine output" (kT extracted per mine at 100% concentration) and "mine build cost" (in resources) are race-design settings. A community strategy article illustrating a *customized* race spelled the underlying shape of the formula out concretely: with a mine efficiency of 1.2 kT and 24 mines/10,000 pop, a homeworld sitting at the guaranteed 30% concentration floor was reported to produce "450kt-1000kt of germanium per year." The generalized formula:

```
mineral_output(type) = operating_mines * (mine_output_setting / 10) * (concentration_percent(type) / 100)
```

The default mine settings (cost in resources, output per mine, and mines-operable-per-10,000-colonists) were not confirmed with a directly quoted primary-source number in this research pass; commonly-cited community defaults are 5 resources to build, 1 kT/mine/year at 100% concentration (i.e., "10 mines produce 10 kT" at the default output setting), and 10 mines operable per 10,000 colonists — treated here as plausible, symmetric with the confirmed factory defaults, but unverified (see Open Questions). Mines, unlike factories, appear to require only resources (no minerals) to build.

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Mines)](https://archive.org/details/manual_Stars); ["Rapid Colony Development" by Michael Meagher — starsfaq.com](http://starsfaq.com/articles/sru/art77.htm)

### 5. Defenses

Planetary defenses (SDI, Missile Battery, Laser Battery, Planetary Defense, Neutron Battery, etc.) partially protect a planet against bombing, mass-driver mineral packets, and invasion. Adding a "Defenses" item to the queue increases the count of whichever defense type the planet is currently using; upgrading to a better defense *type* happens automatically and for free the instant the relevant technology is researched — it never needs to be queued.

> "Adding defenses increases the number of existing defenses of the type you're currently employing... Upgrading defenses happens automatically. Whenever you learn new technology that applies to defense, all defenses on all your planets upgrade automatically and at no cost."

The manual frames the operable limit on defenses the same way as factories/mines — "you can build as many defenses as you wish, you can only operate as many as your population has resources to handle" — but community sources are consistent that there is also a hard, population-independent ceiling of 100 defenses per planet (the manual's own example screenshot shows a fully-built planet at "100 of 100"). The resource cost to build one defense unit was found cited in a community wiki search snippet as approximately 15 resources, but this could not be corroborated against the primary manual text or against a mineral-cost figure, so it is listed under Open Questions rather than presented as confirmed.

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Building Planetary Defenses)](https://archive.org/details/manual_Stars)

### 6. Terraforming

Terraforming nudges one habitability factor (Gravity, Temperature, or Radiation) by 1% per completed "Terraform Environment" unit, automatically choosing whichever factor is furthest out of the race's preferred range:

> "Each 1% Terraforming task executed will modify one of the environmental factors by 1%, which will improve the overall habitability value by at least 1% and probably more."

Two auto-build variants exist: **Min(imum) Terraforming**, which only fixes factors currently *outside* habitable range (and which the manual singles out as urgent — colonists start dying if a negative-value planet is left un-terraformed for more than a year) and stops once the planet value reaches zero; and **Max(imum) Terraforming**, which keeps improving already-positive factors up to a player-specified percentage ceiling and the limits of researched terraforming technology. Total possible terraforming per factor is capped by tech level, normally maxing out at 15% (potentially higher — see the race trait below).

Community wiki sources (not independently confirmed against the primary manual text in this pass) cite a terraforming cost of 100 resources per 1% terraformed for a standard race. The **Total Terraforming** race trait is explicitly documented in the manual as reducing this cost:

> "Total Terraforming... Terraforming requires 30% less resources and you can research terraforming technologies that improve factors up to 30% instead of just 15% normally."

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Terraforming) and Race Traits appendix](https://archive.org/details/manual_Stars); [Total Terraforming — Stars!wiki (via search index)](https://wiki.starsautohost.org/wiki/Total_Terraforming)

### 7. Mineral Alchemy (turning resources into minerals)

When an item is stuck because a needed mineral has run out, Mineral Alchemy converts resources into minerals directly and can be queued (manually or as an auto-build item) ahead of the blocked item:

> "Each unit of mineral alchemy will turn a mere 100 of your resources (25 if you have the Mineral Alchemy trait) into 1 kT of each of the three minerals."

As an auto-build item placed in front of another item, it activates only in the specific year it's needed to unblock that item; placed last in the queue (or alone), it consumes all remaining resources for the year converting them to minerals.

Source: [STARS! Player's Guide, Ch. 7 "Production" (Unblocking a Production Queue)](https://archive.org/details/manual_Stars)

### 8. Queue ordering, blocking, and partial completion

The queue is a strict top-to-bottom work list:

> "You have one production queue per planet. The queue is essentially a work list. Items are produced in the order shown in the queue, from top to bottom."

Each item tracks its own percent-complete, shown when it's selected. Key documented behaviors:

- **Insertion ahead of a partially-built item does not erase its progress, only pauses it.** "If you add an item to the top of the queue in front of something that is partially complete, your people will not work to complete the original item until the new item you placed in the queue is complete or has been deleted."
- **Deleting a partially-built item forfeits everything spent on it.** "If the item is removed from the queue before completion, resources and minerals already spent on the item are lost."
- **Mineral shortfalls block ordinary items, but not auto-build items.** "Production of items that require minerals is halted if the planet runs out of minerals. Auto-build items that require only resources will continue to be produced." A normal (non-auto-build) item stuck for lack of minerals is described as "blocking" the queue — its remedies are freighting in minerals, flinging a mineral packet, or queuing Mineral Alchemy ahead of it — implying downstream items behind a blocked ordinary item do not receive that year's leftover resources either, until it is unblocked or removed. If projected time-to-completion exceeds 100 years at current mineral income, the item's name is shown in red as a "practically never" warning.
- **Auto-build items never block and are simply skipped** in a year they can't be executed, and they never show progress themselves — instead, the moment work is actually done on one, it manifests as an ordinary partially-completed item for that one unit.
- **Leftover resources go to research, not to next turn's production.** Nothing generated in a turn is wasted: any resources not consumed by the queue (after satisfying every buildable item, or hitting one that blocks) are applied to research that same year. A per-planet "Contribute only leftover resources to research" checkbox governs *when* production claims priority over the player's global research-funding percentage: unchecked, some resources go to research first per the player's normal research allocation and the remainder funds the queue; checked, the queue is fully funded first and only the true leftover goes to research.
- **A planetary disaster (e.g., a comet strike) resets the queue entirely**, losing all in-progress work and its invested resources (though disasters can also deposit windfall minerals usable immediately).

Source: [STARS! Player's Guide, Ch. 7 "Production" (How Production Works, Clearing/Unblocking the Production Queue, Adding Auto-Build Items)](https://archive.org/details/manual_Stars)

### 9. Auto-build vs. manual queue items and production templates

Manually-added items are one-shot orders (optionally multiplied via Shift/Ctrl-modified Add for batches of 10/100/max) that are removed from the queue once built. Auto-build items ("Mines/Factories/Defenses/Mineral Alchemy/Terraforming (Auto Build)") are persistent standing orders phrased as "up to N": they stay in the queue indefinitely, attempting each year to reach (but never exceed) the stated target count of that item, and must be removed manually. A **production template** is a saved, reusable sequence of auto-build items (plus the leftover-resources-to-research setting) that can be applied to any planet's queue in one action; the **default template** auto-applies to every newly founded or captured colony. The manual's own illustrative default template:

```
Minimum Terraform Up to 10%
Factories (Auto Build) Up to 10
Mines (Auto Build) Up to 10
Defenses (Auto Build) Up to 2
Factories (Auto Build) Up to 25
Mines (Auto Build) Up to 25
Maximum Terraform Up to 10%
Defenses (Auto Build) Up to 5
```

— reasoned (per the manual) as: fix life-threatening habitability problems first, then continuously grow infrastructure while the colony is young, add a little defense once the colony can afford it, push infrastructure to its population-based ceiling once mature, polish habitability, then invest further in defense.

Source: [STARS! Player's Guide, Ch. 7 "Production" (Production Templates, Adding Auto-Build Items to the Queue)](https://archive.org/details/manual_Stars)

## Worked Examples

All three examples use the confirmed defaults from §1–2 (1 resource / 1000 population; factories cost 10 resources + 4 kT Germanium and yield 1 resource/year each) plus the community-cited, lower-confidence mine defaults from §4 (5 resources to build, 1 kT/mine/year at 100% concentration, no minerals required) purely for illustration — treat the mine-specific numbers in these examples as illustrative, not verified constants.

### Example 1 — A brand-new colony bootstrapping its first factories

- Population: 100,000. No factories or mines built yet. Surface Germanium: 500 kT (a generous starting stock, for illustration).
- Queue: `Factories (Auto Build) Up to 10`.

**Turn 1 calculation:**
1. Population resources: `floor(100000 / 1000) = 100`.
2. No factories exist yet, so factory-derived resources = 0. Total resources available = **100**.
3. Operable factory cap = `floor(100000/10000) * 10 = 100` — far above the 10 the auto-build order targets, so the cap isn't the binding constraint this turn.
4. The auto-build order wants to reach 10 factories. Cost for 10: `10 * 10 = 100 resources` and `10 * 4 = 40 kT Germanium`.
5. Resources (100) exactly cover 10 factories; Germanium (500 kT on hand) easily covers 40 kT. All 10 factories complete this turn. Resources remaining: 0. Germanium remaining: 460 kT.

**Turn 2 calculation** (now with 10 operating factories):
1. Population resources: 100 (population unchanged for simplicity).
2. Factory resources: `10 operating factories * 1 = 10`.
3. Total resources available = **110**.
4. The auto-build order still reads "Up to 10" and 10 already exist, so it is satisfied and skipped (auto-build items never overshoot their stated count). With nothing else queued, all 110 resources flow to research this turn.

This demonstrates the population-resource formula, the default factory cost/output, an auto-build order being fully satisfiable in one turn, an auto-build order going idle (not deleted) once its target is met, and leftover resources defaulting to research.

### Example 2 — A maturing colony with a mixed manual + auto-build queue and a mid-queue mineral bottleneck

- Population: 400,000. Currently operating: 30 factories, 20 mines. Surface Germanium: 25 kT (mining hasn't kept pace with factory demand).
- Queue, top to bottom: `Scout` (manual, one-shot; costs 10 resources + 2 kT Ironium), `Factories (Auto Build) Up to 40`, `Mines (Auto Build) Up to 40`, `Defenses (Auto Build) Up to 5` (starting from 0 defenses; illustrative cost 15 resources each, per the lower-confidence figure in §5).

**Turn calculation:**
1. Population resources: `floor(400000/1000) = 400`.
2. Factory resources: operable cap is `floor(400000/10000)*10 = 400`, well above the 30 actually built, so all 30 operate: `30 * 1 = 30`.
3. Total resources available = **430**.
4. Top of queue, the `Scout`: costs 10 resources + 2 kT Ironium; assume Ironium is plentiful. Spend 10 resources → **420 remaining**. Scout completes and leaves the queue.
5. `Factories (Auto Build) Up to 40`: 10 more factories are needed (40 − 30). Full cost would be `10*10=100 resources` and `10*4=40 kT Germanium`, but only 25 kT Germanium is on hand — the mineral-limited affordable amount is `floor(25/4) = 6` factories (24 kT Germanium, 60 resources). Because this is an auto-build item, it simply builds as many as it can afford (6) rather than blocking the queue: spend 60 resources and 24 kT Germanium → **360 resources remaining**, 1 kT Germanium left on hand, 36 factories now operating.
6. `Mines (Auto Build) Up to 40`: 20 more mines needed (40 − 20), at the illustrative 5-resources/no-minerals cost: `20*5=100 resources`, fully affordable → **260 resources remaining**, 40 mines now operating.
7. `Defenses (Auto Build) Up to 5`: 5 defenses at an illustrative 15 resources each = 75 resources, fully affordable → **185 resources remaining**, 5 defenses now built.
8. Queue now has nothing left to fund. All **185** leftover resources flow to research this turn.

This demonstrates top-to-bottom consumption across multiple queue entries in a single turn, a one-shot manual item consuming resources before any auto-build item gets a turn, an auto-build item being mineral-constrained yet still not blocking the queue (it just does as much as it can), and the residual flowing to research after every queued need is met.

### Example 3 — A high-value ship blocked by a mineral shortage, then freed with Mineral Alchemy

- Population: 600,000, generating 600 population resources/year, plus 50 operating factories (50 resources/year) = 650 resources/year.
- Surface minerals: Ironium 300 kT, Boranium 20 kT, Germanium 10 kT.
- Queue, top to bottom: `Battleship` (manual, one-shot; illustrative full cost 500 resources + 200 kT Ironium + 150 kT Boranium + 100 kT Germanium), `Mineral Alchemy (Auto Build)` placed *after* it, `Factories (Auto Build) Up to 60`.

**Turn 1 calculation:**
1. Total resources available: 650.
2. The `Battleship` is the top (and, being an ordinary manual item, blocking) entry. Its Boranium requirement (150 kT) and Germanium requirement (100 kT) both exceed what's on the surface (20 kT and 10 kT respectively) — per §8, an ordinary item that requires minerals it doesn't have halts; no resources are diverted past it to the auto-build items below it this turn, even though 650 resources sat unused. The queue is effectively frozen on this item.
3. Because the projected wait for enough Boranium/Germanium income (at the colony's current mining rate) is severe, the Battleship's queue entry would be shown in red once its time-to-completion estimate exceeds 100 years (per §8) — the player's cue to intervene.

**Turn 2 — player reorders the queue to add Mineral Alchemy ahead of the Battleship:**
1. New queue: `Mineral Alchemy (Auto Build, as needed)`, `Battleship`, `Factories (Auto Build) Up to 60`.
2. Mineral Alchemy converts 100 resources into 1 kT of *each* mineral (Ironium, Boranium, Germanium together) per unit. To close the Boranium/Germanium gap for the Battleship as fast as possible, the auto-build alchemy consumes resources this turn — say all 650 available are spent on alchemy since it's positioned to unblock the item behind it: `650 / 100 = 6.5`, i.e. 6 whole units convert for 600 resources, yielding +6 kT to each mineral (Boranium 20→26 kT, Germanium 10→16 kT, Ironium 300→306 kT), leaving 50 resources unspent this turn (insufficient for a 7th unit) which then flow onward — but the Battleship still can't fully complete, so those 50 resources apply as partial progress toward the Battleship's 500-resource cost instead of being wasted or sent to research, since it is still short on Boranium/Germanium and cannot be fully paid for in minerals this turn either. (Whether a normal item can accept a *partial*, proportional resource payment in a turn where its full mineral cost still can't be met — as opposed to only receiving resources in a turn where minerals are fully available — was not confirmed with a directly quoted rule in this research pass; see Open Questions.)
3. Over subsequent turns, continued Mineral Alchemy (and/or freighted-in minerals) closes the remaining Boranium/Germanium gap; once both are available in full, the Battleship's stored resource progress plus that turn's resource income complete it, and the `Factories (Auto Build) Up to 60` order — untouched and unblocked this whole time because it never got a turn while the Battleship blocked the queue above it — finally begins receiving resources.

This demonstrates an ordinary item blocking the entire downstream queue when short on minerals (§8), the red "practically never" warning threshold, using Mineral Alchemy as the documented unblocking tool (§7) with its exact 100-resources-to-1-kT-of-each-mineral conversion rate, and highlights (via the flagged uncertainty in step 2) exactly where this specification's confidence in the turn-by-turn partial-payment mechanic runs out.

## Open Questions / Uncertainties

- **Exact default mine settings — PARTIALLY RESOLVED by direct empirical testing (2026-09-04).**
  Opened the actual Production Queue dialog (Stars! v2.70j/JRC3) for a freshly-created custom race
  with no economic-slider changes from wizard defaults, and selected "Mine": the dialog's own
  "Required Minerals" panel read **Ironium 0kT, Boranium 0kT, Germanium 0kT, Resources 5** —
  confirming the community-cited default build cost of **5 resources, no minerals** directly from
  the game. The same test on "Factory" read **Germanium 4kT, Resources 10**, confirming the
  already-known factory default exactly. Mine output (kT/mine/year at 100% concentration) and
  mines-operable-per-10,000-colonists were not tested this pass (would need a multi-turn mining
  observation) and remain open.
- **Resources-per-colonist favorable extreme.** The manual's OCR text reads "one resource... for every seven colonists," which is almost certainly a scan/OCR corruption of "700 colonists" (consistent with the documented 1000-colonist default and general community accounts of the race wizard's range), but this document could not confirm the exact digit sequence against a second, independently legible copy of the same passage.
- **Defense build cost.** A community wiki search snippet cited approximately 15 resources per defense unit, with no corroborating mineral cost or primary-source confirmation. The 100-defenses-per-planet hard cap is widely and consistently cited in community sources but was not independently confirmed against the primary manual text (which frames the limit only in terms of population/resources, not an explicit numeric ceiling).
- **Terraforming resource cost.** The "100 resources per 1%" figure (and Total Terraforming's 30%-cheaper modifier, which *is* directly confirmed in the manual) comes from a community wiki search snippet, not a directly quoted manual passage. Whether this cost scales with anything else (e.g., current tech level, or the specific environmental factor being changed) is not documented in any source located here.
- **Race-wizard slider ranges (min/max), not just the default and one favorable extreme.** The manual's "excel at production" checklist gives only the single most-favorable endpoint of each economic slider (factory/mine cost, output, and per-10,000-colonist operability), not the full numeric range or the mechanism (points cost) by which a race design trades one for another. The *Official Strategy Guide* mirror's worked "Monster Race" examples show plausible intermediate values (e.g. "13-15/7-9/18-25" for one archetype) but these are example builds, not the underlying range table.
- ~~**Joint resource/mineral bottleneck mechanic for a single partially-built item.**~~ **RESOLVED
  by direct empirical testing (2026-09-04): partial fulfillment does happen, at whole-unit
  granularity, for an ordinary (non-auto-build) batch order — it is not all-or-nothing.** Test:
  created a custom race (Claim Adjuster, no LRTs, default sliders) on its 100%-habitability,
  25,000-population homeworld (249kT Ironium / 206kT Boranium / 265kT Germanium on hand, 10
  factories/10 mines built), and queued `Factory x100` (a manual batch order via Ctrl+Add, costing
  400kT Germanium + 1000 resources total — deliberately far more Germanium than the 265kT on hand)
  followed by `Mines (Auto Build) Up to 1`. Generated one turn (Stars! v2.70j/JRC3, via the actual
  running game). Result: population grew to 28,700 (an exact, independent match to this same
  spec's Example 1 growth-curve table — see §3 — despite coming from a different source/patch),
  Factories built rose from 10 to **13** (i.e. 3 of the 100 queued factories completed, consuming
  30 of the required 1000 resources and part of the required Germanium), and re-opening the
  Production Queue dialog showed the item's remaining quantity as **"Factory 97"** with **"9% Done"**
  progress banked toward the *next* (4th) unit — i.e. leftover resources beyond three whole units'
  worth were *not* discarded, they carried forward as fractional progress exactly as un-blocked
  items do. The `Mines (Auto Build) Up to 1` entry behind it was already satisfied before the turn
  even started (10 mines already built exceeds "up to 1"), so it does not by itself demonstrate
  whether an auto-build item behind a *still-blocking* manual item gets funded the same turn —
  that narrower question (auto-build specifically unblocked mid-turn by the item ahead of it
  running out of things to spend on) remains open. The manual's "halts if the planet runs out of
  minerals" wording is therefore best read as "stops producing *more* once it can no longer afford
  the next whole unit," not as "makes zero progress the instant the full batch cost exceeds what's
  on hand." Exact rounding of the intermediate mineral figure shown for the remaining 97 units
  (380kT, 8kT less than the naive 97×4=388kT expectation) was not fully reconciled and is noted
  here rather than asserted as a precise formula. Screenshots preserved at
  `docs/ui-reference/production-queue-partial-fulfillment.png` and
  `docs/ui-reference/planet-view-after-turn1-generate.png`.
- **Whether idle (population-capped) factories/mines can ever be lost**, e.g. to population decline stranding built infrastructure permanently versus it simply waiting inactive for population to recover — not addressed in any source located here.
- **Direct access to wiki.starsautohost.org was blocked** by the site's automated bot-check (Cloudflare "Just a moment..." interstitial) throughout this research session; per this project's clean-room policy against defeating bot-detection, no attempt was made to bypass it. Facts attributed to that wiki in this document were instead obtained via search-engine result snippets that quote or closely paraphrase its pages — treat these as secondary/lower-confidence versus the directly-read Player's Guide OCR text and starsfaq.com pages (which were fetched and read in full).

## Sources

- [STARS! The Premiere Space Strategy Game — Player's Guide, official manual (archive.org item page)](https://archive.org/details/manual_Stars) — primary source for population/factory resource formulas and defaults, factory germanium cost, the operable-factories/mines population cap concept, defense build/upgrade behavior, terraforming mechanics and the Total Terraforming trait, Mineral Alchemy's conversion rate, and the entire Production Queue chapter (ordering, partial completion, blocking/unblocking, auto-build behavior, templates). Read via the item's OCR full-text transcription: `https://archive.org/download/manual_Stars/Stars_djvu.txt`.
- ["How to Get Over 25,000 Resources by 2450" by Jason Cawley — The Stars! FAQ (starsfaq.com)](http://starsfaq.com/articles/25k_by_2450.htm) — auto-build strategy conventions, the "operable factories" grow-into-your-cap dynamic, and typical production-template phrasing.
- ["Rapid Colony Development" by Michael Meagher — Stars!-R-Us article (starsfaq.com)](http://starsfaq.com/articles/sru/art77.htm) — worked illustration of the mine output/concentration relationship and the homeworld's guaranteed mineral-concentration floor in practice.
- [Chapter 2: Basic Race Design — Stars! Official Strategy Guide (unofficial mirror)](http://www.deepsky.com/~gpeters/stars/www.anrokima.de/Strategie_Handbuch/ssg/ssg02.htm) — corroborates the default factory cost (10 resources), default output (1 resource/factory/year), and default germanium cost (4 kT).
- [Chapter 3: Building a Monster Race — Stars! Official Strategy Guide (unofficial mirror)](http://www.deepsky.com/~gpeters/stars/www.anrokima.de/Strategie_Handbuch/ssg/ssg03.htm) — worked example race-design economic settings, illustrating the shape and rough scale of the factory/mine slider ranges.
- [Chapter 6: Early Resource Management — Stars! Official Strategy Guide (unofficial mirror)](http://www.deepsky.com/~gpeters/stars/www.anrokima.de/Strategie_Handbuch/ssg/ssg06.htm) — the compounding-growth framing of factories building factories, and basic production-queue prioritization advice (factories → mines → terraforming → defenses).
- [Chapter 6: Early Resource Management — Stars!wiki](https://wiki.starsautohost.org/wiki/Chapter_6:Early_Resource_Management) — cited via search-engine snippet only (direct fetch blocked by the site's bot-check); corroborates the population/factory/mineral three-factor framing.
- [Total Terraforming — Stars!wiki](https://wiki.starsautohost.org/wiki/Total_Terraforming) — cited via search-engine snippet only; source for the (unconfirmed against the primary manual) 100-resources-per-1% baseline terraforming cost figure.
