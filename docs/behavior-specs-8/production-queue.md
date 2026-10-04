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

The unmodified default for both is **10 per 10,000 colonists, confirmed directly from the binary this pass**. This was previously only a community convention. Each setting's legal range is **5–25**. Both values come from the race wizard's own per-setting bound table and the default (Humanoid) preset record; see "Race-design economic settings, recovered from the binary" under §4 below. Formally:

```
operable_factories_cap = min(build_cap_factories, max(1, floor(factories_per_10k_setting * population / 10000)))   (default setting = 10)
operable_mines_cap     = min(build_cap_mines,     max(1, floor(mines_per_10k_setting     * population / 10000)))   (default setting = 10)
operating_factories    = min(built_factories, operable_factories_cap)
operating_mines        = min(built_mines,     operable_mines_cap)
```

**Corrected by the segment-24/25 sweep (this pass).** The first version of this formula used `floor(population / 10000) * setting`, which quantizes in whole 10,000-colonist blocks. The code computes the product first and floors afterwards: the routine pair `FUN_1048_4c24` (mines) and `FUN_1048_4e26` (factories) (`stars.exe.export.c:27730`-`27765`, `27850`-`27885`) multiply the race setting (per-race generic field 6 for mines, 3 for factories) by the planet's population stored in units of 100 colonists, divide by 100, never return less than 1, and clamp to a separate, population-independent **build cap** (below). A 25,000-colonist homeworld at the default 10-per-10,000 therefore operates 25 factories, not 20. Alternate Reality races (PRT 8) get 0 from every one of these routines, which is why they can neither build nor operate mines, factories or defenses.

**Build cap (previously undocumented).** The number of mines or factories a planet may *contain* is limited by the planet's **maximum** population, not its current one: `FUN_1048_4bac` (mines) and `FUN_1048_4dae` (factories) (`:27702`-`27726`, `:27822`-`27846`) take the planet's maximum supportable population (`FUN_1048_4a8e`, documented in `population-growth.md`) in units of 100 colonists, multiply by the same per-10,000 setting, divide by 100, and floor the result at **10**. At 100% habitability and the default setting this is 1,000 of each. The in-game planet-info sentence "You have N mines on X. You may build up to M; however, your colonists are currently capable of operating only K of them" (dynamic strings 205-209) prints exactly this pair. Defenses have their own cap (`FUN_1048_4ed0`, `:27889`-`27908`): **four times the planet's habitability value in percent, clamped to 10..100** (0 for Alternate Reality), and an operable count (`FUN_1048_4f18`, `:27914`-`27946`) of one defense per 2,500 colonists (rounded up, at most 1,000, at most the cap). The 100-defense ceiling quoted in §5 is therefore the top of a habitability-scaled range, not a flat limit: any planet at 25% habitability or better can hold 100, while a poorer planet is held to four times its habitability value (never below 10, which is also the cap for planets with zero or negative value).

Factories or mines built beyond the current cap are not destroyed or wasted — they simply sit idle (produce nothing) until population growth raises the cap enough to bring them online. This "grow into your infrastructure" dynamic is explicitly used as a strategy in community guides, e.g. one long-standing strategy article describes deliberately letting a homeworld's population climb until "the factories that pop can operate (858)" before changing its build orders.

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Mines/Factories sidebar)](https://archive.org/details/manual_Stars); ["How to Get Over 25,000 Resources by 2450" by Jason Cawley — starsfaq.com](http://starsfaq.com/articles/25k_by_2450.htm)

### 4. Minerals from mines

Mines extract Ironium, Boranium, and Germanium independently, based on each mineral's own surface concentration on that planet (0–100%, tracked separately per mineral). Mining decreases concentration over time (asymptotically — the manual states a planet never truly runs out, extraction just gets very slow near 1% concentration), and every player's home/starting world is guaranteed never to drop below 30% concentration in any mineral, for the life of the game, regardless of who owns it:

> "You never run out of minerals on a planet, you just decrease the concentration until it reaches 1%..." / "The mineral concentration on your home, or starting world never drops below 30."

Like factory output, "mine output" (kT extracted per mine at 100% concentration) and "mine build cost" (in resources) are race-design settings. A community strategy article illustrating a *customized* race spelled the underlying shape of the formula out concretely: with a mine efficiency of 1.2 kT and 24 mines/10,000 pop, a homeworld sitting at the guaranteed 30% concentration floor was reported to produce "450kt-1000kt of germanium per year." The generalized formula:

```
mineral_output(type) = operating_mines * (mine_output_setting / 10) * (concentration_percent(type) / 100)
```

**The default mine settings are now confirmed from the binary (this pass):** 5 resources to build, a mine-output setting of 10 ("every 10 mines produce up to 10 kT of each mineral every year", i.e. 1 kT per mine per year at the formula's full-concentration scale above), and 10 mines operable per 10,000 colonists. The build-cost figure also matches the live-client observation in Open Questions. Mines, unlike factories, appear to require only resources (no minerals) to build.

#### Race-design economic settings, recovered from the binary

The race wizard stores all seven economic settings as small integers. It validates each one against a per-setting minimum/maximum table and seeds new races from the first built-in preset (Humanoid). Both tables were read directly out of `stars.exe`. For the location, the addressing subtlety that hid them from earlier passes, and the cross-checks, see `race-designer-ui-and-availability.md` "Economic-settings stage".

| Setting | Min | Max | Default |
|---|---|---|---|
| Colonists per 1 resource | 700 | 2,500 | 1,000 |
| Resources produced per 10 operating factories | 5 | 15 | 10 |
| Resources to build one factory | 5 | 25 | 10 |
| Factories operable per 10,000 colonists | 5 | 25 | 10 |
| Factory Germanium cost (checkbox) | 3 kT | 4 kT | 4 kT (box unchecked) |
| kT of each mineral per 10 operating mines | 5 | 25 | 10 |
| Resources to build one mine | **2** | 15 | 5 |
| Mines operable per 10,000 colonists | 5 | 25 | 10 |

The colonists-per-resource setting is stored as 7–25 and displayed with a "00" suffix, hence 700–2,500. Every range matches the manual's "favorable extreme" figures quoted in §2/§3 (factories costing 5, 10 factories producing 15, 25 operable per 10,000). The 2-resource mine-cost floor is lower than some community write-ups state, but the point-total formula has a dedicated branch for exactly that one sub-3 value (`race-traits.md` §1a), which only makes sense if 2 is legal. Alternate Reality races cannot change the six factory/mine settings: the wizard greys them out, and selecting AR resets them to exactly the defaults above (`stars.exe.export.c` lines 93759-93766).

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Mines)](https://archive.org/details/manual_Stars); ["Rapid Colony Development" by Michael Meagher — starsfaq.com](http://starsfaq.com/articles/sru/art77.htm)

### 5. Defenses

Planetary defenses (SDI, Missile Battery, Laser Battery, Planetary Defense, Neutron Battery, etc.) partially protect a planet against bombing, mass-driver mineral packets, and invasion. Adding a "Defenses" item to the queue increases the count of whichever defense type the planet is currently using; upgrading to a better defense *type* happens automatically and for free the instant the relevant technology is researched — it never needs to be queued.

> "Adding defenses increases the number of existing defenses of the type you're currently employing... Upgrading defenses happens automatically. Whenever you learn new technology that applies to defense, all defenses on all your planets upgrade automatically and at no cost."

The manual frames the operable limit on defenses the same way as factories/mines — "you can build as many defenses as you wish, you can only operate as many as your population has resources to handle" — but community sources are consistent that there is also a hard, population-independent ceiling of 100 defenses per planet (the manual's own example screenshot shows a fully-built planet at "100 of 100"). The resource cost to build one defense unit was found cited in a community wiki search snippet as approximately 15 resources, but this could not be corroborated against the primary manual text or against a mineral-cost figure, so it is listed under Open Questions rather than presented as confirmed.

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Building Planetary Defenses)](https://archive.org/details/manual_Stars)

**Cost, corrected by the segment-24/27 completion-effect sweep (this pass) — the "25 / 44 / 48" figures reported by an earlier pass were never defense costs.** An earlier version of this section read the production cost calculator `FUN_10d0_221a` as pricing Defenses at 25, 44 or 48 resources (Packet Physics / every other trait / Interstellar Traveler) and Terraforming at 70, 110 or 120. Both readings were wrong. Those two case groups — item types **6 and 0x11**, and item types **0xe, 0xf and 0x10** — are the **Mineral Packet** items; the build-completion routine `FUN_10b8_0e68` creates mass packets for exactly those item types, the queue-caption table names them "Mineral Packets", "Mixed Mineral Packet" and the three single-mineral packets, and the numbers are kilotons of minerals, not resources (§10 has the full catalog, and §10's packet subsection has the real values). The earlier pass had no way to see this because it read the cost side of the switch alone.

The real Defenses branch is the case group for item types **2** (auto-build) and **9** (manual) (`stars.exe.export.c:87812`-`87838`). It resolves component category `0x8000` subtype 9 through the shared component resolver and copies that component's own cost record: subtype 9 is **SDI**, and `extracted-game-data/component-stats.tsv` gives it **15 resources plus 5 kT each of Ironium, Boranium and Germanium**. The cost is fixed at the SDI price whatever defense technology the race has learned, which is exactly the manual's "upgrading happens automatically and at no cost": the completion effect (§10) only increments a count, and the best owned defense technology is applied elsewhere. Two consequences:

- The community figure of roughly 15 resources per defense unit (the one this document long carried as "lower-confidence") is **correct**; it is the binary-derived 44 that was wrong. The mineral part (5 kT of each) is new information.
- **Inner Strength (PRT 4) has its "planetary defenses cost 40% less" advantage implemented right here.** When the owner's PRT is 4, all four cost fields are multiplied by 3 and integer-divided by 5 (`:87826`-`87837`), so an Inner Strength defense costs **9 resources plus 3 kT of each mineral**. This closes the earlier question of whether that trait was implemented in the cost calculator at all. Alternate Reality races cannot build defenses (§3).

The earlier reading also produced a speculative "Packet Physics / Interstellar Traveler are treated as one logistics category" remark, supported by the new-game second-planet gate in `race-traits.md` §2a. That grouping is still real (both traits are singled out by the packet code, and by the second-planet gate), but it has nothing to do with Defenses or Terraforming and the remark is withdrawn.

### 6. Terraforming

Terraforming nudges one habitability factor (Gravity, Temperature, or Radiation) by 1% per completed "Terraform Environment" unit, automatically choosing whichever factor is furthest out of the race's preferred range:

> "Each 1% Terraforming task executed will modify one of the environmental factors by 1%, which will improve the overall habitability value by at least 1% and probably more."

Two auto-build variants exist: **Min(imum) Terraforming**, which only fixes factors currently *outside* habitable range (and which the manual singles out as urgent — colonists start dying if a negative-value planet is left un-terraformed for more than a year) and stops once the planet value reaches zero; and **Max(imum) Terraforming**, which keeps improving already-positive factors up to a player-specified percentage ceiling and the limits of researched terraforming technology. Total possible terraforming per factor is capped by tech level, normally maxing out at 15% (potentially higher — see the race trait below).

Community wiki sources (not independently confirmed against the primary manual text in this pass) cite a terraforming cost of 100 resources per 1% terraformed for a standard race. The **Total Terraforming** race trait is explicitly documented in the manual as reducing this cost:

> "Total Terraforming... Terraforming requires 30% less resources and you can research terraforming technologies that improve factors up to 30% instead of just 15% normally."

Source: [STARS! Player's Guide, Ch. 6 "Planets" (Terraforming) and Race Traits appendix](https://archive.org/details/manual_Stars); [Total Terraforming — Stars!wiki (via search index)](https://wiki.starsautohost.org/wiki/Total_Terraforming)

**Cost and identity, corrected by the completion-effect sweep (this pass) — the "70 / 110 / 120" figures were mineral-packet kilotonnages, and queue-item types 4, 5 and 12 are the three Terraforming items.** The actual Terraforming branch of the cost calculator `FUN_10d0_221a` is the case group for item types **4, 5 and 0xc (12)** (`stars.exe.export.c:87850`-`87867`): **100 resources per 1% step, or 70 with the Total Terraforming trait (per-race boolean selector 1), no minerals, and halved for a Claim Adjuster (PRT 3)** by a one-bit right shift. This restores the "100 resources per 1%" community figure and shows Total Terraforming's "30% less" is exactly 100 to 70. The Packet-Physics-cheap / Interstellar-Traveler-pricey pattern seen in the old 70/110/120 reading belongs to mineral packets (see the Defenses note above and §10), not to terraforming.

Identification of the three items rests on four independent lines of evidence, which agree:

- **Captions.** The production-catalog captions are dynamic strings 126-143, indexed by item type (`126 + type`): 4 is "Min Terraform" (130), 5 is "Max Terraform" (131) and 12 is "Terraform Environment" (138). The production-template display `FUN_10d0_345e` (`:88529`-`88615`) builds its captions by exactly this formula from the six-bit type stored in each template entry, so the indexing is the real numbering of the type field and not a coincidence.
- **Completion effect.** In the build-completion routine `FUN_10b8_0e68` (`:77255`-`77280`), types 4, 5 and 12 share one branch that repeatedly asks the terraform-step chooser `FUN_1048_3eee` which environment axis and direction to move, changes that planet environment byte by exactly one point (clamped to 1..99), and posts message 123 ("Your terraforming efforts on \p have improved/worsened the \e to \E") once per step.
- **Purchase gating.** `FUN_10b8_0756` (`:76707`-`76720`) maps both auto-build types 4 and 5 to the manual type 12, clamps the number of steps to the planet's remaining terraform headroom (`FUN_1048_537e`), and, for type 4 only, allows nothing once the planet's habitability value for its owner is positive — so **Min Terraform stops as soon as the planet is habitable, while Max Terraform keeps going to the technology limit**, exactly as the manual describes. `FUN_10b8_0000` gives type 12 the same headroom clamp with message 303 ("orders to terraform beyond the maximum allowed. The orders have been reduced").
- **Helper predicate.** The small routine `FUN_1048_5336` (`:28110`-`28125`) answers "is this queue record a Terraforming order" for exactly types 12, 4 and 5 (it has no caller in the export, but its content is unambiguous).

The terraform-axis-selection behavior (§6 above, "automatically choosing whichever factor is furthest out of range") **is independently confirmed**: the stepper evaluates all three axes and mutates only the single axis and direction giving the largest habitability gain.

Claim Adjuster races can still queue these items, at half price, but the default-template application that runs on every new or captured colony deliberately leaves both terraform items out of a Claim Adjuster's queue (§10 below), because that trait terraforms planets for free each turn (`turn-generation-engine.md` §11).

**Also confirmed by inspection:** Factories' Germanium build cost follows exactly `4 - traitFlag`, i.e. 4 kT normally or 3 kT with the discount trait — an exact match to this document's §2 claim, independently verified at the code level. (The selector behind the "one kT less" flag is per-race generic bit `0x1f`, i.e. the race-wizard checkbox rather than a Lesser Racial Trait. The same case group has a second form, taken when bit 8 of the global option byte `DAT_1128_078b` is set: all three minerals then cost `2 - flag` each instead of only Germanium costing `4 - flag`. That bit is tested together with "current player is player 0" in the tutorial hooks throughout the client, so it reads as a tutorial-mode flag; the flag's label was not otherwise confirmed. Factory and Mine resource costs are the race's own generic fields 2 and 5 — defaults 10 and 5 — and Mines cost no minerals. `stars.exe.export.c:87772`-`87811`.)

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
- **A planetary disaster (e.g., a comet strike) resets the queue**, losing all in-progress work and its invested resources (though disasters can also deposit windfall minerals usable immediately). **Refinement from the segment-24 sweep:** the shared cleanup routine `FUN_10b8_371a` (`stars.exe.export.c:78756`-`78805`), called at the end of both the comet strike (`FUN_10b8_33ee`) and the environment-shift event (`FUN_10b8_37c8`), keeps every **auto-build entry** (item types 0-6) in its original order and deletes every other entry — manual mines/factories/defenses, terraforming, packets, scanners, Genesis Devices and all ship and starbase orders. If nothing survives, the whole queue is freed. The in-game text for the environment shift (message 253) says "this change has canceled all planetary production".

Source: [STARS! Player's Guide, Ch. 7 "Production" (How Production Works, Clearing/Unblocking the Production Queue, Adding Auto-Build Items)](https://archive.org/details/manual_Stars)

**The per-turn, per-planet queue-application loop, and the narrower open question it resolves, confirmed by inspection of the exported client.** The actual turn-generation code that spends a planet's resources/minerals against its queue lives in `FUN_10b8_0000` (segment 24), which walks the planet's production-queue entries **top to bottom in a single pass per planet per turn**, calling a per-item apply routine (`FUN_10b8_0756`) once for the item currently at the head of the queue. That routine buys as many whole units of the item as the turn's remaining resource/mineral budget allows (the same whole-unit-granularity mechanism already empirically confirmed above), then reports one of several completion-status codes back to the caller. The caller's handling of that status is the mechanism that decides whether the walk continues to the next queue entry or stops for the turn: a status meaning "this item is now fully satisfied" (fully built out, or — for an auto-build entry — its target reached or it made whatever partial progress it could without literally running out of resources to spend) sends the loop straight on to the next item in the same call; only a status meaning "this ordinary item still needs more of a mineral it doesn't have, and there was nothing else useful this iteration could do about it" causes the loop to `break` and stop funding the rest of that planet's queue for the year.

**This resolves the open item below: yes, an auto-build item positioned behind a manual item is funded the same turn the manual item stops blocking.** Because the whole queue is processed in one function call per planet per turn, "the blocking item completes/no-longer-blocks" and "the next item gets its turn" are not separated by a turn boundary — they are two steps of the same loop iteration sequence. Concretely: if the manual item at the head of the queue receives enough resources and minerals during this turn's own pass to either finish outright or otherwise stop reporting a blocking status, the walk immediately proceeds to evaluate the auto-build entry behind it, in that same call, rather than waiting for a subsequent turn. Conversely, if the manual item still can't fully clear that status after being given its share of the turn's budget (e.g. it remains short on a mineral even after any partial whole-unit progress), the loop breaks immediately and the auto-build entry behind it receives *nothing* that turn — directly confirming this document's existing "downstream items behind a blocked ordinary item do not receive that year's leftover resources" claim, and pinning down exactly why: it is this unconditional `break`, not a scheduling rule evaluated in advance. The exact mapping from each numeric status code to its English meaning (e.g. distinguishing "auto-build target already met" from "auto-build made partial progress" from "ordinary item fully completed") was reconstructed from the surrounding control flow rather than from any string/label evidence, so the specific codes are not asserted individually — only the binary continue-vs-break behavior this document relies on, which is unambiguous from the control flow itself. *(This paragraph's reluctance is superseded: the next paragraph names all eight codes from the per-item routine's own assignments.)*

**The eight status codes, named (segment-24 sweep, this pass).** `FUN_10b8_0756` (`stars.exe.export.c:76634`-`76962`) stores its result in the caller's status word at `:76912`-`76944`; the caller (`FUN_10b8_0000`, `:76549`-`76592`) acts on it as follows. Codes 0-4 mean "move on"; codes 5-7 mean "stop the walk for this planet this turn".

| Code | Meaning | Caller action |
|---|---|---|
| 0 | An ordinary (manual) entry had its whole remaining quantity bought this turn. | Entry removed; the same slot is examined again (the next entry slides into it). |
| 1 | An auto-build entry bought at least one unit and reached its allowed quantity (N, or the room left under the operable cap if smaller). | Continue to the next entry. |
| 2 | An auto-build entry had nothing to buy (no room left under the operable cap, or no accelerator/target for packets). | Continue. |
| 3 | An auto-build entry bought some units but was stopped by a **mineral** shortfall with no Mineral Alchemy entry in front of it. | Continue; suppresses the "queue is empty" message (below). |
| 4 | Same as 3 but nothing was bought. | Continue; suppresses the "queue is empty" message. |
| 5 | An entry bought at least one whole unit and still has quantity left (ran out of resources, or, for a manual item, of a mineral). | **Stop.** |
| 6 | No whole unit was bought, but the head unit's percent-complete advanced. | **Stop.** |
| 7 | No progress at all. | **Stop.** |

This settles the apparent tension between the manual ("auto-build items never block") and the empirical partial-payment test: an auto-build entry that runs short of a **mineral** returns 3 or 4 and the walk simply continues, exactly as Example 2 below assumes; an auto-build entry that runs short of **resources** returns 5-7 and ends the walk, but that is moot because nothing was left to spend; and an ordinary entry short of a mineral first spends every cost component in proportion to the completion percentage its scarcest ingredient allows (this is the "Factory 97 / 9% Done" observation), and then returns 5-7 and blocks everything behind it. When an auto-build entry stops with codes 5-7 its partial progress is peeled off into a brand-new one-unit **manual** entry (the manual equivalent of the auto type — Mine, Factory, Defenses, Mineral Alchemy, Terraform Environment or Mixed Mineral Packet) inserted at the very top of the queue (`:76569`-`76588`), so the next turn resumes that one unit before anything else; the auto entry itself is never edited.

**Two queue messages, with their real conditions.** Message 63 ("The production queue on \p is empty.") is posted each turn for an owned planet with no queue at all (`:76361`). Message 62 ("\p has completed its orders. The production queue is empty.") is posted (`:76600`-`76604`) when the queue has been emptied *or* the walk reached the end of the list with no entry having returned code 3 or 4. Consequences worth reproducing: a queue consisting only of auto-build entries that all report code 1 or 2 posts message 62 every turn (for mines, factories and defenses that means every entry either bought its full quantity or found no room under the operable cap), and a queue whose auto-build entries are held back by minerals stays silent. Messages 175-180 ("built as many mines/factories/defenses as the current population can operate — auto building temporarily blocked", and the "as the planet can support — stopped" variants) are treated by the message-click handler as production-queue messages (`:14265`, together with 62 and 63, so clicking one opens the queue), but **this build never posts them**: the next paragraph records the whole-executable evidence for that conclusion. The auto-build clamps that would have justified them produce no message here.

**Messages 175-180: defined, routed, never emitted (settled by a whole-executable check).** *What they mean.* In `extracted-game-data/message-strings.txt` order the six strings are: 175 mines and 176 mines, 177 factories and 178 factories, 179 defenses and 180 defenses, each pair being "has built as many as the current population can operate — auto building is temporarily blocked" followed by "has built as many as the planet can support — auto building is stopped". The identifier order follows the three auto-build entry types (0 mines, 1 factories, 2 defenses, as in the item-type catalog of §10), so the natural mapping is `175 + 2 x autoType + (0 for the operable-count limit, 1 for the build cap)`; that mapping is inferred from the string order, not observed in code. The two limits are the ones documented in §3 and §5: the *operable* count that the planet's current colonists can run (`FUN_1048_4c24`, `4e26`, `4f18`) and the *build cap* set by the planet's maximum population (`FUN_1048_4bac`, `4dae`, `4ed0`). The event each message reports is therefore "an auto-build entry had nothing to buy because its building type had reached one of those two limits".

*Where an emitter would have to sit, and what is there instead.* The only place the auto-build limits are applied is the auto-build branch at the top of `FUN_10b8_0756` (`:76677`-`76733`): for auto types 0-2 it computes `allowed = limit - buildings already on the planet` (operable-count routine with next year's projected population, as §10a says, `:76685`-`76725`; that routine already clamps its result to the build cap, so at the cap the two limits coincide and the code would have to compare the result with the cap to choose between the "blocked" and "stopped" strings), and if that is smaller than the queued quantity it silently overwrites the quantity with the allowed figure, floored at zero (`:76726`-`76733`). A zero result flows on as status code 2 of the table above. No message call exists on that path; the only message the routine posts is the Mineral Alchemy report 140 (`:76909`-`76911`). The caller (`FUN_10b8_0000`, `:76545`-`76592`) reads the status word only to choose between "advance", "re-examine this slot" and "stop the walk" (`:76549`, `:76566`-`76567`); none of the eight codes is ever added to a base to form a message identifier. The completion routine `FUN_10b8_0e68` posts only literal identifiers, 53-58 ("built N factories/mines/defense outposts"), where the singular/plural choice is made by asking the message store whether a message of that type was already posted for the planet this turn (`FUN_1030_821a`, `:77238`-`77246`), not by a computed base.

*Negative evidence, so the answer does not rest on one grep.* (1) **Computed type codes.** The server-side appenders `FUN_1030_7444`/`FUN_1030_7460` have 147 call sites in the export (`FUN_1030_74ba`, the prepend variant, has 4 and the client-side variable-argument appender `FUN_1030_764a` has 4, matching the "155 call sites" count in `client-ui-dialog-catalog.md`). Every argument was read. The type is a literal or a small fixed-set selection at every site (for example 0x35-0x3a and 0x30-0x32 built/routed, 0x42-0x49 cargo reports, 0x83-0x8a comet, 0xd6/0xd7, 0x127/0x128, 0x12c/0x12d/0x15a/0x15b, 0x166-0x174, 0x17a-0x17d, 0x8d/0x8e/0x144, 0xa4/0xa8, 0x115/0x116/0x9f); no literal or computed set contains a value from 0xaf to 0xb4 (175-180). The neighbouring blocks that *are* emitted confirm the gap is real: 170-174 ("new planet found", including the terraforming variant) are posted client-side while a planet record is decoded (`FUN_1070_1fee`, `:45654`-`45692`), and 181-184 (victory, defeat) and 187/188 (elimination) are posted by the victory routine through the prepend appender (`:79303`-`79316`, `:79327`, `:79333`). (2) **Unmaterialised code.** All 28 functions of segment 24 tile the segment exactly (checked in the raw executable: each function ends with a far return and the next begins at the following byte, total length `0x539e`), so there is no hidden region as there was for the Auto Generate Options callback (`turn-generation-engine.md` §8). (3) **Raw operand scan.** Searching every code segment of the executable for the 16-bit values 0xAF-0xB4 as push, move, add or compare operands finds only dynamic-string identifiers for menu text, menu command identifiers, jump displacements and an unrelated starting-population constant (`:50960`); none is passed to a message routine. (4) **The one reference.** The message-click handler (`:14265`) is the only code that names the range; it treats 62, 63 and 175-180 as "production queue" messages, which shows the message types were designed and routed but nothing in this build produces the auto-build ones.

*Consequence for a re-implementation.* Reproducing this build means posting nothing when an auto-build entry is clamped or skipped, and treating the queue-empty messages 62/63 as the only auto-build-related notices. An implementation that wants the strings used can post 175/177/179 when the operable-count limit is the binding one and 176/178/180 when the build cap is, at the point marked above, but that would be an addition, not observed behaviour.

**Confirmed by inspection of the exported client.** The 100-year "practically never" threshold is not a display heuristic layered on top of some other estimate — the completion-time estimator itself literally **simulates up to 100 yearly iterations** of projected resource/mineral income against the queue, and if the target item has not finished by iteration 100, its finish-time is forced to exactly 100 rather than being computed further. This is an exact match for this document's claim, down to the specific number.

**Also confirmed:** the exact packed queue-record format — quantity is stored in a 10-bit field (hard cap **1,023** units per line), alongside an item-type index and a small sub-flag; a build-order is refused outright once its total queued-item count exceeds **200** entries, a previously undocumented structural cap distinct from the per-line quantity cap.

**Full queue-item text-color scheme, newly identified and confirmed by inspection of the exported client, including the exact RGB values.** The queue listbox's populator prepends one invisible marker character to each item's display text, and the listbox's own owner-draw routine switches on that same character to pick the item's text color — the two routines share this single-character code entirely independently of any font/highlight state. Reading the estimator's two outputs (the simulated iteration at which the item first starts receiving resources, and the one at which it finishes — both forced to 100 if not reached within the 100-year simulation ceiling described above) as (start, finish):

- **(start, finish) = (1, 1)** — the item will both begin and fully complete production in the very next turn — is marked with the color **dark green** (RGB 0,127,0). This is the direct code-level confirmation of the "green if it'll finish in 1 turn" rule.
- **start = 0, or start = 1 with finish > 1** (i.e. the item is already receiving resources this turn but will need further turns) is marked **dark blue** (RGB 0,0,127).
- **start ≥ 100** (never begins within the simulated window) — or any other value not matched by a more specific case — falls back to plain **red** (RGB 255,0,0). This is the same "practically never" case already documented above, now with its exact color confirmed; it also fires for any completion state the code doesn't otherwise recognize.
- **start = finish = 0, or start = finish = −1** (a zero-remaining-cost or not-applicable item — e.g. one already fully paid for) is marked **gray** (RGB 127,127,127) — closer to a dark gray than true black, though it would likely read as "black" against the listbox's normal white-ish item text at a glance.
- Every other case (start in the 2-99 range, i.e. "will start within the simulated window, but not right away") gets no special marker and is left in the listbox's ordinary default text color — the "black" (or, in a specific alternate listbox state, white) baseline the other four colors stand out against.

The exact English meaning of the blue/gray cases beyond the mechanical trigger above (e.g. whether "blue" specifically reads to a player as "in progress" and "gray" as "nothing to report") was not independently confirmed against the manual or live play in this pass — only the color values, the marker characters, and their precise (start, finish) trigger conditions are confirmed from the decompiled code itself.

### 9. Auto-build vs. manual queue items and production templates

Manually-added items are one-shot orders (optionally multiplied via Shift/Ctrl-modified Add for batches of 10/100/max) that are removed from the queue once built. Auto-build items ("Mines/Factories/Defenses/Mineral Alchemy/Terraforming (Auto Build)") are persistent standing orders phrased as "up to N": they stay in the queue indefinitely and must be removed manually. **Correction from live testing (10h):** for Mines, Factories and Defenses the figure after "up to" is *not* a total the planet is topped up to (as the manual's wording and the default template's "Factories up to 10 ... up to 25" suggest); it is the number of units the entry may buy *each year*, still clamped by the operable-count room and the year's resources and minerals. A **production template** is a saved, reusable sequence of auto-build items (plus the leftover-resources-to-research setting) that can be applied to any planet's queue in one action; the **default template** auto-applies to every newly founded or captured colony. The manual's own illustrative default template:

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

**Confirmed and extended by inspection of the exported client.** The literal display strings **"(Auto Build)"** and **"up to N"** (for a persistent standing order) are both confirmed to appear verbatim in the game's own listbox-population code, in two independent functions — a strong, direct confirmation of this document's naming convention for auto-build items, down to the exact wording. The manual-add stepper's Shift/Ctrl-modified batch amounts are also confirmed exactly: **no modifier = 1, Shift = 10, Ctrl = 100, Ctrl+Shift = effectively max** (the code clamps the Ctrl+Shift batch to 1,020, one below the queue-line's hard 1,023-unit cap, rather than exactly 1,000).

**Correction — the production-template manager's identity.** The saved-template screen is a **4-slot** manager (not more, not a variable count): each of the 4 slots holds up to **12** auto-build entries plus a single stored flag bit corresponding to the "contribute only leftover resources to research" checkbox described above — confirming this document's claim about what a template stores, down to that exact settings-bit being saved alongside the item list. The *quick, per-planet* production-queue editor (a separate screen from the 4-slot template manager) is where ordinary auto-build/manual queue editing actually happens; these are two distinct screens sharing underlying data, not one screen serving both roles.

**New mechanic, not in this document previously — Mineral Alchemy's automatic counterpart for Alternate Reality.** Distinct from the manually-queued Mineral Alchemy item described in §7, races with the Alternate Reality primary trait receive an **automatic, non-queued** per-turn conversion of leftover resources into minerals at a race-specific ratio, processed during turn generation rather than through the production queue at all. See `turn-generation-engine.md` §6.

**Segment attribution, added by a later pass.** Every UI-facing mechanic in §8-9 above that was previously confirmed "by inspection of the exported client" without naming a code location lives in code-coverage segment 27 (Ghidra `FUN_10D0_*`, plus one specially-named hidden dialog procedure): the 100-iteration completion simulator, the packed queue-record format, the manual-add Shift/Ctrl quantity stepper and its Move-Up/Move-Down commands, the "(Auto Build)"/"up to N" listbox strings, and the 4-slot template manager (`ZIPPRODDLG`, this segment's one specially-named procedure, following the same hidden-dialog-procedure pattern already seen in segments 17/28/29/30). See `research-tech-tree.md`'s new segment-27 note for the itemized write-up, including two further previously-undocumented details found there: an "Upgrade"/"Downgrade" caption suffix on starbase design entries, and a custom-painted itemized cost-breakdown panel distinct from the single combined cost figure this document otherwise describes.

### 10. Item-type catalog, costs and completion effects (segment-24 sweep, added this pass)

This section records the full switch behind every production item, read from the three routines that together define one: the cost calculator `FUN_10d0_221a` (segment 27, `stars.exe.export.c:87531`-`87982`), the per-item purchase routine `FUN_10b8_0756` (segment 24, `:76634`-`76962`) and the build-completion routine `FUN_10b8_0e68` (segment 24, `:76969`-`77485`). Earlier passes read the cost side and the effect side separately and mis-paired them (the Planet Rebirth episode, and the wrong Defenses/Terraforming costs corrected in §5-§6); the table below is cross-checked on all three sides plus the caption table.

**Queue-record layout (32 bits per entry).** Bits 0-9 are the quantity (1-1,023). Bits 10-16 are the item type. Bits 17-19 are the category: **1 = planetary catalog item** (everything in the table below), **2 = ship or starbase design**, in which case the item-type field holds the design slot (0-15 = ship designs, 16-25 = starbase designs; the design records are 147 bytes each). Bits 20-26 are the **percent complete of the unit currently being built** (0-100): the amount already paid toward each of the four cost components (three minerals and resources) is that percentage of the unit's cost, which is why a partially bought unit displays as "9% Done". The remaining top bits are unused. The 1,023 cap, the 200-entry cap and the Ctrl+Shift figure of 1,020 quoted in §8-§9 all refer to this layout.

**Auto-build to manual pairing.** Every auto-build type is converted, at purchase time, to a manual "one unit" type whose leftover progress it can hold: Mines (0) to Mine (8), Factories (1) to Factory (7), Defenses (2) to Defenses (9), Alchemy (3) to Mineral Alchemy (11), Min Terraform (4) and Max Terraform (5) to Terraform Environment (12), Mineral Packets (6) to Mixed Mineral Packet (17) (`FUN_10b8_0756`, `:76685`-`76725`). Costs are shared within each pair.

| Type | Caption (dynamic string) | Kind | Unit cost (`FUN_10d0_221a`) | Effect when a unit completes (`FUN_10b8_0e68`, unless noted) |
|---|---|---|---|---|
| 0 / 8 | Mines (126) / Mine (134) | auto / manual | Resources = the race's mine-cost field (default 5); no minerals | Adds mines: `min(units bought, build cap - built)` to the 12-bit mine count. Message 55 (one) or 56 (several): "You have built a mine / \i mines on \p" |
| 1 / 7 | Factories (127) / Factory (133) | auto / manual | Resources = factory-cost field (default 10); Germanium 4, or 3 with the "one kT less" option (§6) | Adds factories to the 12-bit factory count. Messages 53 / 54 |
| 2 / 9 | Defenses (128) / Defenses (135) | auto / manual | SDI component cost: 15 resources + 5 / 5 / 5 kT; Inner Strength x3/5 (§5) | Adds defenses to the 12-bit defense count (bits 0-11 of the word at planet byte 0x18). Messages 57 / 58 |
| 3 / 11 | Alchemy (129) / Mineral Alchemy (137) | auto / manual | 100 resources, or 25 with the Mineral Alchemy trait (selector 6); no minerals | Nothing in the completion routine. Each unit bought inside `FUN_10b8_0756` adds 1 kT of each mineral to the stockpile (`:76896`-`76908`); message 140 ("transmuted common materials into \ikT each") is gated on the transient "turn being generated" global bit that `population-growth.md` describes for the same flag |
| 4 / 5 / 12 | Min Terraform (130) / Max Terraform (131) / Terraform Environment (138) | auto / auto / manual | 100 resources, 70 with Total Terraforming, halved for Claim Adjuster; no minerals (§6) | One environment step per unit; message 123 per step |
| 6 / 17 | Mineral Packets (132) / Mixed Mineral Packet (143) | auto / manual | 44 kT of each mineral + 10 resources; Packet Physics 25 kT + 5 resources; Interstellar Traveler 48 kT + 10 resources | Creates or enlarges a mass packet (§10b) |
| 14 / 15 / 16 | Ironium / Boranium / Germanium Mineral Packet (140-142) | manual | 110 kT of that one mineral + 10 resources; Packet Physics 70 kT + 5; Interstellar Traveler 120 kT + 10 | Same, one mineral (§10b) |
| 13 | Genesis Device (139) | manual | Component-cost lookup, category `0x8000` subtype 14: 5,000 resources, no minerals | Planet reset (§10c); message 283 to every player |
| 10 | (blank, 136) | unused | no cost arm | no arm; a record with this type does nothing |
| 18-26 | component names, category `0x8000` subtypes 0-8 | manual | component-cost lookup: Viewer 50, Viewer 90, Scoper 150, 220, 280, Snooper 320X, 400X, 500X, 620X; base 100 resources + 10 / 10 / 70 kT | Installs that planetary scanner (§10d) |
| 27 | "Planetary Scanner" (1307), presumably | manual | priced as subtype 0 (Viewer 50) | Installs the **best scanner the owner has** (§10d) |
| any other | | | none | `FUN_10b8_0e68` returns failure |

Mineral packets, the Genesis Device and scanners are all one-off **manual** items: there is no auto-build type for Genesis or scanners, and the only auto-build packet type is the mixed one.

**10a. Quantity clamps applied before purchase (`FUN_10b8_0000`, `:76463`-`76541`, and `FUN_10b8_0756`, `:76685`-`76733`).** For manual Factory, Mine and Defenses orders the entry quantity is cut to `build cap - built` (§3) with message 298 ("orders to build planetary installations beyond the maximum allowed. The orders have been reduced"), and the entry is deleted if the room is zero or negative. For Terraform Environment the cut is to the remaining terraform headroom with message 303. For auto-build Mines, Factories and Defenses the clamp is instead to `operable count (using next year's projected population) - built`, with no message; auto Alchemy always buys "as many as possible" (its quantity is forced to 1,000); auto Mineral Packets buy nothing unless the planet has a mass accelerator and a target. Manual packet orders with no accelerator or no target are deleted with message 297. Scanner orders on a planet that already has a scanner are deleted with message 185. **Resolved by live test (see 10h): the "up to N" figure on auto-build Mines, Factories and Defenses is a per-turn maximum quantity, not a total target.** The entry buys at most `min(N, operable count - built)` units each turn (subject to resources and minerals); the built count is never subtracted from N.

**10b. Mineral packets (types 6, 14, 15, 16, 17).** Requirements: a starbase on the planet whose design carries a mass driver (component category `0x0200`, subtypes 7-15 = Mass Driver 5 up to Ultra Driver 13; `FUN_1048_5138`, `:28009`-`28056`) and a destination set on the planet. The launch rating is the best driver's warp (subtype minus 2, i.e. warp 5-13), plus one if that best rating appears in two different starbase slots. Without an accelerator the packet "disintegrates" (message 209); with an accelerator but no target, message 210 (the Set Dest button hint). The delivered cargo is smaller than the cost: a mixed packet carries **40 kT of each mineral (Packet Physics 25)** for a 44 kT-each price, and a single-mineral packet carries **100 kT (Packet Physics 70)** for 110 kT, so ordinary races lose 10% in the build, Interstellar Travelers 20% (48 and 120) and Packet Physics races nothing. The packet's speed is the planet's chosen packet speed if it lies in 5..(best driver warp + 3), otherwise the launch rating; how far the speed exceeds the driver rating (0-3, one more for Interstellar Traveler, capped at 3) is stored as the packet's *overspeed class*. A new order merges into an existing packet of the same owner leaving the same planet with the same speed, target and class provided its total mass stays under 16,300 kT (message 212), otherwise a new packet object is created (message 211, or message 297 if none can be allocated); each mineral in a packet is capped at 32,760 kT. Decay in flight (`FUN_10b8_4200`, `:79447`-`79505`, called from `FUN_10b8_433a`): packets at overspeed class 0 do not decay at all; classes 1, 2, 3 lose 10%, 25%, 50% of each mineral per year, with a minimum loss of 10 kT per mineral (Packet Physics: half the percentage and a 5 kT minimum); a packet with nothing left is destroyed. This refines `turn-generation-engine.md` §3, which had the percentages but not the class-0 exemption or the minimum.

**10c. Genesis Device (type 13) in full.** The effect (`:77424`-`77459`) is broader than "Planet Rebirth" was first described: it broadcasts message 283 to **every** player, then (except for Alternate Reality owners) zeroes the mine and factory counts (planet bytes 0x15-0x17), zeroes the defense count (the low 12 bits of the word at byte 0x18) and sets the planetary-scanner field to its "none" value (0x1f), then for every owner it zeroes the three mineral stockpiles, re-rolls the three **mineral concentrations** (each 25 + two independent draws of 0-39, so 25-103) and re-rolls the **three environment values, both current and original** (each 1 + two independent draws of 0-49, so 1-99). Population, ownership and any starbase are untouched. If several Genesis Devices complete in one turn the effect runs once (the completion routine is called once with the unit count). The order is otherwise an ordinary manual item bought like any other (whole-unit purchase with partial progress); there is no quantity special case, only the fact that the effect ignores the unit count.

**10d. Scanners (types 18-27).** The planet's scanner is a five-bit field (bits 12-16 of the dword at byte 0x18; 0x1f means none) holding the component subtype 0-8. Completion stores the subtype and posts message 124 ("\p has built a new \k planetary scanner"). Type 27 runs the "best available scanner" search (`FUN_1008_58de`, `:3643`-`3656`) from subtype 8 downward and installs the first one the owner may build. Because the field is a single value, building a scanner never stacks; a later research breakthrough that unlocks a better scanner upgrades every installed scanner automatically (message 343, produced by the research code).

**10e. Starbases and ships (category 2).** A starbase design completing (design slot 16-25) installs or replaces the planet's starbase: it is refused silently if the design record is flagged obsolete/deleted or invalid, otherwise message 205 (no docking capacity), 206 (docking limited to \ikT hull weight) or 207 (any size) is posted. Replacing a starbase with a design of a lower hull-type index runs the downgrade handler (`FUN_10c8_49d0`); if the planet had no mass driver before, the packet target and speed are cleared when the new base has none, or initialised to the new driver's rating when it has one; if it already had one they are left alone. The new design's "built" and "existing" counters each go up by one and the replaced design's "existing" counter goes down by one. A ship design (slot 0-15) requires a starbase on the planet; if the design is obsolete or invalid the order fails with message 79 ("someone lost the plans"). A new fleet is created and, if the starbase has a rally destination, a second waypoint is set with the highest warp the fuel allows, giving messages 49/50 (routed) or 51/52 (not routed for lack of fuel); without a rally point messages 47/48. **Hard limit: 512 fleets per race.** When a race already owns 512 fleets the new ships are merged into a fleet of the same design already sitting at the same planet if one has room (each design stack holds at most 32,766 ships; message 313), otherwise they are lost (message 186). The resources and minerals are spent before this check, and an order whose completion routine reports failure is zeroed, not refunded.

**10f. Default-template application (new and captured colonies).** When a colonisation or invasion succeeds (`FUN_10b8_1ea6`, template copy at `:77899`-`77943`; see `turn-generation-engine.md` §11), the new owner's stored default production template is copied into the planet's queue (up to 12 entries, each a six-bit type and ten-bit quantity, all category 1) and the "contribute only leftover resources to research" flag is copied from the template. Two race-specific exclusions apply at copy time: **Alternate Reality races skip types 0, 1 and 2** (Mines, Factories, Defenses) and **Claim Adjuster races skip types 4 and 5** (Min and Max Terraform).

**10g. Hard limits and gates, consolidated.**

- Quantity per line 1,023; queue length 200 (found in the AI's insertion routine; the human UI's limit was not separately confirmed); template 4 slots x 12 entries; head-unit progress 0-100%.
- Mines and factories per planet: build cap `max(10, per-10k setting x max population / 100)`; defenses: `clamp(4 x habitability %, 10, 100)`; all zero for Alternate Reality. Operable counts use *current* population (auto-build uses next year's projected population).
- One starbase and one scanner per planet; 512 fleets per race; just under 32,767 ships per design stack; 32,760 kT per mineral in a packet; mineral concentration and environment values clamp to 1..99 on every step.
- Race gates: Alternate Reality (no mines/factories/defenses, no such template entries); Claim Adjuster (half-price terraforming, no terraform template entries); Inner Strength (defenses x3/5); Total Terraforming (terraform 70); Mineral Alchemy (alchemy 25); Packet Physics (cheap and small packets, half decay); Interstellar Traveler (dear packets, one extra overspeed class); the factory "one kT less" wizard option; a per-race status bit that removes 20% of a planet's resource output before the queue is funded (`FUN_10b8_0000`, `:76404`-`76410`, the same status byte discussed in `turn-generation-engine.md` §5).
- Option gates: the game-setup "No Random Events" bit suppresses the comet, environment-shift, mineral-deposit and Mystery Trader rolls, and also the colonisation artifact bonus (`turn-generation-engine.md` §11; the complete option-bit-to-step map is §1b there: the special-object passes and the salvage rolls are *not* gated by this bit); the tutorial-mode bit changes the factory mineral cost (§6).
- Resource funding: a planet's resource total includes the deferred Ultimate-Recycling credit blended in as `output + deferred x output / (deferred + output)` (`:76365`-`76369`, `:76397`-`76401`), see `race-traits.md` §3a.

**10h. Live test of the auto-build "up to N" meaning (Stars! v2.70j under otvdm-master-2697, 2026-09-30).** Setup: Tiny universe, one Humanoid homeworld (Zucchini; default race settings, so a factory costs 10 resources + 4 kT Germanium and a mine 5 resources), with the queue's "Contribute only leftover resources to research" box ticked so the queue got the planet's whole output. Start of year 1: population 25,000, 10 mines and 10 factories built, both operable caps 25 (shown as "10 of 25"), 35 resources per year, 451 kT Germanium. A total-target reading and a per-turn-maximum reading predict clearly different results, and the game followed the per-turn-maximum reading in every year.

*Run 1, `Factories (Auto Build) Up to 12` alone* (10 built, so a total target would allow only 2 more and then nothing):

| Year (after generating) | Resources per year | Factories built (cap) | Change | Queue afterwards |
|---|---|---|---|---|
| start | 35 | 10 (25) | | Factories Up to 12 |
| 1 | 41 | 13 (28) | +3 | Factory 1 (59% done), Factories Up to 12 |
| 2 | 50 | 17 (33) | +4 | same |
| 3 | 60 | 22 (38) | +5 | same |
| 4 | 71 | 28 (43) | +6 | same |

The planet passed 12 factories in the very first year and kept buying, each year spending essentially the whole resource income (year 1: 3 units; year 2: the finishing head unit plus 3; then 5 and 6), and stood at 28 factories, more than twice N, when the run ended. Each year ended with a leftover fraction peeled off into a one-unit manual `Factory 1` entry at the head of the queue ("59% done"), exactly as the status-code table in section 8 describes; the auto entry itself never changed and never left the queue. Germanium fell from 451 to 389 kT. The auto entry's text stayed "Factories ... Up to 12" throughout.

*Run 2, a different N with resources far above N* (queue: `Factories (Auto Build) Up to 2`, then `Mines (Auto Build) Up to 3`; the old auto entry removed; state at the start: 28 factories and 10 mines built, caps 43, 71 resources per year, a `Factory 1` head unit at 69%; a total target would buy no mines at all since 10 exceeds 3, and no factories beyond the head unit):

| Year | Resources per year | Factories | Mines | Change |
|---|---|---|---|---|
| start | 71 | 28 (43) | 10 (43) | |
| 5 | 81 | 31 (50) | 13 (50) | +3 factories (head unit plus 2), +3 mines |
| 6 | 90 | 33 (57) | 16 (57) | +2, +3 |
| 7 | 101 | 35 (66) | 19 (66) | +2, +3 |

Every year the two entries bought exactly N units (2 factories = 20 resources, 3 mines = 15 resources, 35 resources in all) although the planet was nowhere near a cap and had 80-100 resources available; the rest went to research. The entries stayed on the queue with unchanged text ("Up to 2", "Up to 3", shown in green, the finishes-next-year colour).

*Queue-dialog display.* The dialog prices an auto entry as N whole units regardless of the built count: `Up to 1` showed 10 resources and 4 kT Germanium, `Up to 12` showed 120 resources and 48 kT Germanium (still so after 13 and 17 factories existed), `Up to 11` showed 110 and 44, and each Add click raises N by one. Its completion estimate is likewise computed for N units ("Completion 1 - 9 years" at 35 resources per year for Up to 12, then "1 - 8" after year 1; "1 - 4 years" for Up to 11 at 71). The auto entry reads "0% Done"; the head `Factory 1` entry shows the peeled partial progress (59%, 69%). The Remove button lowers N by one per click (Up to 12 becomes Up to 11), so removing an auto entry outright takes N clicks.

Result versus the code reading: consistent. The purchase routine's only comparison is `min(N, operable - built)`; the built count is never subtracted from N, so the entry re-buys up to N units every year until the operable cap is reached. Consequences for a re-implementation: (1) an auto-build "up to N" order never goes idle merely because N or more units already exist; (2) in the manual's own default template, "Factories up to 10" followed later by "Factories up to 25" simply buys up to 10, then up to 25, per year; (3) the operable cap (based on next year's projected population) is the only stop; (4) the queue estimator's cost figure for such an entry is N units, not N minus built. Only Factories and Mines were exercised live; Defenses share the same clamp code and are assumed to behave the same, and the cap-clamp itself (`operable - built` smaller than N) was not reached in the test.

## Worked Examples

All three examples use the confirmed defaults from §1–2 (1 resource / 1000 population; factories cost 10 resources + 4 kT Germanium and yield 1 resource/year each) plus the community-cited, lower-confidence mine defaults from §4 (5 resources to build, 1 kT/mine/year at 100% concentration, no minerals required) purely for illustration — treat the mine-specific numbers in these examples as illustrative, not verified constants.

### Example 1 — A brand-new colony bootstrapping its first factories

- Population: 100,000. No factories or mines built yet. Surface Germanium: 500 kT (a generous starting stock, for illustration).
- Queue: `Factories (Auto Build) Up to 10`.

**Turn 1 calculation:**
1. Population resources: `floor(100000 / 1000) = 100`.
2. No factories exist yet, so factory-derived resources = 0. Total resources available = **100**.
3. Operable factory cap = `floor(100000/10000) * 10 = 100` — far above the 10 the auto-build order targets, so the cap isn't the binding constraint this turn.
4. The auto-build order may buy up to 10 factories per turn. Cost for 10: `10 * 10 = 100 resources` and `10 * 4 = 40 kT Germanium`.
5. Resources (100) exactly cover 10 factories; Germanium (500 kT on hand) easily covers 40 kT. All 10 factories complete this turn. Resources remaining: 0. Germanium remaining: 460 kT.

**Turn 2 calculation** (now with 10 operating factories):
1. Population resources: 100 (population unchanged for simplicity).
2. Factory resources: `10 operating factories * 1 = 10`.
3. Total resources available = **110**.
4. The auto-build order still reads "Up to 10", but the 10 already built do not use it up (per 10h, N is a per-turn maximum): it buys another 10 factories for 100 resources and 40 kT Germanium (Germanium 460 to 420), giving 20 factories. The 10 resources left over flow to research. Each further turn it buys 10 more until the operable cap (100 here) or the Germanium or resources run out.

This demonstrates the population-resource formula, the default factory cost/output, an auto-build order buying its full per-turn quantity in one turn and again the next turn (it goes idle, without being deleted, only when the operable cap leaves no room), and leftover resources defaulting to research.

### Example 2 — A maturing colony with a mixed manual + auto-build queue and a mid-queue mineral bottleneck

- Population: 400,000. Currently operating: 30 factories, 20 mines. Surface Germanium: 25 kT (mining hasn't kept pace with factory demand).
- Queue, top to bottom: `Scout` (manual, one-shot; costs 10 resources + 2 kT Ironium), `Factories (Auto Build) Up to 40`, `Mines (Auto Build) Up to 40`, `Defenses (Auto Build) Up to 5` (starting from 0 defenses; illustrative cost 15 resources each, per the lower-confidence figure in §5).

**Turn calculation:**
1. Population resources: `floor(400000/1000) = 400`.
2. Factory resources: operable cap is `floor(400000/10000)*10 = 400`, well above the 30 actually built, so all 30 operate: `30 * 1 = 30`.
3. Total resources available = **430**.
4. Top of queue, the `Scout`: costs 10 resources + 2 kT Ironium; assume Ironium is plentiful. Spend 10 resources → **420 remaining**. Scout completes and leaves the queue.
5. `Factories (Auto Build) Up to 40`: the entry may buy 40 factories this turn (the operable cap of 400 leaves ample room; the 30 already built are not subtracted). Full cost would be `40*10=400 resources` and `40*4=160 kT Germanium`, but only 25 kT Germanium is on hand — the mineral-limited affordable amount is `floor(25/4) = 6` factories (24 kT Germanium, 60 resources). Because this is an auto-build item, it simply builds as many as it can afford (6) rather than blocking the queue: spend 60 resources and 24 kT Germanium → **360 resources remaining**, 1 kT Germanium left on hand, 36 factories now operating.
6. `Mines (Auto Build) Up to 40`: the entry may buy 40 mines this turn, at the illustrative 5-resources/no-minerals cost: `40*5=200 resources`, fully affordable → **160 resources remaining**, 60 mines now operating.
7. `Defenses (Auto Build) Up to 5`: 5 defenses at an illustrative 15 resources each = 75 resources, fully affordable → **85 resources remaining**, 5 defenses now built.
8. Queue now has nothing left to fund. All **85** leftover resources flow to research this turn.

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

- ~~**Exact default mine settings.**~~ **RESOLVED from the binary (this pass).** The race-wizard bound table and default preset record were read directly out of `stars.exe`. The mine defaults are **mine output 10 (kT per 10 mines per year), 10 mines operable per 10,000 colonists, and mine cost 5 resources**, with ranges 5–25, 5–25 and 2–15 respectively. The full table is in §4, "Race-design economic settings, recovered from the binary". This worked because the tables are not in the shared data segment at all. The clamp routine reads them relative to its own code segment (segment 29), right after the point-cost tables `race-traits.md` §1a had already recovered from that same segment. So the "Ghidra database unavailable" obstacle recorded below was never actually the blocker: a raw NE-segment-table parse of the executable reaches the bytes directly. The mine-cost default of 5 independently matches the live-client test recorded below, and that match validates the extraction. The earlier notes are kept for provenance:
  **Previously PARTIALLY RESOLVED by direct empirical testing (2026-09-04).**
  Opened the actual Production Queue dialog (Stars! v2.70j/JRC3) for a freshly-created custom race
  with no economic-slider changes from wizard defaults, and selected "Mine": the dialog's own
  "Required Minerals" panel read **Ironium 0kT, Boranium 0kT, Germanium 0kT, Resources 5** —
  confirming the community-cited default build cost of **5 resources, no minerals** directly from
  the game. The same test on "Factory" read **Germanium 4kT, Resources 10**, confirming the
  already-known factory default exactly. Mine output (kT/mine/year at 100% concentration) and
  mines-operable-per-10,000-colonists were not tested this pass (would need a multi-turn mining
  observation) and remain open.
  **Attempted at the code level this pass, not completable.** The Custom Race Wizard's economic
  slider page (`RACEWIZARDDLG3`, segment 29) does not itself store the min/max/default numbers; its
  paint and value-clamp logic (`FUN_10e0_1d9e` and `FUN_10e0_223e` respectively) resolve each of
  the 7 slider rows to a generic per-race field index via a small per-row lookup table, then clamp
  a candidate value against two further tables — addressed directly by that field index, not
  per-race data — that hold the field's min and max bounds; the same field index and the same
  generic accessor (`race_base + field_index + 0x3e`) are reused for every other "generic per-race
  field" this project has already documented (PRT, LRT bits, etc.), confirming this is one uniform
  mechanism rather than something economic-slider-specific. This is a genuine, previously
  unattributed finding about *how* the wizard validates and presumably defaults these sliders, but
  the actual byte values in those two bound tables (and any separate default-value table) are
  binary data-segment content, not something the exported decompiled source text carries — this
  pass could not read them without direct access to the Ghidra project's own database, which was
  not available (the project's `.rep` database files returned a permission error when read
  directly, and running the headless analyzer to script a byte dump was judged out of scope for
  this pass). So: mine output and mines-per-10,000 defaults remain unconfirmed by a clean static
  value, as before — but the mechanism that *would* yield them, if the raw table bytes were
  extracted in a future pass, is now identified precisely.
- ~~**Resources-per-colonist favorable extreme.**~~ **RESOLVED from the binary (this pass): 700.** The manual's OCR text reads "one resource... for every seven colonists". The wizard's bound table stores this setting's minimum as 7, and the stage appends a literal "00" suffix to the displayed value (the suffix string is in the data segment next to the "kT" suffix used for mine output). So the favorable extreme displays as **700** colonists, and the default of 10 displays as 1,000. The OCR "seven" was indeed a dropped "00".
- ~~**Defense build cost.**~~ **RESOLVED, and the earlier resolution below it was itself wrong (segment-24 sweep, this pass).** The 44-resource / Packet-Physics-25 / Interstellar-Traveler-48 figures that a previous pass attached to Defenses belong to the Mixed Mineral Packet item (kilotons of each mineral, plus 5 or 10 resources), as the completion-effect routine and the caption table prove (§10). The true Defenses cost is the SDI component's own record: **15 resources + 5 kT each of Ironium, Boranium and Germanium**, times 3/5 for Inner Strength (§5). The community figure of about 15 resources is therefore right, and the earlier instruction to "retire" it is withdrawn. The 100-defense ceiling is confirmed as the top of `clamp(4 x habitability %, 10, 100)` (§3).
- ~~**Terraforming resource cost.**~~ **RESOLVED, and likewise corrected (segment-24 sweep, this pass).** The 70 / 110 / 120 figures were the single-mineral Mineral Packet kilotonnages (§10). Terraforming (item types 4, 5, 12) costs **100 resources per 1%, 70 with Total Terraforming, halved for Claim Adjuster, no minerals** (§6), which matches the community figure. The unexplained "why Packet Physics and Interstellar Traveler are singled out" puzzle disappears: they are the two traits with special packet economics.
- ~~**New, open (segment-24 sweep): the meaning of the "up to N" figure on auto-build Mines, Factories and Defenses.**~~ **RESOLVED by live test (2026-09-30): N is a per-turn maximum, exactly as the code reading predicted; the manual's "total target" wording is wrong for this build.** See 10h for the observed numbers.
- ~~**Race-wizard slider ranges (min/max), not just the default and one favorable extreme.**~~ **RESOLVED from the binary (this pass).** All seven ranges and defaults are in §4's recovered table: colonists/resource 700–2,500 (default 1,000); factory output 5–15 (10); factory cost 5–25 (10); factories/10k 5–25 (10); mine output 5–25 (10); mine cost 2–15 (5); mines/10k 5–25 (10). The numbers come from the same field-index-keyed min/max tables identified below, read from segment 29 of `stars.exe` rather than from the shared data segment. The point-cost side of the trade-off (how points are charged per setting) is in `race-traits.md` §1a. The original note is kept below for provenance: The manual's "excel at production" checklist gives only the single most-favorable endpoint of each economic slider (factory/mine cost, output, and per-10,000-colonist operability), not the full numeric range or the mechanism (points cost) by which a race design trades one for another. The *Official Strategy Guide* mirror's worked "Monster Race" examples show plausible intermediate values (e.g. "13-15/7-9/18-25" for one archetype) but these are example builds, not the underlying range table. **Partially resolved by inspection of the exported client, this pass** — see the new note under "Exact default mine settings" immediately above: the wizard clamps every slider against a pair of generic, field-index-keyed min/max byte tables (`FUN_10e0_223e`) shared with every other "generic per-race field" this project documents, so the range-lookup *mechanism* is now identified precisely. The actual numeric bounds in those tables were not extractable from the exported decompiled source (they are binary data-segment content, not code) and remain unconfirmed; this is the same underlying blocker as the mine-defaults item above, not a separate one.
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
  ~~that narrower question (auto-build specifically unblocked mid-turn by the item ahead of it
  running out of things to spend on) remains open.~~ **RESOLVED by inspection of the exported
  client, this pass: yes.** See the new note under §8 above, tracing the actual per-turn queue
  loop (`FUN_10b8_0000`/`FUN_10b8_0756`, segment 24) — the whole queue is walked top-to-bottom in
  one function call per planet per turn, so an item that stops reporting a blocking status
  partway through that call (whether by completing outright or simply having nothing further to
  spend on) lets the walk proceed immediately to the next entry, auto-build or not, in that same
  turn; only an item that still reports a blocking status after being given its share of the
  budget causes the walk to stop for the year. The manual's "halts if the planet runs out of
  minerals" wording is therefore best read as "stops producing *more* once it can no longer afford
  the next whole unit," not as "makes zero progress the instant the full batch cost exceeds what's
  on hand." Exact rounding of the intermediate mineral figure shown for the remaining 97 units
  (380kT, 8kT less than the naive 97×4=388kT expectation) was not fully reconciled and is noted
  here rather than asserted as a precise formula. Screenshots preserved at
  `docs/ui-reference/production-queue-partial-fulfillment.png` and
  `docs/ui-reference/planet-view-after-turn1-generate.png`.
- ~~**Whether idle (population-capped) factories/mines can ever be lost**~~, e.g. to population
  decline stranding built infrastructure permanently versus it simply waiting inactive for population
  to recover. **RESOLVED (2026-09-24 pass) by an exhaustive cross-binary search for every writer of
  the built-factory/built-mine fields — not via starvation, but via a distinct, previously-undocumented
  random event.**

  The two fields are packed together in the planet record's 16-bit word at **byte offset `0x16`**
  (corrected in the segment-24 sweep: this text originally said "one dword at byte offset `0x2c`",
  a mix-up between the export's 16-bit word indexing and byte offsets — byte `0x2c` is actually the
  starbase-design/damage word): bits 4-15 hold the **built factory count** (read as `param_1[0xb] >> 4`
  — confirmed consistently in `FUN_1048_4faa`'s cap-comparison, `FUN_1048_4d50`, and the
  production-apply code below), and a 12-bit **built mine count** is formed from byte `0x15` (low
  eight bits) plus the low nibble of byte `0x16` (high four bits)
  (`CONCAT11((char)param_1[0xb], (char)(param_1[10] >> 8)) & 0xfff`, confirmed in `FUN_1048_4cce`).
  Every reference to either field across the full exported source was enumerated (not just the one
  annual-resource-production routine checked previously) and each was individually classified:

  - **The only routine that *increments* either field** is the production-queue build-completion
    committer, `FUN_10b8_0e68` (segment 24, `stars.exe.export.c:76969`-`77488`, called once per
    completed queue item from the segment-24 per-turn queue-apply loop `FUN_10b8_0000` at `:76555`).
    Its `case 0`/`8` (Mines) and `case 1`/`7` (Factories) branches (`:77186`-`77221`) each compute
    `min(populationCap, requestedAmount) - currentCount` and add the (always non-negative, floor-
    clamped-to-0) result into the packed field — an ordinary "build N more" increment, never a
    decrement.
  - **A second, distinct writer in the same function — not previously known to this project — zeroes
    both fields outright: `case 0xd` (`stars.exe.export.c:77424`-`77459`).** This branch broadcasts
    message string **283**, `"Strong fundamental forces have rebirthed \p."` (recovered in
    `extracted-game-data/message-strings.txt`), to every player in the game (`:77426`-`77431`), then —
    for any non-Alternate-Reality owner — zeroes byte `0x15` and the word at `0x16` (wiping *both* the
    factory count and the mine count, `:77434`-`77435`), zeroes the defense count and sets the
    planetary-scanner field to "none" (`:77436`-`77437`), and then, for every owner, zeroes all three
    mineral stockpiles, re-rolls all three mineral concentrations **and re-rolls all three
    environment values, current and original**, from fresh RNG draws (`:77439`-`77458`; full ranges in
    §10c). This event was not
    previously documented anywhere in this project prior to the pass below, which corrects its
    original "internal random event" characterization.
  - **One further write site, `FUN_1070_2440` (segment 15, `stars.exe.export.c:45838`), is turn/save-
    file deserialization**, not a gameplay mechanic: it decodes the same adaptive-width field encoding
    already documented in `save-turn-file-format.md` §8, loading a previously-saved built-count value
    back into memory — not a decline write.
  - Two further candidate matches were checked and ruled out: `FUN_1070_4ad8`
    (`stars.exe.export.c:48146`) only *reads* the fields (an "does this planet have any built
    infrastructure" test, feeding some other decision); and a cluster of matches near
    `stars.exe.export.c:39440`-`39460` turned out to belong to an unrelated GDI-drawing routine
    (confirmed by neighboring `SETROP2`/`SETBKCOLOR`/`INTERSECTRECT` calls) operating on a
    differently-shaped record, not a planet.

  **Conclusion on the original writer-search question: population decline/starvation itself never
  touches these fields** — confirmed both by this exhaustive writer search and by
  `population-growth.md` §3's independent, fully-traced read of the population decline function
  (`FUN_1038_47d0`), which only ever touches the population field and its own fractional-carry byte.
  The "grow into your infrastructure" framing (idle factories/mines sit inactive, not destroyed by
  population loss) stands confirmed. But the literal question — can built factories/mines ever be
  permanently lost at all — resolves **yes**, just not by the originally-suspected mechanism, and (see
  immediately below) not by a random event either: completing a Genesis Device wipes both counts to
  zero (along with mineral stockpiles and concentrations), as a deliberate player/AI choice, independent
  of population or starvation.

  **Conclusion on this event's trigger, corrected this pass: it is not a random turn-generation event
  at all — it is the build-completion effect of the Genesis Device component, a real, player/AI-
  buildable production queue item.** The project owner flagged that this event's *effect* was fully documented but its
  trigger chance/frequency was not, on the assumption (carried over from the prior pass's own framing,
  quoted above) that it was an internal-only, player-inaccessible code. Tracing backward from
  `FUN_10b8_0e68`'s `case 0xd` (the effect) overturns that assumption:

  - **`FUN_10b8_0e68` has exactly one call site in the entire exported source** (grep-confirmed, no
    second reference and no function-pointer/jump-table use anywhere) — `stars.exe.export.c:76555`,
    inside the ordinary per-planet queue-apply loop `FUN_10b8_0000`. Its category/item-type parameters
    are decoded directly from the planet's real, persisted queue record (the same packed format
    documented in §8 above), not from any parallel/shadow structure. Mechanically, this item-type-13
    effect can therefore *only* fire by way of a genuine queue record reaching the head of a planet's
    queue — which already rules out it being a hidden slot of either of `turn-generation-engine.md`
    §5's 13-entry/6-entry random-event tables (both rolled and dispatched entirely inside segment 31,
    never touching the production queue at all).
  - **The decisive cross-check: the cost-calculator `FUN_10d0_221a`'s own `case 0xd`
    (`stars.exe.export.c:87896`-`87910`) prices item-type 13 by looking up a real, named component** —
    it packs category `0x8000` (Planetary scanners/defenses) with subtype `0xe`(14) and passes that
    straight to the shared component-availability/cost resolvers (`FUN_1008_5194`/`FUN_1050_7d44`),
    the identical mechanism `ai-opponent-behavior.md` uses project-wide to name components from
    `extracted-game-data/component-stats.tsv`. That category/subtype pair is **Genesis Device**
    (`component-stats.tsv` row 299: category `0x8000` idx `14`, cost **5,000 resources, 0 kT of every
    mineral**) — confirmed independently in `ai-opponent-behavior.md`'s own catalog-numbering work,
    which reached the identical `case 0xd`/Genesis-Device identification from an unrelated angle
    (resolving neighboring catalog items 4/5/`0xc`) without knowing about this document's Planet
    Rebirth entry. **Item-type 13 is therefore a real, priced, catalog production item — Genesis
    Device — not an internal-only code.** This document's prior-pass claim that "`case 0xd` sits
    numerically alongside, but structurally separate from" the ordinary item-type codes, and that the
    game engine "trigger[s] it internally rather than a player ever queuing it directly," is
    **corrected**: a player (or the AI) queues Genesis Device exactly like any other catalog item, and
    `case 0xd`'s zero-factories/zero-mines/reroll-concentrations effect (message 283, "Strong
    fundamental forces have rebirthed \p.") is what happens when that one-shot device completes
    construction on a planet — a deliberate, planet-resetting super-terraform trade-off, not a random
    misfortune. "Planet Rebirth" is kept here only as this project's descriptive name for that
    build-completion effect, not as a distinct random-event mechanic.
  - **Correction to a claim in this bullet's earlier form (segment-24 sweep):** the purchase routine
    `FUN_10b8_0756` does *not* give item-type 13 a special "lump-sum" path. Its `local_24` test
    (`stars.exe.export.c:76677`) is true only for **auto-build** entries (category 1, type below 7);
    every manual catalog item — Mine, Factory, Defenses, Alchemy, Terraform, packets, scanners and
    Genesis Device alike (types 7-27) — fails it and is bought by the same whole-unit loop with
    partial-progress banking (§8, §10). Genesis Device is simply an ordinary manual item that happens
    to cost 5,000 resources.
  - **Gating condition, resolving the "chance/frequency" question the project owner actually asked
    about.** There is no "percentage chance per turn" to find, because completing a queued item is
    deterministic, not random — the real gate is *component availability*: a race can only queue
    Genesis Device once it has unlocked the component at all. `turn-generation-engine.md` §5 already
    documents the one-time, per-race random grant mechanism for exactly this component: **Genesis
    Device is bit index 10 of the confirmed 12-component list** that `FUN_1118_1196`'s 13-entry
    table/`FUN_10d8_4b8e` grants ("Multi Cargo Pod, Multi Function Pod, Langston Shell, Mega Poly
    Shell, Alien Miner, Hush-a-Boom, Anti Matter Torpedo, Multi Contained Munition, Mini Morph, Enigma
    Pulsar, **Genesis Device**, and Jump Gate, in bit order" — Genesis Device is the 11th named entry,
    0-indexed bit 10). So the only "randomness" behind Planet Rebirth is that same already-documented,
    once-per-race-per-game unlock roll (which, per the correction in `turn-generation-engine.md` §5, is
    fired by battle wreckage, starbase dismantling or planet capture rather than by a per-turn timer; it is
    not a per-planet event) — after which building and completing the device on any given planet is entirely the
    player's (or AI's) choice, gated only by having 5,000 spare resources.
  - **This also resolves the AI-opponent lead found while searching for a hidden trigger, and removes
    the apparent paradox it seemed to pose.** `FUN_1090_2736` (`stars.exe.export.c:62694`-`62794`), the
    generic "insert an item into a production plan" function AI build-planning logic calls ~90 times,
    has one call site at `stars.exe.export.c:68067` — `FUN_1090_2736(0xd, 1, 1, 1)`, i.e. **queue
    exactly one Genesis Device** — gated on the target planet's population exceeding 10,000
    (`:68042`), the AI's staged plan not already containing one (`:68050`), and a couple of
    `FUN_1040_1652` random-chance rolls (`:68064`-`68065`, presumably the AI's own "is it worth using
    my one-shot device here" heuristic, not a game-engine trigger). Read against a self-destructive
    "internal random event," this looked paradoxical (why would AI logic deliberately wipe its own
    planet?); read as "the AI chooses to build its one Genesis Device on a large, mature colony," it
    is unremarkable, sensible build-planning — quantity 1 is exactly right for a one-shot special
    component, and there is no paired manual/auto-build case for item-type 13 anywhere in either
    `FUN_10d0_221a` or `FUN_10b8_0e68`'s switches, consistent with a single-use item rather than a
    standing order.
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
