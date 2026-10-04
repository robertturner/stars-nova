# Planet Habitability, Population Growth, and Mineral Mining

## Overview

Three interlocking subsystems are covered here: (1) how a planet's raw environment (Gravity,
Temperature, Radiation) and a race's tolerance for each combine into a single **habitability
percentage**; (2) how that percentage, together with current population and a race's chosen growth
rate, produces **population growth per turn** — including the slowdown from overcrowding and the
special case of colonizing a new world; and (3) how **mineral concentration** and mine count combine
to determine how many kilotons of Ironium/Boranium/Germanium are mined per turn, and how
concentration depletes as a planet is worked over time.

All three systems share one property that matters for a clean-room implementation: they are
**per-planet, per-turn scalar formulas** with no hidden state beyond the numbers already visible on
the planet report (population, capacity, concentration, mine/factory counts) and the race's
customization sliders. The formulas below come from two kinds of community sources: (a) values
players could read directly out of the game's own help text (as mirrored by the Stars!AutoHost
wiki and the GameFAQs strategy guide), and (b) formulas the community reverse-engineered purely by
**observing external behavior** in testbed games — most notably the population-growth and
habitability formulas below, credited across multiple sources to Bill Butler and Jason Cawley, who
derived them by running controlled test games and curve-fitting the results, not by reading the
program's code. Where a documented table of in-game outcomes let us cross-check a formula
independently (the crowding-factor table and the remote-mining example both did), we verified the
match arithmetically and note the result inline.

This document supersedes the "exact per-planet habitability formula... is intentionally left to the
population-growth spec" open item noted in `race-traits.md`: a fully-specified formula was located
(§2 below), with the earlier document's "0.5 × 0.5 × 0.5" illustration confirmed by its own source
to be a simplified, inexact stand-in for it.

## Mechanics

### 1. Environment axes and per-axis value

A race's customization wizard sets a tolerance **range** (not just a single ideal point) on each of
three independent axes — Gravity, Temperature, Radiation — plus an option to be fully **immune** to
any one (or, at extra cost, two) of them. Each axis is scored **separately** before the three scores
are combined:

- A race can set a narrow or wide tolerance band anywhere along that axis's full physical range.
  Considering one axis in isolation, the resulting per-axis value for a planet a race can actually
  colonize falls between **40% and 100%**; planets outside the tolerance band on that axis produce a
  value as low as **-15%** on that axis alone. [wiki: Chapter 3: Building a Monster Race]
- **Immunity** to an axis pins that axis's contribution to its best possible value (equivalent to the
  planet always sitting at the ideal point for that axis), regardless of the planet's actual
  reading. One immunity **roughly doubles** overall habitability across the colonizable galaxy
  compared to a no-immunity race with otherwise similar tolerances, because it removes one full
  factor from the combination in §2; two immunities roughly double it again, but cost enough design
  points that races taking two struggle to also afford a growth rate above 12-13%.
  [wiki: Chapter 3: Building a Monster Race]

### 2. Habitability percentage formula

The best-documented derivation (credited to Bill Butler, obtained by statistical study of test-game
results rather than source inspection) expresses each axis as a **normalized distance from the
race's ideal center point toward the edge of its tolerance band**:

```
g = clicksFromIdealCenter_gravity     / totalClicksCenterToEdge_gravity
t = clicksFromIdealCenter_temperature / totalClicksCenterToEdge_temperature
r = clicksFromIdealCenter_radiation   / totalClicksCenterToEdge_radiation
```

so `g, t, r = 0` at the exact center of the race's tolerance band on that axis, and `1` at the outer
edge of what the race can tolerate at all. The overall habitability percentage is then:

```
x = max(0, g - 0.5);   y = max(0, t - 0.5);   z = max(0, r - 0.5)

Hab% = sqrt[(1-g)^2 + (1-t)^2 + (1-r)^2] / sqrt(3)  *  (1-x) * (1-y) * (1-z)
```

i.e. a normalized Euclidean "closeness to the ideal point" term, further penalized by an extra
multiplicative factor on any axis that is already past the halfway point out toward its edge (which
is what makes the falloff steeper than a simple average as a planet gets more marginal).
[starsfaq.com, "Guts of Planet Values", §4.11]

- The source explicitly notes this is an **approximation** ("the farther habs are from center, the
  less accurate the result... though errors are small, within a percentage point or two") and gives
  no derivation for how it behaves once one or more axes are *outside* the tolerance band entirely
  (`g, t,` or `r > 1`), i.e. for "red," uninhabitable-with-negative-value planets — see Open
  Questions.
- A separate, explicitly-labeled-as-inexact illustration from the wiki uses a plain product of the
  three axis fractions (e.g. 0.5 × 0.5 × 0.5 ≈ 12%) to build intuition about why immunity is
  valuable; its own author calls it "dead wrong" as an actual formula and it should **not** be used
  for implementation — it is included in the Sources list only for that historical reason.
  [wiki: Chapter 3: Building a Monster Race]

**Partial verification against the exported client.** Inspection of the executable's per-planet habitability computation confirms two structural claims above directly: the out-of-band penalty on a single axis is capped at exactly **15** (matching the "-15% on that axis alone" figure), and a fully immune axis is skipped from the combination entirely rather than merely clamped to its best value — both consistent with this section. However, the *in-band* combination the code performs is a **sequential chain of multiplications** of a running percentage-closeness accumulator by a per-axis linear-distance fraction — not a Euclidean/`sqrt(3)`-normalized distance as boxed above. The exact per-axis fraction was not fully decoded to a clean closed form (the code passes its running result through an unanalyzed helper before a final division), so this document cannot yet state a corrected formula with confidence — but implementers should treat the boxed `sqrt[...]/sqrt(3)` formula as **an approximation not confirmed to match the executable's exact arithmetic**, alongside its own source's caveat that it is explicitly approximate.

### 3. Population growth rate formula

Every race sets a single **base growth rate** slider (commonly 12%-20%; see the worked-example
discussion below), which is then modified per-planet by habitability and, above a threshold, by
crowding:

```
capPct = population / maxPopulationForThisPlanet

if capPct <= 0.25:
    popGrowth = population * growthRate * habValue

else:
    crowdingFactor = (16/9) * (1 - capPct)^2
    popGrowth = population * growthRate * habValue * crowdingFactor
```

(`growthRate` and `habValue` are both expressed as fractions, e.g. 0.15 and 0.90.)
[starsfaq.com, "Guts of Population Growth", §4.9 — credited to Jason Cawley, crediting Bill Butler]

This is independently corroborated by a documented table of the *effective* growth-rate multiplier
(as a percentage of the racial maximum) a **100%-habitability** world experiences at each capacity
level, which matches `crowdingFactor` above almost exactly at every listed point (e.g. `capPct=0.70`
→ `(16/9)*0.3^2 = 0.16` → 16%, `capPct=0.50` → `(16/9)*0.5^2 ≈ 0.444` → 44%,
`capPct=0.30` → `(16/9)*0.7^2 ≈ 0.871` → 87%):

| Capacity | Effective rate (% of racial max) |
|---|---|
| 10% | 100% |
| 20% | 100% |
| 30% | 87% |
| 40% | 64% |
| 50% | 44% |
| 60% | 28% |
| 70% | 16% |
| 80% | 7% |
| 90% | 2% |
| 100% | 0% |

[wiki: Chapter 6: Early Resource Management]

**Verification against the exported client — a different mechanism shape than boxed above.** The executable's per-turn population-growth calculation does not visibly compute a `crowdingFactor` multiplier at all. Instead it computes a **ceiling** — `cap = habValue * growthRateTrait / 100` (floored at a minimum of 10) — and then applies `popGrowth = min(population * growthRateTrait / 100, cap)`: that is, habitability sets an upper bound the raw population×growth-rate figure is clamped against, rather than a continuously-applied multiplicative factor combined with a separately-shaped crowding curve. Whether this reconstruction is numerically equivalent to the boxed formula for capacity percentages under 25% (where the boxed formula also has no crowding term) was not checked against the documented effective-rate table below; above 25% capacity the two descriptions diverge in shape and should not both be assumed correct. This is flagged as a live discrepancy rather than a resolved correction, since the exact scale/units of a couple of the record fields involved were not independently confirmed — see Open Questions.

**Negative-habitability decline — resolves an Open Question below.** The same turn-processing code's negative-habitability branch is fully recoverable: population decline is `max(1, (-habValue * capacityField) / 10)` colonists per turn, split into whole and fractional parts, with the fractional remainder (0-99) **persisted on the planet record and carried forward turn to turn** rather than discarded — a smooth, formula-driven decline curve, not an abrupt "dies after about a year" cliff. The positive-habitability branch uses the same fractional-carry mechanism (a previously undocumented per-planet field), with the growth rate additionally halved under a combination of a global flag and a per-race trait bit in some cases.

Practical race-design guidance on the growth-rate slider itself (from community strategy guides,
not the base game rules): **12%** is described as the lowest rate considered viable against human
opponents; **15%** is called the standard/default-feel rate; **17%** and **19%** are common
"monster race" choices (19% rather than a round 20% because the marginal design-point cost from 19
to 20 is disproportionately high); and races built around the Hyper Expansion trait commonly choose
a nominal **4%**, because that trait doubles the effective rate actually applied (to 8%).
[wiki: Chapter 2: Basic Race Design; wiki: Chapter 3: Building a Monster Race]

### 4. Colonization mechanics

- A brand-new colony starts with whatever population a player chooses to unload from a colonizing
  ship's cargo hold — there is no colony-size slider or fixed "seed size" set by the game itself.
  Community convention (not a hard engine rule we could source) treats **2,500 colonists** as the
  traditional minimal colonizer load for a standard race (or **1,000** for a race with Hyper
  Expansion, whose colonizer ships are cheaper/lighter), but explicitly warns against leaving a
  colony at that size: an under-populated colony is called out as an easy, low-cost target for other
  players, and the same source recommends immediately following up with **at least 20,000**, ideally
  **40,000+**, colonists to make the colony viable and defensible.
  [starsfaq.com, "How-to guide to expansion" by William Butler]
- A colony's habitability percentage is exactly the formula in §2, evaluated for the colonized
  planet; a freshly founded colony therefore starts growing immediately at
  `growthRate * habValue` (it starts at 0% of its own capacity, well under the 25% crowding
  threshold).
- The homeworld is a special case: it is generated at **100% habitability for its owning race** and
  a population cap of **1,000,000** colonists under a standard, unmodified race (before any trait or
  slider that changes maximum population), and starts with roughly **100 resources/turn** of income
  and a few hundred kilotons of each mineral already on the surface.
  [wiki: Chapter 6: Early Resource Management; starsfaq.com, "Playable AR races" by Jason Cawley]

### 5. Mineral concentration and mining

Each of the three minerals (Ironium, Boranium, Germanium) has its own **concentration** value on a
planet, read on a scale that behaves like a percentage but drives mining through a nonlinear
depletion curve rather than a flat percentage-of-100 yield:

- **Depletion curve.** For a mine (or mine-equivalent) operating at the standard 1.0 efficiency, the
  number of kilotons that can be extracted before concentration drops by exactly one point is
  `12500 / concentration` for concentration ≥ 27. Below concentration 27 this flattens to a linear
  **462 kT per point** down to concentration 4; the last few points are irregular: **1,000 kT** each
  for the 4→3 and 3→2 drops, and **2,000 kT** for the 2→1 drop. Concentration never drops below 1.
  Mine efficiency above 1.0 (a racial customization) scales these thresholds up proportionally (e.g.
  1.5× at 1.5 efficiency), extracting more kT per point of concentration drop, not more points per
  kT. [starsfaq.com, "Mineral Concentration And Mining" by Jason Cawley]
- **Continuous approximation.** For concentration ≥ 27, integrating the above gives a closed form
  for total kT recovered dropping from a starting concentration `Cstart` to an ending concentration
  `Cend`: `minerals ≈ 12500 * ln(Cstart / Cend)`. The same source notes the true (discrete,
  turn-by-turn) result runs slightly *higher* than this continuous estimate, more so at high mining
  rates, because concentration only steps down once per accounting event rather than continuously.
  [starsfaq.com, "Mineral Concentration And Mining" by Jason Cawley]
- **Per-turn mined amount.** We found no single source stating a general algebraic "kT mined this
  turn" formula in those terms, but two independently documented data points pin it down and agree
  with each other: (a) a fleet of exactly the maximum **4,000 mine-equivalents** mining a
  concentration-1 planet is documented to yield exactly **40 kT**, and (b) a race's default mine
  customization of "10 mines produce 10 kT of minerals" is understood to mean that figure is at
  100% concentration. Both are consistent with, and only with:

  ```
  mineralsMinedThisApplication = mineEquivalents * concentration / 100
  ```

  (`mineEquivalents` already folds in the race's mine-efficiency/mine-value setting.) Concentration
  is drawn down by this same amount using the depletion curve above *between* successive mining
  applications within the same turn when more than one mining source (e.g. several remote-mining
  fleets, or a planet's own mines plus an orbiting fleet) act in sequence — documented directly by
  the example of five fleets of 4,000 mine-equivalents each dropping a planet from concentration 100
  to 34 in a single turn. We treat the boxed formula as high-confidence but **not verbatim-sourced**
  — see Open Questions. [wiki: Remote Mining; wiki: Chapter 2: Basic Race Design]

- **Confirmed by inspection of the exported client — the per-mining-source engine, exact rounding
  rule, and depletion carry mechanism.** A single function drives every mining application (a
  planet's own mines, and each orbiting remote-mining fleet in turn), operating on a per-planet
  record that stores, for each of the three minerals independently: a current **concentration**
  byte (0-100) and a persisted **fractional-progress** byte (0-255) toward the next concentration
  point-drop. Per application:
  - The raw amount is `concentration * miningRate`, where `miningRate` is either a specific fleet's
    contribution (when previewing/applying one remote-mining fleet in a stack) or, for a planet's
    own mines, `numMines`-derived value additionally scaled by the race's "resources produced per
    10 mines" design setting (the same slot documented elsewhere as governing mineral output per
    mine; a default value of 10 makes this scaling a no-op, reproducing the boxed formula above
    exactly — the boxed formula is therefore confirmed as the *default-race special case* of a more
    general `mineEquivalents * concentration * mineOutputSetting / 1000` formula).
  - That raw amount is divided by 100, and the remainder (0-99) is **not simply truncated**: the
    executable draws one call from the same combined-multiplicative-congruential RNG already
    identified elsewhere in this document (range 0-99) and compares it against the remainder,
    rounding the mined amount **up** by one whole kT if the draw is less than the remainder and
    down (truncating) otherwise — i.e. unbiased stochastic rounding, where a true value of
    X.37 kT rounds up 37% of the time and down 63% of the time, rather than always truncating or
    always rounding to nearest. This stochastic-rounding step is itself gated by a global flag
    (an option bit distinct from the per-planet/per-race fields), so it may be togglable at the
    game level rather than universal; which in-game option this corresponds to was not identified.
  - Separately, and unconditionally, concentration depletion for the *committed* (non-preview) case
    uses the persisted fractional-progress byte as a fixed-point carry: each mineral's "kT needed to
    drop one more concentration point" is computed as `12500 * (fractionRemaining/256) /
    effectiveConcentration`, confirming the community-sourced **12500 constant** directly against
    the executable's own arithmetic. `effectiveConcentration` is the raw concentration value for
    concentration ≥ 25, but is floor-clamped to a small set of tiers for low concentration (25 for
    concentration 5-24, 10 for concentration below 5) rather than following a smooth curve down to
    concentration 1 — broadly consistent with this document's already-cited "flattens at low
    concentration" shape, though the exact breakpoints (5 and 25) do not exactly match the
    previously-documented community breakpoints (27, and the irregular 4/3/2/1 tail); this may
    reflect a different game version, or a different accounting layer, and is flagged as a partial,
    not exact, match. When the kT available this turn covers the full threshold, concentration drops
    by one point and the fractional-progress byte resets; otherwise the byte is updated to carry the
    partial progress forward to the next turn/application — the same "persist a fractional remainder
    on the planet record, carried turn to turn" pattern this document already documents for
    negative-habitability population decline (§3), now confirmed to be reused for mineral depletion
    as well, i.e. a general engine-wide technique rather than a one-off.
  - When several mining sources act at the same planet in the same turn (e.g. a planet's own mines
    plus one or more orbiting remote-mining fleets), the function recurses once per additional
    fleet, aggregating the four-value (resources-equivalent slot unused; three-mineral) result
    array — directly confirming this document's Example 5 mechanic (fleets applied in sequence,
    each depleting concentration before the next fleet's application is computed) at the
    implementation level, not just via the external worked-table match.
  - This same core mining function is also called directly from the production-queue/ship-design
    dialog code (the segment covered in `research-tech-tree.md`'s "Prerequisites" area) to compute a
    live mining-rate preview when a player is designing or reviewing a remote-mining fleet,
    confirming the preview and the actual turn-processing mined amount share one implementation.
- **Remote mining fleet cap.** A single fleet's remote-mining contribution is capped at 4,000
  mine-equivalents; any additional mining capacity stacked into the same fleet beyond that produces
  no extra minerals — splitting the same total mine-equivalents across more, smaller fleets always
  mines *less* in total than concentrating them (since each fleet mines in sequence against a
  progressively lower concentration). [wiki: Remote Mining]
- **Mine count, not mine efficiency, drives concentration depletion.** A strategy-guide source
  explicitly distinguishes the two: building more mines depletes concentration faster; making
  existing mines more *efficient* (more kT per mine) does not accelerate concentration loss on its
  own, though a planet with more (even if less efficient) mines will keep extracting *something*
  once concentration has bottomed out at 1, where a planet with fewer, more "efficient" mines
  extracts less overall. [GameFAQs Strategy Guide by Mars Jenkar / plague006]

**The surrounding summary popup, confirmed by inspection of the exported client.** The mining engine
above lives inside a small modeless popup window (a persistent child window, created once per
active player/race context alongside the main map and toolbar and thereafter only redrawn, never
recreated). Its draw, hit-test, and click-dispatch code was traced separately from the mining
engine itself:

- **What it displays.** The popup paints a bordered panel (using the same bevel-border drawing
  primitive documented in `client-ui-dialog-catalog.md`'s widget-primitives note) containing an
  object icon, one or more text lines built from string-resource templates (owner/object name,
  and at least one further status line whose exact wording could not be recovered), and a row of
  colored fill bars — consistent with a per-mineral (Ironium/Boranium/Germanium) mining/capacity
  gauge laid out in three evenly spaced columns. A dedicated relayout routine recomputes the
  column positions and two label widths from live text-extent measurements whenever the content
  changes, then invalidates only the affected sub-rectangles rather than the whole popup.
- **Hit-testing.** A dedicated hit-test function maps a client-area point to one of roughly a dozen
  named zones: the three mineral-bar columns (individually addressable), a combined "any mineral
  bar" zone (enabled only when more than one mining source — the planet's own mines plus one or
  more orbiting remote-mining fleets, or multiple co-located fleets/records — is actually present
  at the location, via a dedicated "more than one source here" check), an owner/name zone, and
  several further zones tied to a currently-selected stack member, an owner field, and a
  design/type field.
- **Click dispatch is mostly a launcher, not a handler.** For nearly every hit zone, the click
  handler does not act directly — it packages the target object's id, owner, and a small integer
  "mode" code (1 through at least 11) into a set of shared fields and then calls into **segment
  25's mode-switched hover/info popup** (the same popup-window mode dispatcher documented in
  `client-ui-dialog-catalog.md`'s "Toolbar, tooltip, and contextual popup" section) to actually
  present the resulting contextual view. This ties the two previously-separately-documented popup
  mechanisms together: this popup is one of the *triggers* for segment 25's mode-switched popup,
  supplying it with a target/owner/mode selected by exactly which zone was clicked. One hit zone's
  mode calculation references a design record at a stride of 147 bytes past a per-owner base
  address — matching, and cross-confirming, the 147-byte on-disk ship-design record layout already
  documented in `ship-design-and-components.md`.
- **Two hit zones are handled locally instead of deferring to segment 25.** Clicking the combined
  mineral-bar zone with the secondary (right) mouse button opens a dynamic popup menu (built via
  the same generic popup-menu builder documented for segment 25) listing every present mining
  source — the planet's own mines plus each orbiting remote-mining fleet in turn — and, once a
  source is chosen, invokes the mining engine directly in preview mode (uncommitted) to compute
  and apply that source's contribution; this is also where the production-queue/ship-design
  dialog's "live mining-rate preview" (noted above) and the popup's own preview share one code
  path. Clicking the same zone with the primary button instead cycles through every object
  co-located at that position (filtered by several owner/cloak/type bit tests) and makes the next
  one the active selection — the same "repeated click cycles through a stack" behavior already
  documented for the map's own contextual picker, independently re-implemented here for this
  popup's own click handling.
- **When it appears and disappears.** A separate routine (called from several other segments
  whenever the active selection changes — e.g. after a map click, an order-editor accept, or a
  production-queue edit) rebuilds the popup's caption from the currently selected planet, fleet, or
  a third, less-common selectable record kind (tracked in its own array, distinct from the planet
  and fleet-stack arrays used elsewhere in this popup; its exact real-world identity could not be
  confirmed), then unconditionally repaints the popup. It is explicitly **shown** (via an
  SWP_SHOWWINDOW-equivalent reposition call, docked near a fixed corner offset of a reference
  window) only when that third selectable kind is the current selection **and** it passes an
  owner-match test, a type/status-byte test, and a per-race field lookup returning one specific
  value; in every other case (including ordinary planet or fleet selection) the same routine
  explicitly **hides** it (SWP_HIDEWINDOW-equivalent). This means the popup documented here is not
  simply "always visible while an object is selected" — its visibility is gated by a fairly narrow,
  specific condition on the third object kind, while its *content* (caption/name) is still kept in
  sync for the ordinary planet/fleet selections that do not make it visible. This may indicate the
  window this document has been describing is one of several similarly-built summary popups sharing
  the same drawing/hit-test code, only one of which was traced to a concrete show/hide trigger in
  this pass — flagged here as an open question rather than asserted as certain.

## Worked Examples

The following are intended as unit-test seed cases. Examples 1 and 3 quote numbers taken directly
from a primary source's own worked table (so they are suitable as regression fixtures); Examples 2,
4, and 5 apply the documented formulas to illustrative inputs we chose ourselves, and are provided
to exercise the marginal/overcrowded/mining code paths, not as literally-sourced fixtures.

### Example 1 — Well-suited planet (sourced): homeworld growth curve

A Jack-of-All-Trades-style race with growth rate **R = 15%** on its **100%-habitability** homeworld,
starting from the historical 25,000-colonist seed population, produces (population under the 25%
crowding threshold throughout this stretch, so `popGrowth = population * 0.15 * 1.00`):

| Year | Population | ΔPop | % growth |
|---|---|---|---|
| 2400 | 25,000 | — | — |
| 2401 | 28,700 | 3,700 | 15% (≈14.8% actual, rounds to 15%) |
| 2402 | 33,000 | 4,300 | 15% |
| 2403 | 38,000 | 5,000 | 15% |

Applying the formula directly to the first step: `25000 * 0.15 * 1.00 = 3750`, close to the
documented `3,700` — the small gap is consistent with in-game rounding/truncation to whole
colonists each year, which this source's own methodology (reading numbers off the in-game planet
report turn by turn) would naturally reflect but our idealized formula does not.
[wiki: "Population Growth and Equivalent Value" by Lex Young, 1997]

**Independently re-confirmed by direct empirical testing (2026-09-04).** A freshly-created custom
race (Claim Adjuster PRT, no LRTs, left at the Custom Race Wizard's default growth-rate slider
position) was started on its homeworld in the actual running game (Stars! v2.70j/JRC3) at 25,000
population, 100% habitability, and advanced one turn: the planet report read exactly **28,700**
population afterward, a Δ of exactly **3,700** — matching this table's 2400→2401 entry
number-for-number, from a different (though compatible) patch version and a different primary
source than the 1997 wiki article this table was originally built from. Since this exact match
requires R=15% (as this worked example uses), it's also a strong indication that **15% is the
Custom Race Wizard's actual default growth-rate slider position** — previously only described by
community guides as the "standard/default-feel" choice, not confirmed as the wizard's literal
starting value.

### Example 2 — Marginal ("yellow") planet: illustrative

Take a planet at **45% habitability** for the colonizing race, capacity 450,000 (i.e. `0.45 *
1,000,000`), currently holding 10,000 colonists (well under the 25% = 112,500 threshold), and a
racial growth rate of 15%:

```
popGrowth = 10000 * 0.15 * 0.45 = 675
newPopulation = 10675
```

Contrast with Example 1's homeworld at the same 10,000-population starting point:
`10000 * 0.15 * 1.00 = 1500` — the 45%-habitability colony grows at 45% of the rate the homeworld
would, exactly as the multiplicative formula predicts.

### Example 3 — Overcrowded homeworld (sourced): crowding factor in effect

A 100%-habitability, 1,000,000-capacity homeworld at **70% capacity** (700,000 colonists), racial
growth rate 10%:

```
capPct = 0.70          (> 0.25, so crowding applies)
crowdingFactor = (16/9) * (1 - 0.70)^2 = (16/9) * 0.09 = 0.16
popGrowth = 700000 * 0.10 * 1.00 * 0.16 = 11200
```

This matches the sourced table entry exactly: "70% / 700,000 ... 1.6% / 11,200" — the table reports
the *combined* effective rate (`growthRate * crowdingFactor` = `10% * 16% = 1.6%`) applied to the
700,000 population. [wiki: Chapter 6: Early Resource Management]

A second point from the same table, at **100% capacity**: `crowdingFactor = (16/9) * 0 = 0`, so
`popGrowth = 0` — population plateaus at capacity rather than being documented as declining further
past 100% (we found no source describing an explicit population *loss* mechanic purely from
exceeding 100% of capacity on an otherwise-positive-habitability world; compare Open Questions on
negative habitability, which is a different case).

### Example 4 — Mineral concentration and mining: illustrative

A planet at **Germanium concentration 50** with **100 standard mines** (1.0 efficiency, no other
mining fleets in orbit that turn):

```
mineralsMinedThisTurn = 100 * 50 / 100 = 50 kT
```

Fifty kT is well short of the `12500 / 50 = 250 kT` needed to drop concentration by a full point, so
concentration remains 50 after this turn (any partial progress toward the next point-drop is,
per the source's own caveat, an accounting detail internal to the discrete simulation rather than
something the continuous approximation resolves).

### Example 5 — Remote mining depletion (sourced): large-fleet case

Five separate remote-mining fleets, each carrying exactly the fleet cap of **4,000
mine-equivalents**, mine the same Germanium-100 planet in sequence within one turn. Applying
`mineralsMinedThisApplication = mineEquivalents * concentration / 100` and then depleting
concentration via the continuous approximation `Cend = Cstart * exp(-mined / 12500)` between each
fleet (since each fleet acts on the concentration left by the previous one) gives, step by step:

| Fleet # | Concentration at start | kT mined this fleet | Concentration after (≈) |
|---|---|---|---|
| 1 | 100 | 4,000 | 72.6 |
| 2 | 72.6 | 2,904 | 57.6 |
| 3 | 57.6 | 2,303 | 47.9 |
| 4 | 47.9 | 1,916 | 41.1 |
| 5 | 41.1 | 1,644 | 36.0 |

This lands close to, but slightly above, the directly documented outcome of this exact scenario
("5 fleets of 4000 mine equivalents each reduce mineral concentration 100 to concentration 34 in
one turn") — the small gap (36 vs. 34) is exactly the direction and rough size predicted by the
source's own note that the discrete, real depletion runs a bit ahead of the continuous
approximation, especially at high mining rates. [wiki: Remote Mining; starsfaq.com, "Mineral
Concentration And Mining"]

## Open Questions / Uncertainties

- ~~**Negative-habitability ("red planet") behavior is not clearly documented.**~~ **PARTIALLY
  RESOLVED by inspection of the exported client** — see §3's "Negative-habitability decline" note
  above: decline is a smooth per-turn formula with a persisted fractional-carry byte, not an abrupt
  cliff, contradicting the secondary "dies after about a year" summary this document had flagged as
  unconfirmed. The exact real-unit meaning of the "capacity field" used in that formula (and
  whether it is the same capacity concept used elsewhere in this document) was not independently
  confirmed, so treat the formula's *shape* as confirmed and its exact field semantics as still
  open.
- **The core random-number generator has been identified in the exported client**: a combined
  multiplicative congruential generator (two independent seed-pair instances) in the style
  published by L'Ecuyer in 1988, used game-wide for every "roll a random outcome" decision this
  document and others reference (e.g., random turn events, AI decision jitter). This does not by
  itself resolve any formula above, but is noted here since several "random" behaviors referenced
  throughout this document's sources ultimately reduce to this one generator.
- ~~**The boxed per-turn mining formula (`mineEquivalents * concentration / 100`) is a synthesis, not
  a verbatim-quoted formula.**~~ **RESOLVED by inspection of the exported client** — see §5's new
  "Confirmed by inspection" note above: the executable computes exactly this figure (as the
  default-race special case of a more general formula that also folds in the race's mine-output
  design setting), and additionally reveals that the fractional remainder is not truncated but
  stochastically rounded via the game's RNG, and that concentration depletion is tracked with a
  persisted fractional-progress byte per mineral rather than recomputed from scratch each turn.
- **No confirmed hard, engine-enforced minimum population to found a colony.** Community guidance
  (2,500 / 1,000-for-Hyper-Expansion colonists) is a strategic convention, not a stated rule; we did
  not find a source confirming whether the game engine enforces any minimum above "more than zero
  colonists in the dropping ship's cargo."
- **The exact mapping from a planet's raw Gravity (g)/Temperature (°C)/Radiation (mR) reading to the
  0-100 "click" scale used in §2's `g, t, r` terms** (i.e. the real-unit bounds of the galaxy-wide
  possible range on each axis, and whether the click scale is linear or logarithmic per axis) was
  not confirmed by a source we could access in this pass; widely-repeated figures (e.g. roughly
  0.12g-8.00g for Gravity, roughly -200°C to 200°C for Temperature, 0-100mR for Radiation) appear
  across secondary Stars! tools and calculators but we could not trace them to an equally
  authoritative primary description alongside the §2 formula itself, so we omit them as asserted
  fact pending corroboration.
- **Rounding/truncation rule for population growth is still unconfirmed** — but a closely related
  mechanism is now confirmed elsewhere. Example 1's small documented-vs-formula gap (3,700 vs.
  3,750) is consistent with per-turn integer truncation of population, but no source states the
  exact rounding rule used internally for growth specifically. Inspection of the exported client
  did confirm (§5) that the *mining* pipeline uses unbiased stochastic (RNG-drawn) rounding of
  fractional kT amounts rather than plain truncation, gated by a global flag; the population-growth
  code path was not re-inspected in this pass to check whether it reuses the same technique, so this
  remains open for growth specifically, but the discovery makes stochastic rounding a plausible
  answer worth checking first.
- **wiki.starsautohost.org served an anti-bot interstitial when fetched directly during this
  research session**; every wiki page cited above was instead retrieved through Wayback Machine
  snapshots of the same URLs, which should mirror the live content but could theoretically lag a
  live edit.

## Sources

- starsfaq.com, "Guts" FAQ, §4.9 "Guts of Population Growth" and §4.11 "Guts of Planet Values" —
  http://www.starsfaq.com/advfaq/guts2.htm
- starsfaq.com, Stars!-R-Us article, "Mineral Concentration And Mining" by Jason Cawley —
  http://starsfaq.com/articles/sru/art211.htm
- starsfaq.com, Stars!-R-Us article, "How-to guide to expansion" by William Butler —
  http://starsfaq.com/articles/sru/art205.htm
- starsfaq.com, Stars!-R-Us article, "Playable AR races" by Jason Cawley —
  http://starsfaq.com/articles/sru/art21.htm
- Stars!AutoHost community wiki, "Remote Mining" —
  https://wiki.starsautohost.org/wiki/Remote_Mining (accessed via Wayback Machine snapshot)
- Stars!AutoHost community wiki, "Chapter 2: Basic Race Design" —
  https://wiki.starsautohost.org/wiki/Chapter_2:Basic_Race_Design (via Wayback Machine snapshot)
- Stars!AutoHost community wiki, "Chapter 3: Building a Monster Race" —
  https://wiki.starsautohost.org/wiki/Chapter_3:Building_a_Monster_Race (via Wayback Machine
  snapshot)
- Stars!AutoHost community wiki, "Chapter 6: Early Resource Management" —
  https://wiki.starsautohost.org/wiki/Chapter_6:Early_Resource_Management (via Wayback Machine
  snapshot)
- Stars!AutoHost community wiki, "'Population Growth and Equivalent Value' by Lex Young 1997
  v2.6/7" — https://wiki.starsautohost.org/wiki/%22Population_Growth_and_Equivalent_Value%22_by_Lex_Young_1997_v2.6/7
  (via Wayback Machine snapshot)
- Mars Jenkar / plague006, *Stars! Strategy Guide* v1.11, GameFAQs (mine-count-vs-efficiency
  discussion) — https://gamefaqs.gamespot.com/pc/198797-stars/faqs/41043
- (Referenced only to note it is explicitly non-authoritative) Stars!AutoHost community wiki,
  simplified "0.5 × 0.5 × 0.5" habitability illustration in "Chapter 3: Building a Monster Race" —
  same URL as above; the source itself calls this simplification "dead wrong" as a literal formula.

Note on access: wiki.starsautohost.org presented an automated anti-bot verification page when
fetched directly during this session; all wiki content cited above was instead retrieved through
Wayback Machine snapshots (https://web.archive.org/web/*/https://wiki.starsautohost.org/wiki/*) of
the same pages.
