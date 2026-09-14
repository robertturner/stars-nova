# Fleet Movement, Scanning, and Cargo

Behavior specification for warp-speed movement, fuel, scanning/detection, and cargo logistics in the 1995-2000 4X game *Stars!*, written for a clean-room reimplementation. All facts below are restated in original wording from public community research — primarily the long-standing *starsfaq.com* "Stars! Advanced and Technical FAQ" ("Guts" reference), the community-maintained *Stars!wiki* at wiki.starsautohost.org (accessed through its Wayback Machine mirrors, since the live site sits behind an active bot-detection challenge), a widely-mirrored GameFAQs strategy guide, and TV Tropes' mechanics summary — plus the official Player's Guide manual (hosted as a public-domain scan on archive.org) for UI-level descriptions. No content was derived from the original binary, any decompilation artifact, or Ghidra output. Where sources are silent, contradictory, or only inferred, this is called out explicitly under "Open Questions" rather than asserted as fact.

## Overview

A *Stars!* fleet moves by being given one or more **waypoints** — destinations with an optional task (colonize, transport cargo, remote-mine, lay mines, scrap, etc.) — and a **warp speed** (1 through 10). Distance covered per year scales with the *square* of the warp factor, so speed has a very steep fuel cost: doubling from warp 5 to warp 10 roughly quadruples the ground covered but the fuel bill grows much faster still, because fuel use scales with both mass and the distance-to-max-distance ratio at that speed. Ships either burn fuel to move (most engines) or, below a per-engine "free" threshold speed, generate a little extra fuel while moving (ramscoop engines). Detection of other empires' fleets and planets is entirely scanner-range-based and split into two tiers — ordinary scanners that only reveal ships/planets while physically in range, and rarer "penetrating" scanners that also reveal a planet's mineral/population/defense details without a ship actually visiting it. Cargo (minerals, colonists, and fuel) is carried in a hull's built-in cargo hold, optionally enlarged with cargo-pod components, and is moved between planets and fleets with per-waypoint load/unload orders, including conditional orders that stop loading/unloading once a target amount is reached. Longer-range logistics can bypass warp travel altogether via player-built Stargates or naturally occurring Wormholes.

Sources: [Navigation — Stars!wiki](https://web.archive.org/web/20241109000233/https://wiki.starsautohost.org/wiki/Navigation), [Chapter 9: Intelligence — Stars!wiki](https://web.archive.org/web/20210731001838/https://wiki.starsautohost.org/wiki/Chapter_9:Intelligence), [Guts of fuel generation — Stars!wiki](https://web.archive.org/web/20250114132950/https://wiki.starsautohost.org/wiki/Guts_of_fuel_generation)

## Mechanics

### 1. Warp speed and distance per turn

*Stars!* has ten warp factors. The maximum distance a fleet can cover in a single year at warp factor *N* is `N²` light-years — so warp 4 tops out at 16 ly/year, warp 9 at 81 ly/year, and warp 10 (the fastest speed any ship can ever be ordered to) at 100 ly/year. This relationship is confirmed both by a community minefield-crossing analysis that explicitly measures "100% chance of travelling only 16ly" at warp 4 and notes ships "sometimes travel as far as 81ly at warp 9" (matching 4² and 9² respectively), and by the fan-made *Planets.nu* reimplementation's own help documentation, which states its "Max Distance Per Turn" constant is "equal to warp speed squared." A fleet given a waypoint closer than its maximum range for the chosen speed simply arrives that same year and can act on its task immediately; a fleet given a farther waypoint covers as much of the distance as its ordered speed and available fuel allow and continues from where it stopped the following year.

Each engine component has its own top-line rated speed (e.g., a Fuel Mizer tops out around warp 6; higher-tier engines reach warp 9 or 10 natively), and normal play cannot order a ship faster than its installed engine's rated maximum. **Warp 10 is a special case**: it can be selected for *any* ship regardless of its engine, but unless the engine is one of the small set specifically rated "safe" at warp 10 (e.g., Interspace-10, unlocked via the No Ram Scoop Engines trait, or the endgame Trans-Star 10), every individual ship attempting warp 10 risks outright destruction: a 10% chance per year, rolled independently per ship (not per fleet, and not scaled by how many engines that ship's hull mounts).

A second, unrelated risk applies only to fleets built with the **Cheap Engines** lesser racial trait: any time such a fleet is ordered above warp 6, there is a 10% chance per year that the engines simply fail to engage at all (the fleet does not move that turn, and is not damaged or destroyed — contrast this with the warp-10 destruction risk above, which is a different mechanic keyed to warp 10 specifically rather than "above warp 6").

Separately, scout- and transport-oriented play can push a ship faster than its engine's most fuel-efficient "free" speed simply by accepting a steeper fuel burn, up to the engine's absolute rated maximum — this is informally summarized in community strategy writing as "fuel is speed."

Sources: [Guts, §4.8.1 "Analysis of 'Best Speed' in a Minefield" — starsfaq.com](http://www.starsfaq.com/advfaq/guts2.htm), [Fuel Consumption Details — help.planets.nu](https://help.planets.nu/fuel-details), [Interspace-10 — Stars!wiki](https://web.archive.org/web/20250324023702/https://wiki.starsautohost.org/wiki/Interspace-10), [Lesser racial traits — Stars!wiki](https://web.archive.org/web/2023/https://wiki.starsautohost.org/wiki/Lesser_racial_traits), [Stars! Strategy Guide, §PG3-09 "Cheap Engines" — GameFAQs (Mars Jenkar/plague006)](https://web.archive.org/web/20210615103545/https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043), [Stars! (1995) — TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Stars1995), [Chapter 9: Intelligence — Stars!wiki](https://web.archive.org/web/20210731001838/https://wiki.starsautohost.org/wiki/Chapter_9:Intelligence)

### 2. Fuel consumption

**Consumption formula.** A widely-cited community derivation (originally posted to the `rec.games.computer.stars` newsgroup and preserved in the starsfaq.com "Guts" reference) states the baseline rule as: 1 milligram (mg) of fuel moves 200 kilotons (kT) of ship mass 1 light-year, when the relevant engine/speed combination's "Fuel Usage Number" (FUN — a per-engine, per-warp table constant printed in the game's own Guts-of-Engines reference) equals 100. Every other FUN value simply scales this proportionally. In formula form, fuel used per light-year traveled is:

```
FuelPerLY(mg) = (TotalMass_kT / 200) * (FUN / 100) * IFE_multiplier
IFE_multiplier = 0.85 if the race has Improved Fuel Efficiency, else 1.0
TotalFuelForTrip(mg) = FuelPerLY * DistanceTraveled_LY
```

A second, more granular formula is documented by the *Planets.nu* fan reimplementation (a separate, actively-maintained clone of *Stars!*'s ruleset, not the original game) as:

```
FuelUse = TRUNC( FUN * TRUNC(TotalMass_kT / 10) * TRUNC(Distance_LY) / MaxDistancePerTurn / 10000 )
MaxDistancePerTurn = WarpFactor^2
```

This integer-truncating version is algebraically the same relationship rescaled and rounded at different points, and is presented here as a plausible closer match to the original fixed-point engine's actual rounding behavior — but since it comes from a compatible clone rather than the original game, it should be treated as a secondary cross-check rather than a confirmed identical implementation (see Open Questions).

**Fuel generation (ramscoops).** Below its rated "free" warp threshold, a ramscoop-type engine produces fuel instead of consuming it, and every engine (ramscoop or not) produces a small amount of fuel at warp 1 specifically. Per-engine fuel generated over a distance `D` (at full-throttle travel for that speed) follows a step pattern relative to how far below the engine's free-travel speed it is running:

```
F = 0   -- running above the engine's free-travel warp (fuel is being consumed, not generated)
F = D   -- running exactly at the engine's free-travel warp
F = 3D  -- running 1 warp factor below the free-travel warp
F = 6D  -- running 2 warp factors below the free-travel warp
F = 10D -- running 3+ warp factors below the free-travel warp
```

This is per engine, not per ship — a hull with 2, 3, or 4 engines generates (or burns) proportionally more fuel. The Stars!wiki's own generation table (reproduced below, in mg, current as of the "JRC4" game patch — the wiki explicitly warns older patches had different, buggier values) shows every conventional (non-scoop) engine generating exactly 1 mg at warp 1, which is the mechanical basis for treating warp 1 as an always-available, self-sustaining crawl speed regardless of remaining fuel:

| Engine | W9 | W8 | W7 | W6 | W5 | W4 | W3 | W2 | W1 |
|---|---|---|---|---|---|---|---|---|---|
| Settler's Delight | - | - | - | 36 | 75 | 96 | 90 | 40 | 10 |
| Quick Jump 5 / Long Hump 6 / Daddy Long Legs 7 / Alpha Drive 8 / Trans-Galactic Drive / Interspace-10 / Trans-Star 10 | - | - | - | - | - | - | - | - | 1 |
| Fuel Mizer | - | - | - | - | - | 16 | 27 | 24 | 10 |
| Enigma Pulsar / Sub-Galactic Fuel Scoop | - | - | - | - | 25 | 48 | 54 | 40 | 10 |
| Radiating Hydro-Ram Scoop / Trans-Galactic Fuel Scoop | - | - | - | 36 | 75 | 96 | 90 | 40 | 10 |
| Trans-Galactic Super Scoop | - | - | 49 | 108 | 150 | 160 | 90 | 40 | 10 |
| Trans-Galactic Mizer Scoop | - | 64 | 147 | 216 | 250 | 160 | 90 | 40 | 10 |
| Galaxy Scoop | 81 | 192 | 294 | 360 | 250 | 160 | 90 | 40 | 10 |

**Improved Fuel Efficiency (IFE)** lesser racial trait reduces all fuel consumption by 15% across every engine, and unlocks the Fuel Mizer, Trans-Galactic Mizer Scoop, and Galaxy Scoop engines.

**Radiation hazard of low-tier ramscoops.** The cheapest ramscoop, the Radiating Hydro-Ram Scoop, is documented as gradually harming colonists riding along in the same fleet (unless the race's ideal radiation setting is very high), which in practice restricts that engine to mineral-only hauling rather than mixed colonist/cargo runs.

**Running out of fuel.** Fleet fuel is depleted during the normal movement phase of turn processing, alongside minefield hits, stargate jumps, and wormhole transits. Because every known engine generates a small positive amount of fuel at warp 1 (see table above), a fleet that exhausts its fuel reserve is not permanently stranded — warp 1 remains available indefinitely as a self-sustaining minimum speed. The precise turn-by-turn behavior when a fleet is ordered to a speed its remaining fuel cannot fully pay for (e.g., whether it silently completes only a partial hop at the ordered speed and stops mid-space, or whether the game automatically drops the order to the highest speed the remaining fuel *can* pay for) was not confirmed with a directly quotable source in this research pass; see Open Questions.

Sources: [Guts, §4.5 "Guts of Fuel Generation" and §4.5.1 "Guts of Fuel Usage" — starsfaq.com](http://www.starsfaq.com/advfaq/guts2.htm), [Guts of fuel generation — Stars!wiki](https://web.archive.org/web/20250114132950/https://wiki.starsautohost.org/wiki/Guts_of_fuel_generation), [Fuel Consumption Details — help.planets.nu](https://help.planets.nu/fuel-details), [Stars! (1995) — TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Stars1995), [Stars! Strategy Guide, §PG3-01 "Improved Fuel Efficiency" — GameFAQs](https://web.archive.org/web/20210615103545/https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043), [Order of Events — Stars!wiki](https://web.archive.org/web/20241127231555/http://wiki.starsautohost.org/wiki/Order_of_Events)

### 3. Scanning and detection

*Stars!* draws a hard line between two scanner tiers:

- **Standard (non-penetrating) scanners** only reveal a fleet or planet's presence and basic identity while a scan-equipped ship or planet is within range of it; they cannot read a distant, un-visited planet's mineral concentration, population, or defenses.
- **Penetrating scanners** additionally reveal a planet's full attributes (and any ships/starbase in orbit there) from range, without ever having to physically visit or orbit it — the effective coverage area is shown as a distinct overlay in the game's Scanner Pane. Penetrating scanner technology first becomes available at Electronics tech level 10; a homeworld's very first ordinary planetary scanner is meaningfully weaker (roughly 150 ly at Electronics tech 3).

**No Advanced Scanners (NAS)** is a lesser racial trait that trades away all penetrating-scanner technology in exchange for doubling the range of every conventional scanner the race builds (e.g., a race's Electronics-tech-3 homeworld scanner reaches 300 ly instead of 150 ly under NAS; the top-tier "Snooper 620X" scanner reaches roughly double its listed range under NAS as well). Some racial abilities keep a narrow slice of penetrating capability even under NAS — e.g., a Jack of All Trades race's built-in Scout/Frigate/Destroyer scanner, a Packet Physics race's mineral-packet scanner, or a Super-Stealth race's Chameleon/Robber Baron components — because those are treated as inherent racial abilities rather than the generic "planet penetrating scanner" technology NAS disables.

**Combining multiple scanners on one design.** When a single ship or starbase design carries more than one scanner component, its effective range is not a simple maximum or sum — it is computed as the fourth root of the sum of each individual scanner's range raised to the fourth power:

```
CombinedRange = ( Σ scanner_i_range^4 ) ^ (1/4)
```

For example, a design carrying two 100-ly scanners and one 60-ly scanner has a combined range of `(100^4 + 100^4 + 60^4)^(1/4) ≈ 120` ly — noticeably more than any single component alone, but far short of a simple sum. The same combination rule is described as applying to penetrating scanners as well.

**Cloaking vs. scanning.** A cloak reduces the effective range at which a scanner can detect the cloaked fleet by the cloak's percentage — e.g., an 80%-cloaked fleet is only detectable once an observer is within 20% of that scanner's normal rated range (so a 150-ly scanner would only spot it inside 30 ly). Because planetary scanners are stationary, an opponent who knows (or conservatively estimates) their range can plan routes that stay just outside it; mobile scout ships are correspondingly more effective at catching cloaked fleets specifically because their position is not predictable turn to turn. Minefields belonging to a Space Demolition race act as an additional, distinct detection layer against cloaked fleets that enter them: the chance of detecting a cloaked fleet inside such a minefield in a given year is `100% − cloak%` (e.g., an 80%-cloaked fleet is spotted 20% of the time per year it sits in the minefield), independent of ordinary scanner range.

**Packet Physics** races get a race-specific scanning shortcut: every mineral packet they fling carries a built-in penetrating scanner whose range equals the square of the packet's own travel warp speed, letting them "scan ahead" of their own mineral shipments.

**Scanning formula, verified against the exported client.** The turn-generation engine's actual detection test (run once per turn for every fleet-vs-fleet and planet-vs-fleet pair) is `distance² <= scanRange²`, with the scanning side's effective range scaled per-dimension by `(100 - cloak%) / 100` before squaring — consistent with, and a more precise restatement of, this document's "effective range reduced by cloak%" claim above. Separate range lookups exist for a fleet's own scan range versus a planet's, though both use the same distance-squared test. **Minefield detection** is confirmed to use a distinct formula from ordinary scanning: `(warningFieldValue + 4)`, squared, as a flat detection radius — independent of the cloak-based scanner test. **Wormhole (and similar unusual-object) detection** is confirmed to be genuinely probabilistic rather than a deterministic range check: a flat radius test is combined with a random roll (0-99) against the observed object's cloak percentage, meaning a wormhole or similar object can go undetected even at close range on any given turn. This document's existing multi-scanner-combination (fourth-root) formula and per-engine detection-range table were not independently checked against the executable.

Sources: [Chapter 9: Intelligence — Stars!wiki](https://web.archive.org/web/20210731001838/https://wiki.starsautohost.org/wiki/Chapter_9:Intelligence), [Chapter 4: Exploration and Expansion — Stars!wiki](https://web.archive.org/web/2023/https://wiki.starsautohost.org/wiki/Exploration_and_Expansion), [Lesser racial traits — Stars!wiki](https://web.archive.org/web/2023/https://wiki.starsautohost.org/wiki/Lesser_racial_traits), [Stars! Strategy Guide, §PG3-11 "No Advanced Scanners" and §PG2-10 "Jack of All Trades" — GameFAQs](https://web.archive.org/web/20210615103545/https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043), [Scanner Technology — Stars!wiki (help-file mirror)](https://wiki.starsautohost.org/wikinew/hlp/index_web/148.html) (page content could not be re-verified directly due to the live site's bot-detection wall; the fourth-root formula and worked 100/100/60→120 example are corroborated identically across two independent search-engine extractions of this same page), [Stars! (1995) — TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Stars1995)

### 4. Cargo capacity and cargo transfer

Every hull has a fixed base cargo capacity in kilotons (kT), shared by minerals, colonists, and (on most hulls) fuel is tracked as a *separate* pool from cargo rather than counting against it. Cargo-pod components can be mounted in eligible slots to add further capacity. Representative base cargo capacities documented for stock hulls:

| Hull | Base cargo (kT) | Notes |
|---|---|---|
| Small Freighter | 70 | |
| Medium Freighter | 210 | requires Construction tech 3 |
| Large Freighter | 1200 | requires Construction tech 8 |
| Super Freighter | 3000 | requires Construction tech 13; Inner Strength race only |
| Privateer | 250 | requires Construction tech 4 |
| Rogue | 500 | requires Construction tech 8; Super-Stealth race only |
| Galleon | 1000 | requires Construction tech 11 |
| Meta Morph | 300 | requires Construction tech 10; Hyper-Expansion race only |
| Mini-Colony Ship | 10 | Hyper-Expansion race only |

Fuel-tanker hulls are a separate special case: the Fuel Transport (750 mg fuel capacity) and Super-Fuel Xport (2250 mg) hulls actively manufacture roughly 200 mg of fuel per year while deployed and additionally raise the healing rate of other ships sharing their fleet.

**Loading and unloading.** Cargo movement is configured per-waypoint as a task, with options including loading/unloading all of a mineral type, loading/unloading a fixed amount, and a conditional form — informally "Set Waypoint to: `<amount>`" — that only loads (or only unloads) as much as needed to bring the source (or destination)'s on-hand quantity up to (or down to) a target level, rather than blindly moving everything available. This conditional form is the documented way to run a fully automated repeat-route between two planets (e.g., a supply run and a mineral-balancing run) without the ship ever over-draining either endpoint. Colonist cargo can also be loaded/unloaded the same way as minerals; Inner Strength race colonists additionally continue to reproduce at a reduced rate while riding in a fleet's cargo hold, spilling any surplus onto a planet the fleet is orbiting.

**Cargo via Stargates.** A fleet using a Stargate must be carrying only fuel — any mineral/colonist cargo must be off-loaded to a planet or another fleet first — except for Interstellar Traveler races, whose racial ability specifically allows gating fleets while still carrying mineral/colonist cargo, with that cargo's mass excluded from the gate's mass-rating check.

**Theft.** Super-Stealth race scanners (Pick Pocket, Robber Baron) can, in addition to their scanning role, siphon cargo directly from an enemy fleet or planet's stores when co-located with it.

Sources: [Hull — Stars!wiki](https://web.archive.org/web/20220121023418/https://wiki.starsautohost.org/wiki/Hull), [Chapter 11: Reducing Micro-Management — Stars!wiki](https://web.archive.org/web/20210730233813/https://wiki.starsautohost.org/wiki/Chapter_11:Reducing_Micro-Management), [Navigation — Stars!wiki](https://web.archive.org/web/20241109000233/https://wiki.starsautohost.org/wiki/Navigation), [Stars! Strategy Guide, §PG2-05 "Inner Strength", §PG2-08 "Interstellar Traveler", §PG2-02 "Super-Stealth" — GameFAQs](https://web.archive.org/web/20210615103545/https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043)

**Cargo-pod and fuel-pod bonuses — resolved by inspection of the exported client.** A per-design capacity calculator, invoked when computing a fleet's total cargo/fuel capacity for the Transfer dialog, adds a fixed per-slot bonus depending on the mounted component's category: **+50, +100, or +250 kT of cargo** capacity for three distinct cargo-pod-category components, and **+250 or +500 mg of fuel** for two distinct fuel-tank-category components, plus a further **+200** fuel from a component in a different category entirely (candidate: a ramscoop or specialty tank, not a plain fuel pod) — all additive atop the hull's own base cargo/fuel values. These values are exact as read from the code; mapping each numeric bonus to a specific named component (e.g., which bonus belongs to "Cargo Pod" versus "Super Cargo Pod") could not be confirmed, since no string/label data survives in the analyzed material. Separately, the same inspection independently confirms fuel is tracked through a structurally distinct low-level accessor from minerals/colonists (3 storage slots vs. 4), corroborating this document's existing "fuel is tracked as a separate pool from cargo" claim at the code level, not just the UI-description level.

**Fleet lifecycle, verified against the exported client.** A fleet is a fixed 124-byte record; fleet IDs are capped at **512 per empire** (an 8-bit fleet-number sub-field, plus a design-slot sub-field capped at the well-known 16-designs-per-race limit). Creating a fleet finds the smallest unused ID for that owner; deleting one frees its cargo/order sub-blocks and removes it from the owner's live-fleet list. Fleets sharing an exact same-turn location are linked into a same-location "stack," rebuilt once per turn/redraw — this is the concrete data structure behind the game's multi-fleet-per-tile display, not previously documented in this specification. **Split Fleet** clones off single-ship fleets per design slot from a source fleet; **Merge Fleets** sums ship counts, cargo, and mass into the primary fleet and deletes the absorbed fleets.

**Merge Fleets mechanics, verified against the exported client.** The merge dialog lists every fleet at the same location, pre-selecting the fleet the merge was invoked from (and, when exactly two fleets are present, both). For each of up to 16 ship-design slots being merged: designs already at 100% fuel merge with no penalty; designs below 100% fuel undergo a **probabilistic fuel-synchronization pass** — a random roll (weighted by remaining fuel fraction and a per-design chance) can strand some ships behind rather than merging them, producing one of five graduated warning messages depending on how many ships were left behind. Designs with genuinely incompatible fuel or cargo capacities abort the merge entirely rather than partially merging. After a successful merge, per-ship special-ability stats (scanning, cloaking, and similar) are recombined: 6 scanner-type fields take the **maximum** value across all merged ships, while a separate 12-slot ability tally **sums** contributions, capped at **25**.

### 5. Waypoints and multi-turn movement orders

A fleet's full route is an ordered list of waypoints, each with its own optional task; the fleet moves toward its next unresolved waypoint every turn at its ordered warp speed and executes that waypoint's task once it actually arrives, then proceeds to the following waypoint the next turn. A **Repeat Orders** flag, valid when the final waypoint matches the starting point, causes the whole waypoint list to restart automatically once completed, which combined with conditional load/unload amounts (see above) is the documented way to build a "set once, forget it" supply route. Distances snap to a fixed one-light-year grid for waypoint placement (diagonal legs are a decimal amount slightly over one light-year per grid step). A route leg that exactly repeats an already-traveled leg (e.g., an out-and-back path) is drawn differently (white) from other legs in the UI.

**Stargates** let a fleet skip warp travel across arbitrary distance in a single turn, provided both the origin and destination planets have a stargate, the fleet carries only fuel (see Cargo, above), and the ship's mass and the distance requested both fall within the *sending* stargate's rated limits (mass checks additionally require the *receiving* gate's rating too). A gate can be pushed up to 5x over its rated distance or mass limit and still sometimes succeed — this "overgating" always damages the ship, and beyond 100% cumulative damage the ship does not survive the jump. Documented overgating damage/loss formulas:

```
RangeDamagePercent = 100% * (Distance - MaxRange) / (4 * MaxRange)
MassDamagePercent  = 100% * (1 - ((5*MaxSendMass - Mass)/(4*MaxSendMass)) * ((5*MaxReceiveMass - Mass)/(4*MaxReceiveMass)))
CombinedDamagePercent = MassDamagePercent + (100% - MassDamagePercent) * RangeDamagePercent

# Community-fitted approximation for the chance a mass-overgated ship vanishes outright (non-IT races):
P_vanish(%) = (100 - A) * (5*MaxMass - Mass)^2 / (4*MaxMass)^2 + A     -- fitted constant A ≈ 68
# For distance overgating, vanish chance is roughly RangeDamagePercent / 3 as a rule of thumb.
```

Interstellar Traveler races are documented as having a reduced (but not quantified) chance of losing overgated ships to the void, while suffering the same damage percentages as anyone else.

**Wormholes** are naturally occurring, free, uncapped-mass shortcuts between two points in deep space (they cannot form too close to a planet's gravity well). A fleet given a wormhole as a waypoint enters and exits the same year it reaches the opening. Wormholes are individually cloaked against ordinary detection by 75% until a player has discovered them at least once, and each end independently drifts a little every year, with an overall stability rating (from "Rock Solid," lasting 30+ years in one area, down to "Very Unstable," relocating within about 5 years) describing how long a given wormhole persists before disappearing or relocating.

Sources: [Navigation — Stars!wiki](https://web.archive.org/web/20241109000233/https://wiki.starsautohost.org/wiki/Navigation), [Chapter 11: Reducing Micro-Management — Stars!wiki](https://web.archive.org/web/20210730233813/https://wiki.starsautohost.org/wiki/Chapter_11:Reducing_Micro-Management), [Guts, §4.6 "Guts of Overgating" — starsfaq.com](http://www.starsfaq.com/advfaq/guts2.htm), [Order of Events — Stars!wiki](https://web.archive.org/web/20241127231555/http://wiki.starsautohost.org/wiki/Order_of_Events), [Stars! Strategy Guide, §PG2-08 "Interstellar Traveler" — GameFAQs](https://web.archive.org/web/20210615103545/https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043)

## Worked Examples

### Example 1 — Fuel burn for a loaded Privateer at warp 8 and warp 9 (from the original community derivation)

Setup: a race with Improved Fuel Efficiency (IFE, −15% fuel use) has a Fuel-Mizer-engined Privateer carrying 3 fuel pods (1400 mg total fuel capacity). Empty hull mass is 80 kT; fully cargo-loaded it weighs 330 kT. At warp 8 the Fuel Mizer's documented Fuel Usage Number (FUN) is 235.

```
FuelPerLY = (330 / 200) * (2.35) * 0.85 ≈ 3.3 mg/ly
One-way range on 1400 mg = 1400 / 3.3 ≈ 424 ly
```

Now suppose the same ship makes a round trip at warp 9 instead (FUN = 360 at warp 9), loaded (330 kT) outbound and empty (80 kT) on the return leg, and we want the total one-way-equivalent range the 1400 mg tank supports averaged over the full loaded+unloaded round trip:

```
AverageMassMoved = (330 + 80) / 2 * 2 = 410 kT-equivalent weight moved over the whole distance
FuelPerLY = (410 / 200) * 3.60 * 0.85 ≈ 6.27 mg/ly
RoundTripRange = 1400 / 6.27 ≈ 223 ly (i.e., the ship can go 223 ly out and 223 ly back on one tank)
```

This shows the double lever a heavier load and a higher warp factor both pull on fuel economy — going one warp faster (8→9) and hauling the return trip's weight into the average very nearly doubles the mg/ly cost per light-year versus the one-way loaded-only figure.

Source: [Guts, §4.5.1 "Guts of Fuel Usage" (attributed to Jason Cawley) — starsfaq.com](http://www.starsfaq.com/advfaq/guts2.htm)

### Example 2 — Combining three scanners on one design

A custom scout design mounts two 100-ly scanners and one 60-ly scanner (for illustration; in the real game most hulls only have one scanner slot type, but multi-scanner combining applies whenever more than one is present, e.g., via a built-in hull scanner plus a mounted component):

```
CombinedRange = (100^4 + 100^4 + 60^4)^(1/4)
             = (100,000,000 + 100,000,000 + 12,960,000)^(1/4)
             = (212,960,000)^(1/4)
             ≈ 120.8 ly
```

The combined range (≈121 ly) is only marginally better than either single 100-ly scanner alone, illustrating why stacking multiple scanners on one hull gives sharply diminishing returns compared to just using the single best available scanner.

Source: [Scanner Technology — Stars!wiki (help-file mirror)](https://wiki.starsautohost.org/wikinew/hlp/index_web/148.html)

### Example 3 — Detecting a cloaked scout, and a minefield's independent detection roll

A defending empire has a planetary scanner rated at 150 ly (non-penetrating). An enemy scout arrives 80%-cloaked.

```
EffectiveDetectionRange = 150 ly * (100% - 80%) = 150 * 0.20 = 30 ly
```

The cloaked scout is invisible to that scanner anywhere beyond 30 ly, even though the scanner's rated range is 150 ly. If that same scout instead flies through a Space Demolition race's minefield, a second and independent detection roll applies regardless of distance to any scanner:

```
P(detected by minefield, per year inside it) = 100% - 80% = 20%
```

So over, say, 3 years spent transiting the minefield, the scout's chance of being spotted by the minefield alone (ignoring re-rolls' independence caveats) is noticeably higher than a single year's 20%, even though the planetary scanner never sees it at all outside 30 ly.

Source: [Chapter 9: Intelligence — Stars!wiki](https://web.archive.org/web/20210731001838/https://wiki.starsautohost.org/wiki/Chapter_9:Intelligence), [Stars! Strategy Guide, §PG2-06 "Space Demolition" — GameFAQs](https://web.archive.org/web/20210615103545/https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043)

## Open Questions / Uncertainties

- **Exact behavior when fuel runs out mid-transit.** Every source found confirms warp 1 is a self-sustaining minimum speed (every engine generates a small positive amount of fuel at warp 1) and that fuel depletion is processed during the movement phase, but no source directly quotable in this pass states the precise turn-level rule for a fleet ordered faster than its remaining fuel supports — e.g., whether the game silently caps that turn's actual distance to whatever the available fuel buys at the ordered speed (leaving the fleet stranded in deep space at zero fuel, immobile except by refuel), or whether it automatically downgrades the order to the fastest speed the fuel on hand can fully pay for that turn. This should be validated against a testbed or a more specific manual passage before implementation.
- **Two non-identical fuel-usage formulas.** The original starsfaq.com community derivation (§2 above) and the Planets.nu fan-reimplementation's documented formula both describe "fuel scales with mass and distance relative to a per-engine/warp constant," but differ in exactly where integer truncation happens and in the constant's scaling (per-10-kT vs. per-200-kT terms). Since Planets.nu is a compatible but independently-implemented clone rather than the original binary, it is included here only as a cross-check on the shape of the formula, not as confirmation of the original game's exact rounding. The original game almost certainly truncates/rounds fuel to whole mg somewhere in the calculation (consistent with all documented amounts being integers), but the precise rounding points were not confirmed.
- **Fuel-generation table version dependence.** The Stars!wiki explicitly flags its fuel-generation table as accurate only for game patch "JRC4," warning that older patches "have numerous bugs in fuel generation" — and indeed an older starsfaq.com posting of what is nominally the same table records the Galaxy Scoop generating 0 mg at warp 9, while the wiki's JRC4-era table records 81 mg at warp 9 for the same engine/speed cell. Which value (if either) matches the specific game version a clean-room implementation should target is unresolved here and should be pinned to a specific patch intentionally rather than by accident.
- **Scanner Technology page could not be re-fetched directly.** `wiki.starsautohost.org`'s live site (including its `/wikinew/hlp/` help-file mirror) sits behind an active bot-detection interstitial that this research session could not pass on that specific page, and no Wayback Machine snapshot of it was found. The fourth-root multi-scanner formula and its worked 100/100/60→120-ly example are corroborated identically across two independent search-engine text extractions of that page, which is treated as reasonably reliable, but the fact itself was never viewed directly in full page context.
- **Complete per-engine rated-speed table.** This document names several specific engines and their documented free/rated speeds (Fuel Mizer, Settler's Delight, Interspace-10, Trans-Star 10, etc.) but a complete table of every engine's own top rated warp speed (as distinct from its ramscoop-free speed) was not assembled in this pass and would need to be sourced from the game's own Ship Design tech browser data or a community tech-level spreadsheet before implementation.
- ~~**Cargo-pod capacity bonus per pod**~~ **RESOLVED by inspection of the exported client** — see the "Cargo-pod and fuel-pod bonuses" note under §4 above (+50/+100/+250 kT cargo; +250/+500/+200 fuel). Per-hull slot *limits* (how many pods a given hull can mount) were not captured.
- **Warp-speed/fuel-consumption-per-LY formula was not found in the exported client segments examined for this pass** (the Transfer/Scanner/cargo-dialog code, and the AI/turn-generation code, were checked). This neither confirms nor contradicts this document's §2 formula — the actual movement/fuel-burn computation likely lives in a segment not covered by that pass.

## Sources

- [Guts (Advanced/Technical FAQ), §4.5 Fuel Generation, §4.5.1 Fuel Usage, §4.6 Overgating, §4.8.1 Best Speed in a Minefield — starsfaq.com](http://www.starsfaq.com/advfaq/guts2.htm)
- [Stars! Advanced and Technical FAQ, table of contents — starsfaq.com](http://www.starsfaq.com/advfaq/contents.htm)
- [Navigation — Stars!wiki](https://web.archive.org/web/20241109000233/https://wiki.starsautohost.org/wiki/Navigation) — waypoints, stargates, wormholes, grid snapping.
- [Chapter 9: Intelligence — Stars!wiki](https://web.archive.org/web/20210731001838/https://wiki.starsautohost.org/wiki/Chapter_9:Intelligence) — scanner tiers, cloak-detection percentage rule, minefield detection, scout design doctrine.
- [Chapter 4: Exploration and Expansion — Stars!wiki](https://web.archive.org/web/2023/https://wiki.starsautohost.org/wiki/Exploration_and_Expansion) — non-penetrating vs. penetrating scanner basics.
- [Chapter 11: Reducing Micro-Management — Stars!wiki](https://web.archive.org/web/20210730233813/https://wiki.starsautohost.org/wiki/Chapter_11:Reducing_Micro-Management) — repeat orders, conditional load/unload ("Set Waypoint to: X") cargo automation.
- [Order of Events — Stars!wiki](https://web.archive.org/web/20241127231555/http://wiki.starsautohost.org/wiki/Order_of_Events) — turn-processing order confirming when fleet movement/fuel/minefield/stargate/wormhole resolution occurs relative to other phases.
- [Guts of fuel generation — Stars!wiki](https://web.archive.org/web/20250114132950/https://wiki.starsautohost.org/wiki/Guts_of_fuel_generation) — full per-engine fuel generation table and patch-version caveat.
- [Hull — Stars!wiki](https://web.archive.org/web/20220121023418/https://wiki.starsautohost.org/wiki/Hull) — base cargo capacities and other hull stats by class.
- [Interspace-10 — Stars!wiki](https://web.archive.org/web/20250324023702/https://wiki.starsautohost.org/wiki/Interspace-10) — warp-10 destruction-risk mechanic for non-warp-10-safe engines.
- [Lesser racial traits — Stars!wiki](https://web.archive.org/web/2023/https://wiki.starsautohost.org/wiki/Lesser_racial_traits) — Cheap Engines, No Advanced Scanners, Improved Fuel Efficiency, No Ram Scoop Engines summaries.
- [Scanner Technology (help-file mirror) — wiki.starsautohost.org/wikinew](https://wiki.starsautohost.org/wikinew/hlp/index_web/148.html) — fourth-root multi-scanner combination formula (accessed via search-engine text extraction only; see Open Questions).
- [Stars! – Strategy Guide, PC, by Mars Jenkar / plague006 — GameFAQs](https://web.archive.org/web/20210615103545/https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043) — racial-trait engine/scanner/cargo side effects (Cheap Engines, No Advanced Scanners, Improved Fuel Efficiency, Inner Strength, Interstellar Traveler, Super-Stealth, Space Demolition, Packet Physics, Jack of All Trades hull/scanner specifics).
- [Stars! (1995) — TV Tropes, "VideoGame" mechanics page](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Stars1995) — ramscoop/conventional engine summary, Cheap Engines failure-to-launch framing, stargate/wormhole framing, cloak-vs-scanner framing.
- [Fuel Consumption Details — help.planets.nu](https://help.planets.nu/fuel-details) — cross-check-only secondary source (compatible fan reimplementation, not the original game) for the truncated fuel-use formula and the warp² "Max Distance Per Turn" constant.
- [STARS! The Premiere Space Strategy Game — Player's Guide (official manual scan, archive.org)](https://ia600508.us.archive.org/14/items/manual_Stars/Stars_djvu.txt) — UI-level phrasing for Warp Factor, Fuel Usage, Scanner Range, and Cargo gauge fields in the Fleet Waypoints tile; did not yield the underlying numeric formulas/tables.

Not used as sources (attempted but inaccessible during this research session): the live `wiki.starsautohost.org` site for pages with no available Wayback Machine snapshot (notably its `/wikinew/hlp/` help-file mirror generally, and any page named `Transferring_Cargo`, `Warp_Speed`, `Engine`, `Fleet_Movement`, or `Cargo`, none of which resolved to an archived snapshot or a live, non-challenge-gated page); `gamefaqs.gamespot.com` directly (blocked outside its Wayback Machine mirror).
