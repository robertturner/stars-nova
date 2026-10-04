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

**Verification against the exported client — now fully resolved (2026-09-24 pass).** The habitability function itself was located directly (`FUN_1048_490e`, segment 10, `stars.exe.export.c:27571`-`27642`) by tracing forward from the population-growth driver (see §3 below), rather than by searching segment 11 as earlier passes had guessed. It confirms two structural claims from this section directly: the out-of-band penalty on a single axis is capped at exactly **15** raw points, and a fully immune axis (a per-axis, per-race "high edge" byte holding a sentinel of -1) is skipped from the combination entirely. It also resolves the "unanalyzed helper" mystery: there is no separate helper function at all — the in-band per-axis reduction is inlined directly in `FUN_1048_490e`'s own body, applied only when a planet's reading is more than halfway from the race's ideal center point toward the edge of its tolerance band on that axis (exactly the "> 0.5" condition the boxed community formula also uses for its `x`/`y`/`z` terms), and its exact closed form is now recovered:

```
d = |planetAxisValue - idealCenter|         (raw 0-100 click units)
h = idealCenter-to-edge distance on the relevant side (toward planetAxisValue)
g = d / h                                    (0 at center, 1 at the tolerance edge)

axisMultiplier = 1                           if g <= 0.5
axisMultiplier = 1.5 - g   (= (3h - 2d) / 2h) if g > 0.5
```

**Correction (later pass, found while independently re-deriving `race-traits.md`'s point-cost formula): this closed form is incomplete.** A separate investigation into `FUN_1048_490e` (this exact function) for an unrelated reason found it also performs a hidden square-root step using two further constants (1/3 and 0.9, at data locations Ghidra did not surface as named symbols) that the per-axis `axisMultiplier` combination described below does not account for — confirmed because `race-traits.md`'s independently-rebuilt formula only reproduced 18 live in-game readings correctly once this square-root term was included. The exact way this term combines with the per-axis product below was not re-derived in this pass; treat the formula immediately below as the confirmed per-axis reduction shape, but **incomplete as the full habitability calculation** until the square-root term is folded in — see `race-traits.md` §1b for the fuller, validated reconstruction this correction is based on.

The three axes' multipliers (one per non-immune axis; immune axes are skipped entirely rather than contributing a multiplier) are combined by a **running product seeded at 100%** — confirming the "sequential chain of multiplications" already documented, now with the exact per-axis factor. This is a genuinely different shape from the boxed `sqrt[...]/sqrt(3)` community approximation: at the tolerance edge (`g=1`) a single marginal axis contributes a **0.5×** multiplier to the product, not a value approaching 0 — a materially gentler falloff than the Euclidean approximation implies. The **out-of-band penalty is additive across axes, not a flat -15 cap on the whole planet**: each out-of-band axis independently contributes `min(distanceOutsideBand, 15)` to a summed penalty, so a planet with two or three axes simultaneously out of tolerance can reach habitability values below -15 (down to -45 if all three axes are maximally out of band) — refining the "-15% on that axis alone" community figure, which is correct only for the single-axis case. Implementers should now treat this reconstructed formula, not the boxed `sqrt[...]/sqrt(3)` one, as the authoritative in-band combination; the boxed formula remains useful only as the community's documented (and self-acknowledged approximate) mental model.

**One adjacent question was resolved while tracing unrelated galaxy-generation code this pass, however: raw Gravity/Temperature/Radiation are not stored as separate physical-unit fields at all.** The galaxy generator's per-planet environment initialization (`FUN_1078_1334`, segment 16, the same routine documented in `new-game-setup.md` §3/§5a) generates each axis directly as a single byte in the ~1-100 range, written straight into the planet record at one fixed byte offset per axis (record offsets `12` and `13`, each mirrored into a small cache byte elsewhere in the record, plus offset `17`/cached at `14` for the third axis — see the correction immediately below for the exact per-axis roll shape). No separate "real-world" Gravity-in-g / Temperature-in-°C / Radiation-in-mR field exists anywhere in this code path — the 0-100 "click" value *is* the planet's actual stored environment reading. This resolves this document's own Open Question about whether a physical-unit-to-click mapping exists to trace: there is no such mapping in the executable, because there is no physical-unit storage to map from. The commonly-repeated real-world bounds (0.12g-8.00g, -200°C to 200°C, 0-100mR) are therefore purely a **display-layer convention** for presenting the 0-100 click value to the player, not a game-internal representation — implementers can treat the 0-100 click scale as authoritative and skip modeling a separate physical-unit axis entirely.

**Correction (later pass, cross-checking `race-designer-ui-and-availability.md` item 38): the "two summed 0-44 rolls plus a flat 31" formula quoted above is the wrong routine — it describes mineral concentration, not the environment axes.** Re-reading `FUN_1078_1334` line by line this pass shows that roll shape (`stars.exe.export.c:50537`-`50544`), and the "maximum minerals" flag it is gated behind (`DAT_1128_0080 & 1`, checked at `50533`), belong to the per-star record's mineral-concentration loop at relative offsets `9`-`11` (`50530`-`50566`) — the exact same generator `new-game-setup.md` §3 already documents, independently and correctly, as "ordinary (non-home-world) planets['] ... mineral concentration: the sum of two independent 0-44 random rolls plus a flat 31." The previous pass found the right function but attributed the wrong field group within it to the environment axes.

The **actual** environment-axis rolls sit a few lines earlier in the same function (`50496`-`50514`) and use a different shape entirely: two axes (record offsets `12` and `13`) are each `roll(0,89) + 1` immediately incremented by a further `roll(0,9)` — a discrete-trapezoid distribution (ramp/flat-plateau/ramp, ramp width 10) over the range 1-99, not a flat/uniform one; the third axis (offset `17`, cached at `14`) is a single `roll(0,98) + 1`, a plain uniform draw over 1-99. There is no "clamped down when it would land above 89" step for these axes at all — that description, too, belonged to the mineral-concentration formula (whose own out-of-band adjustment, gated on a *different* axis's cached value exceeding 89, is a real but separate mechanic not analyzed further here). See `race-designer-ui-and-availability.md`'s "Scope and confidence" section for the full write-up, including the exact match this trapezoid/uniform shape makes against that document's own `w(x)`/`C_middle`/`C_uniform` display formula — this cross-check is now resolved as a match, not merely attempted.

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

**Function located and verified in full (2026-09-24 pass) — the earlier "cap-based" reconstruction is confirmed wrong, and the boxed community formula above is confirmed correct.** The per-turn population growth/decline function is `FUN_1038_47d0` (segment 8, `stars.exe.export.c:20925`-`21060`), taking a planet-record pointer and a commit-vs-preview flag (0 = compute only, non-zero = write the result back to the planet). It is invoked once per owned, non-empty planet by a dedicated per-planet sweep, `FUN_10b8_30ca` (segment 24, `stars.exe.export.c:78387`-`78449`, called with the commit flag set), which is itself called once per turn from a segment-24 economic-pass hub (`FUN_10b8_0000`, `:76294`) invoked directly from the master turn-generation routine at `stars.exe.export.c:72169` — i.e. the calculation genuinely lives in segment 8 as `code-coverage-report.md`'s segment-8 finding (4) already asserted, but the per-turn driver that walks every planet and commits the result lives in segment 24, a segment not previously connected to this subsystem. The same sweep also re-evaluates habitability afterward purely to choose between two population-change notification messages, tying this driver directly into the message-record store documented in `client-ui-dialog-catalog.md`. Two other functions supply the terms:

- **Habitability** comes from `FUN_1048_490e` itself (§2 above) — the growth/decline function calls it directly with the planet pointer and owner-race index, so `habValue` in the formulas below is exactly the signed -45..100 value §2 now documents, not a separate quantity.
- **Growth-rate slider** comes from a small accessor, `FUN_10e0_445a` (`stars.exe.export.c:95498`-`95508`): it reads the race's growth-rate percentage directly off the per-race record (offset `0x5b` into the 192-byte slot documented in `new-game-setup.md` §5) and **doubles it when the race's PRT equals 0 (Hyper Expansion)** — a direct code-level confirmation of `race-traits.md`'s "Colony growth rate is 2x the slider value" claim, previously sourced only from community material.
- **Population capacity** (the planet's max supportable population, used only by the positive/growth branch) comes from `FUN_1048_4a8e` (`stars.exe.export.c:27646`-`27696`): for every PRT except Alternate Reality it computes `habValue * 100` scaled to a true colonist count (so a 100%-habitability world under a standard race yields exactly the documented **1,000,000**), then applies PRT/trait modifiers — **halved for Hyper Expansion** (confirming "`maximum population per planet is halved`" at the code level), **+20% for Jack Of All Trades (PRT 9)** and **+10% under a specific race-trait bit (bit 9 of the generic trait-bit accessor)**, both previously undocumented, plus a distinct, not-fully-traced formula for Alternate Reality (PRT 8) that reads from a per-race lookup table instead of using habitability at all. `FUN_1048_4a8e` is a straightforward candidate worth cross-referencing from `race-traits.md`'s own AR-capacity discussion in a future pass.

  **Both follow-ups resolved this pass (2026-09-25).** Reading `FUN_1048_4a8e` in full:

  - **The +10% bonus's gating trait is Inner Strength (PRT 4).** The bonus is applied unconditionally to every PRT's result (including Alternate Reality's, after its own branch below) via `iVar3 = FUN_10e0_226e((int)puVar2, 9); if (iVar3 != 0) local_6 = local_6 + local_6/10;` (`:27691`-`27694`) — bit 9 of the *generic per-race ability bitmask* read by `FUN_10e0_226e` (the accessor already established project-wide, distinct from the PRT-index accessor `FUN_10e0_222c`). Cross-referencing bit index 9 against `ship-design-and-components.md` §5/§14: that document independently ties this exact bit to **Inner Strength** by two convergent findings — the Bomb component category (`0x0080`) blocks exactly the 5 bomb subtypes Inner Strength is documented to lose ("Smart/Neutron/Enriched-Neutron/Peerless/Annihilator") when bit 9 is set, and the same bit recurs identically in the regular-hull category's exclusions. So: **Inner Strength races get a flat +10% population-capacity bonus on every planet**, stacking with (applied after) any PRT-specific modifier, including Alternate Reality's table lookup below — a previously undocumented part of Inner Strength's trait package.
  - **Alternate Reality's (PRT 8) capacity formula reduces to a lookup-table chain, not a habitability-based computation at all.** When `FUN_10e0_222c(race, 0xe) == 8` (owner is AR), the function skips habitability entirely and instead (`:27664`-`27670`): (1) requires the planet snapshot's owner field to match the queried race and a status-flags byte to have bit `0x2` set (read via the same generic planet-snapshot copy, `FUN_1038_0358`, used at the top of the function — otherwise capacity is 0, i.e. an AR planet with no qualifying orbital base has no population capacity); (2) fetches a per-race pointer, `perRaceDesignArray = *(long*)(race*4 + 0x10c)` — a fixed, low-address table of one design-array pointer per race; (3) indexes it by the planet's current base-design slot number (a byte off the planet snapshot, masked to `& 0xf`) at a stride of **147 bytes (`0x93`)** — exactly the 147-byte on-disk design-record size independently confirmed in `ship-design-and-components.md` §1; (4) reads a 4-byte value at **offset 0** of that design record — matching, byte-for-byte, the "every design record stores a hull-type index at a fixed offset" pattern `ship-design-and-components.md` §14 already established from `FUN_1008_5118`'s 40+ call sites; (5) uses that hull-type index into a **second, distinct table** — a plain array of 4-byte values at fixed base `0x80c` (`hullTypeIndex*4 + 0x80c`) — to get the raw capacity. In short:
    ```
    if not (planet.owner == race and planet.statusFlags & 0x2):
        capacity = 0
    else:
        designPtr   = perRaceDesignArray[race] + (planet.currentBaseSlot & 0xf) * 147
        hullType    = *(int*)(designPtr + 0)              // hull-type index stored in the design record
        capacity    = perHullPopCapTable[hullType]          // 4-byte table at fixed base 0x80c, stride 4
    capacity += capacity/10   if Inner Strength (bit 9, above)
    ```
    This is a materially different mechanic from every other PRT: AR capacity is fixed per the **type of orbital base/habitat currently in orbit** (looked up from a dedicated, apparently hull-indexed table, separate from the main 143-byte hull-stats table `ship-design-and-components.md` §14 documents), not derived from the planet's environment at all — consistent with AR's flavor of living in orbital habitats rather than on the planet surface. The `0x80c` table's actual per-hull-type capacity *values* were not extracted this pass (that would require reading static data out of the binary's data segment, which this pass did not attempt); the lookup *mechanism* is now fully traced and concrete. The status-flags bit `0x2` gating the whole branch (plausibly "has a base/orbital habitat present") and the exact identity of the copied planet-snapshot fields (`local_40`=owner, `local_3d`=status flags, `local_16`=current base-design slot, inferred from their relative stack offsets within `FUN_1038_0358`'s copied block, not independently cross-confirmed against that function's 30+ other call sites) are offered with high but not absolute confidence.

**The actual formula, confirmed structurally identical to the boxed community formula above (not the previously-recorded "cap" reconstruction, which was a transcription/field-identity error from an earlier pass that never actually located this function):**

- **Negative habitability (decline):** `declineRaw = max(1, |habValue| * population / 10)`, then split into a whole-colonist part (`declineRaw / 100`) and a fractional remainder (`declineRaw % 100`, 0-99) that is *subtracted* from a persisted per-planet fractional-carry byte (planet-record offset `0x14`), borrowing a whole colonist from the total whenever the byte would go negative. This resolves the earlier open question over "capacityField"'s identity in this branch with certainty: **it is literally the planet's current population**, not a separate maximum-population field — decline scales with how many colonists are already there, not with how many the planet could hold.
- **Non-negative habitability (growth):** `raw = growthRate * habValue`, halved when a global option-flag bit (`DAT_1128_078a & 2`) and a specific per-race trait-flags bit (byte offset `0x14` into a secondary per-race record, bit `0x4`) are *both* set — confirming the previously-noted-but-unattributed "growth rate additionally halved under a combination of a global flag and a per-race trait bit" mechanism, though the flag/bit's in-game label is still unidentified. If `population > capacity / 4` (i.e. `capPct > 0.25`, matching the boxed formula's threshold) and `population < capacity`, `raw` is further multiplied by `(1000 - round(capPct * 1000))^2 / 562500` — and **562500 is exactly `1,000,000 * 9/16`**, i.e. this expression is arithmetically identical to `(16/9) * (1 - capPct)^2`, the crowding factor from the boxed community formula and the effective-rate table below, confirmed to the exact constant.

  **The `population >= capacity` branch — resolved this pass (2026-09-25), and it is a decline, not a residual growth.** Reading the rest of `FUN_1038_47d0`'s crowding block (`:21004`-`21029`) line by line: let `capPct1000 = floor(population * 1000 / capacity)` (already computed for the sub-cap branch above; here it is `>= 1000` since population has reached or passed capacity). The function first does a 32-bit "is `population < capacity + 10`?" comparison built from split high/low-word arithmetic (`:21005`-`21014`) and returns **exactly 0** (no change at all) whenever that holds — i.e. population sitting anywhere from exactly at capacity up to 9 colonists over it produces flat zero growth, matching Example 3's "population plateaus at capacity" note. Once population reaches **capacity + 10 or more**, it computes `n = 99 - (capPct1000 / 10)` (truncating integer division — `n` is always `<= -1` here) and clamps it to a floor of **-300** (`if (n < -300) n = -300`, the exact "clamped to a small fixed magnitude" this document had already flagged but not quantified), then the same "multiply by 4 via extended-precision doubling" path the sub-cap branch also uses (`LAB_1038_4a04`) feeds `4*n` into the shared `population * raw / 10000` closer. Since `n` is negative, so is the final result — **the population actually shrinks**, not grows, once it clears capacity by 10+:
    ```
    capPct1000 = floor(population * 1000 / capacity)     // population >= capacity, so capPct1000 >= 1000

    if population < capacity + 10:
        popChange = 0
    else:
        n = max(99 - (capPct1000 / 10), -300)             // n <= -1, floor-clamped at -300
        popChange = population * n / 2500                  // = population * (4*n) / 10000; negative -> decline
    ```
    At the clamp (`n = -300`, reached once population is roughly **4x** capacity or more), `popChange = -0.12 * population` — a flat **-12% of population per turn**, the mechanism's maximum decline rate; just past the `capacity+10` threshold the decline is correspondingly tiny (a handful of colonists). This corrects the document's earlier guess (a "small, asymptotically-shrinking residual *growth*... with an outright zero once population is sufficiently at or past capacity") on the sign: there is no further growth past capacity at all — the engine instead applies a smooth, capacity-overshoot-proportional population *penalty*, presumably meant to handle planets pushed over their capacity by an external event (e.g. a capacity-reducing habitability change, or colonists dumped in from elsewhere) by easing them back down, mirroring the same fractional-carry-byte mechanism (offset `0x14`) used everywhere else in this function.

  **The growth-halving flag/trait's in-game label — attempted this pass (2026-09-25), best treated as a well-evidenced negative result.** Re-reading the gate at `:20982`-`20985` precisely: `(DAT_1128_078a & 2) != 0` **and** `(((undefined1*)&DAT_1128_5a16)[race*0xc0] & 4) != 0`.
  - The per-race byte is at **`DAT_1128_5a16`, offset 0 of its own 192-byte-stride (`0xc0`) per-race array**, not "offset `0x14`" as this document previously said — `DAT_1128_5a16`'s address sits exactly `0x54` bytes past the main per-race record base `DAT_1128_59c2` (itself stride-`0xc0`), so it is very likely field offset `0x54` of that same record, addressed here through a byte-typed alias Ghidra gave it rather than through a genuinely separate structure. More importantly, this exact table (traced independently while investigating `fleet-movement-scanning-cargo.md`'s wormhole mechanic, `FUN_1118_0784`) is **already documented there as a reused per-race scratch/flags byte serving several unrelated purposes** — a duplicate-name/"already processed" flag at bit `0x1` (used e.g. at `stars.exe.export.c:8040`, `20107`, `44609`), an unidentified gas-tolerance-threshold selector at bit `0x2`, and (now) this growth-halving gate at bit `0x4`; that document explicitly found it is *not* the confirmed, stable PRT/ability-bit accessor (`FUN_10e0_222c`/`FUN_10e0_226e`, offset `0x3e` of the race record) used for every named trait elsewhere in this project. The same bit `0x4` also gates unrelated-looking logic in segment 23's fleet-task processing (`stars.exe.export.c:73381`, `76404`), in a context comparing the current master turn counter (`DAT_1128_0082`, confirmed elsewhere in this project — see `tutorial-system.md` — to be the turn counter) against the fleet owner's race index mod 8, which reads like a per-turn message/reminder throttle rather than a race-design trait check. **Conclusion: this bit does not behave like a stable, named racial trait at all** — cross-referencing it against every other identified use of the same table turns up only transient, per-turn/per-session scratch state, so no in-game trait or option label could be matched to it. This should be read as revising, not just leaving open, the earlier "trait-flags bit" framing.
  - The global flag, `DAT_1128_078a & 2`, was cross-checked against every other call site in the executable (60+ in total). It is **not a persistent, player-configurable game-setup option** — every site that *sets* it (rather than merely testing it) brackets it tightly around one specific operation and clears it again immediately afterward: the per-player ready-status poll `FUN_1020_4e06` sets it for the duration of its own scan loop (`stars.exe.export.c:9217`, cleared at `:9275`), and the turn-file load/generate command handler for menu commands `0x464`-`0x466` sets it around the "generate the next turn" step specifically (`:5395`, cleared at `:5403`). This is consistent with the bit acting as an internal **"a turn/turn-file is actively being generated or loaded right now"** marker, not a checkbox — which would also explain why it could not be matched to any documented in-game option. This exact flag also gates the mining engine's stochastic-rounding step (§5's item 5, resolved below) via the identical `DAT_1128_078a & 2` test — the same conclusion applies there.

This fully resolves the earlier "algebraic mismatch" concern: the previously-recorded `cap = habValue * growthRateTrait / 100 (floored at 10)` / `popGrowth = min(population * rate/100, cap)` description did not correspond to any code found this pass and should be treated as superseded. The boxed community formula's *shape* — `population * growthRate * habValue`, with a `(16/9)(1-capPct)^2` crowding multiplier above 25% capacity — is now confirmed correct at the arithmetic level, including the exact crowding constant.

**Negative-habitability decline — resolves an Open Question below.** The same turn-processing code's negative-habitability branch is fully recoverable: population decline is `max(1, (-habValue * population) / 10)` colonists per turn (see above for the corrected field identity), split into whole and fractional parts, with the fractional remainder (0-99) **persisted on the planet record and carried forward turn to turn** rather than discarded — a smooth, formula-driven decline curve, not an abrupt "dies after about a year" cliff. The positive-habitability branch uses the same fractional-carry mechanism (planet-record offset `0x14`), with the growth rate additionally halved under a combination of a global flag and a per-race trait bit in some cases.

**Rounding technique resolved: population growth/decline does *not* reuse mining's RNG-based stochastic rounding (item 30, resolved with a negative result).** Both branches use the deterministic fractional-carry-byte mechanism described above — accumulate the 0-99 remainder into a persisted byte, carry/borrow a whole colonist when it overflows/underflows — with no RNG call anywhere in `FUN_1038_47d0`. This is a different (and simpler) technique from the mining pipeline's RNG-drawn stochastic rounding (§5): mining rounds a single application's fractional kT probabilistically each time, while population growth/decline defers its fractional colonist deterministically to the following turn(s) until it accumulates to a whole colonist. Both are real, confirmed techniques in this codebase; they are simply used in different subsystems.

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

    **Gate located this pass (2026-09-25).** The mining engine itself is `FUN_1028_3a74`
    (segment 6, `stars.exe.export.c:13483`-`13629`); the stochastic-rounding step above is at
    `:13554`-`13561`, and its gate is exactly `(local_e != 0) && ((DAT_1128_078a & 2) != 0)`
    (`:13556`) — **the identical global bit** that §3 above traces as (one half of) the
    population growth-rate-halving gate. Cross-referencing every other use of this bit
    (§3's write-up, immediately above) shows it is not a persistent, player-facing game option at
    all: it is set only transiently, bracketing specific internal operations (the per-player
    ready-status poll and the "generate the next turn" step of the turn-file load/save command
    handler) and cleared again right after, behaving as an internal "a turn is actively being
    generated/committed right now" marker. That resolves the "which in-game option" question with
    a negative-but-explained result: there is unlikely to be a matching in-game option, because
    this bit is not one — both the mining engine's stochastic rounding and (in combination with a
    separate per-race scratch bit) population growth's rate-halving are gated on the same internal
    turn-commit marker rather than on anything a player can toggle in a setup dialog.
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

    **Breakpoint mismatch re-checked this pass (2026-09-25) — the code-derived thresholds stand,
    unchanged.** Re-reading the exact clamp in `FUN_1028_3a74` (`stars.exe.export.c:13575`-`13584`)
    line by line reproduces the same result as before: for the current byte concentration `c`
    (0-100), `effectiveConcentration = c` when `c >= 25`, `= 25` when `5 <= c < 25`, and `= 10` when
    `c < 5` (the `c >= 101` branch the code also handles is unreachable for any real, in-range
    byte value and looks like a defensive clamp rather than a real fourth tier). This is a clean,
    unambiguous two-breakpoint (5 and 25) stair-step, not the community-documented three-point
    (27, then the irregular 4/3/2/1 tail) curve. `extracted-game-data/dynamic-strings.txt` and
    `extracted-game-data/message-strings.txt` were both searched this pass for any breakpoint- or
    depletion-related text (`concentration`, `deplet`, and the literal numbers `25`/`27`) that might
    explain the discrepancy; neither file contains any numeric or descriptive text about mining
    depletion tiers at all (`dynamic-strings.txt`'s mineral-related entries are all cosmetic labels
    like "Mineral Concentration View" and "Surface Mineral View", and no message in
    `message-strings.txt` mentions concentration breakpoints). This search therefore does not
    resolve the mismatch either way — it only confirms there is no string-table evidence to appeal
    to. The mismatch is left as previously flagged: the community's 27/4/3/2/1 figures may describe
    a different game version, a different accounting layer, or simply be wrong; this executable's
    own arithmetic is unambiguous and now re-confirmed twice.
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

  **The second line's wording — resolved this pass (2026-09-25), for the caption specifically.**
  `FUN_1028_328e` itself (the caption-rebuild routine cited below) loads two string-table entries
  at its very start (`stars.exe.export.c:13047`-`13049`): id `0x362` (decimal 866, text **"Deep
  Space"**, per `extracted-game-data/dynamic-strings.txt` line `867:866` — used as the fallback
  caption when nothing is selected) and id `0x554` (decimal 1364, text **" Summary"**, leading
  space included, per that file's line `1365:1364`). The routine then string-reverses and
  concatenates the resolved object/owner name with this " Summary" suffix (`:13073`-`13133`)
  before handing the result off as the popup's window caption. So the popup's title reads exactly
  **"`<object/owner name>` Summary"** (e.g. a planet named "Earth" would caption the popup "Earth
  Summary"). This concretely answers "the exact wording of ... a status line" for the caption
  text specifically; it does not establish whether the doc's original "further status line"
  referred to this caption or to a separate line drawn inside the popup body by the mining-bar
  relayout routine mentioned above (that routine's own text was not traced this pass).
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
- **When it appears and disappears.** A separate routine (`FUN_1028_328e`, segment 6,
  `stars.exe.export.c:13023`-`13163`, called from several other segments whenever the active
  selection changes — e.g. after a map click, an order-editor accept, or a production-queue edit)
  rebuilds the popup's caption from the currently selected planet, fleet, or a third, less-common
  selectable record kind (tracked in its own 18-byte-stride array, `_DAT_1128_15fe`, indexed by
  `DAT_1128_4990`, distinct from the planet and fleet-stack arrays used elsewhere in this popup),
  then unconditionally repaints the popup. It is explicitly **shown** (via an SWP_SHOWWINDOW-equivalent
  reposition call, docked near a fixed corner offset of a reference window) only when that third
  selectable kind is the current selection **and** it passes an owner-match test, a type/status-byte
  test, and a per-race field lookup returning one specific value; in every other case (including
  ordinary planet or fleet selection) the same routine explicitly **hides** it (SWP_HIDEWINDOW-equivalent).

  **The third record kind's identity — a well-evidenced, though not 100%-certain, resolution this
  pass (2026-09-24): minefields.** Three independent findings converge on this: (1) the "per-race
  field lookup returning one specific value" gate mentioned above resolves, on inspection, to the
  selected record's owner having race **PRT 5 — Space Demolition**, this project's already-documented
  mine-laying specialist trait, evaluated via the same generic PRT accessor (`FUN_10e0_222c(...,0xe)`)
  used throughout this project; (2) the selection-mode value that activates this third kind
  (`DAT_1128_4986 == 8`) and the same `DAT_1128_4990`/`_DAT_1128_15fe` array pair are independently
  reused by the map canvas's own GDI drawing code (segment 12, `stars.exe.export.c:38330`-`38340`)
  to select a brush/pattern for an on-map overlay from a per-record style-index field — matching this
  project's already-documented "3-pattern minefield overlay with its per-owner visibility mask" in
  segment 12 (`code-coverage-report.md` segment 12 row; `client-interface.md`'s "Map canvas" section);
  (3) the record's compact size (18 bytes) is far too small for a full planet or fleet-stack record
  but comfortably fits a per-minefield summary (owner, type, position/radius, strength) of the kind a
  map overlay and a small popup caption would both need. This is offered as a confident structural
  finding, not a byte-for-byte field-layout confirmation — the record's individual field meanings
  beyond "type" (bits `0xe0` of byte offset 1), "owner" (bits `0x1e` of byte offset 1), and a
  drawing-style index (byte offset 0xc) were not decoded further this pass. This resolves the
  "real-world identity" open question shared with `fleet-movement-scanning-cargo.md`, though that
  file was not itself edited this pass to cross-reference the finding.

  **Further field layout — partially decoded this pass (2026-09-25).** Two more fields, and the
  record's overall shape, were pinned down by reading the array's insert/update routine directly
  (`stars.exe.export.c:44811`-`44907`, reached from a turn-file/network record-apply dispatcher
  structurally identical to the opcode handlers `save-turn-file-format.md` documents elsewhere):
  - **Byte offset `0` (4 bytes)** is a numeric id/handle — confirmed by `FUN_1028_328e`'s own use
    of it, `FUN_1038_1946(*(int*)(record + 0))` (`stars.exe.export.c:13061`), which resolves a
    display name from it the same way `FUN_1038_1d12`/`FUN_1038_1ab4` resolve planet/fleet names
    from *their* id fields elsewhere in the same function — i.e. this is the minefield's own
    unique id, not a pointer.
  - **The record's first 16 bytes (offsets `0x0`-`0xf`) are copied verbatim from incoming
    turn-file/network data**, `(DAT_1128_26b6 & 0x3ff) >> 1` words plus an optional trailing byte
    (`:44887`-`44902`), through the same generic variable-length field-copy idiom
    `save-turn-file-format.md` documents for other opcodes — i.e. everything so far decoded
    (id, type, owner, drawing-style, and the boolean flag byte at offset `0xd` used in the
    `SENDMESSAGE` call at `:13156`) is server/file-authoritative data, not locally computed.
  - **Byte offset `0x10`-`0x11` (the record's last word, completing the 18-byte/9-word stride) is
    *not* part of that copied payload** — it is set locally, immediately after the copy, to
    `DAT_1128_0082` (`stars.exe.export.c:44903`), the master turn counter this project has already
    identified elsewhere (`tutorial-system.md`'s lesson-dispatch write-up). This reads as a
    "last-updated-on-turn" freshness stamp for the cached summary record, distinct from anything
    supplied by the file/network payload itself.
  - **Not resolved this pass:** offsets `0x2`-`0xb` (10 bytes) and `0xe`-`0xf` (2 bytes) — a
    combined 12 bytes — remain undecoded. Given the record's role (a map-overlay/popup-caption
    summary, not the full simulation-side minefield record), these most plausibly hold position
    and radius/strength fields as this document already guessed, but no call site read or wrote
    an individual one of them with an identifiable meaning within this pass's time budget; a full
    byte-for-byte decode would need either the write side of the *live* (non-file-sourced) update
    path — this array is also written directly during ordinary turn processing, not only from
    file/network data, and that path was not located — or a value-level comparison against actual
    in-game minefield data, neither of which this pass attempted.

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

- ~~**Negative-habitability ("red planet") behavior is not clearly documented.**~~ **FULLY RESOLVED
  (2026-09-24 pass)** — see §3's "Negative-habitability decline" note above: decline is a smooth
  per-turn formula with a persisted fractional-carry byte, not an abrupt cliff, contradicting the
  secondary "dies after about a year" summary this document had flagged as unconfirmed. The "capacity
  field" ambiguity is also now resolved: in the decline branch, the multiplier is the planet's current
  **population** itself, not a separate maximum-population/capacity field (that distinct capacity
  concept exists but is only used by the growth branch, sourced from `FUN_1048_4a8e`). See §3 for the
  exact function citations (`FUN_1038_47d0`, `FUN_10b8_30ca`).
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
- ~~**No confirmed hard, engine-enforced minimum population to found a colony.**~~ **RESOLVED
  (2026-09-24 pass), with a correction to this project's earlier function attribution.** The
  Colonize handler does not live in `FUN_10b0_1f8c` after all — a full line-by-line read of that
  function this pass (all 785 lines, `stars.exe.export.c:73289`-`74073`) found it to be entirely
  fleet **movement/Stargate-transit** processing (per-turn position advancement, the Stargate
  ownership/feasibility check and forced cargo-dump-before-gating, the Overgating stochastic loss
  pass, and Alternate-Reality in-flight colonist attrition — all independently confirmed by cross-
  referencing every message code the function posts, e.g. `0xe6`/230 "not owned by you or a friend,"
  `0xec`-`0xee`/236-238 "unloaded ... in preparation for jumping through the stargate," `0xdf`-`0xe1`/
  223-225 ship-destroyed-by-overgating, `0xc1`/193 "colonists ... have died" from warp acceleration).
  None of `extracted-game-data/message-strings.txt`'s four Colonize-specific messages (81-84) are
  posted anywhere in this function. **Colonize is instead handled in `FUN_10b0_3f3a`** (segment 23,
  `stars.exe.export.c:74705`-`76293`) — the *other* per-fleet task dispatcher — directly contradicting
  this document's own previous note that `FUN_10b0_3f3a` was "confirmed not to handle this task"; that
  earlier claim was itself mistaken, not merely superseded.

  The Colonize branch is gated at `stars.exe.export.c:75484` (`uVar34 == 2`, the task nibble read into
  a local at line 74819) and runs a clean, fully-traced sequence, each step keyed to one of the four
  message strings: (1) if the fleet's waypoint has no resolved target, cancel with message 81 "not
  currently in orbit of a planet" (`:75485`-`75497`); (2) resolve the target planet and check its
  owner field (cached as `local_c2`, `0xffff` = unowned) — if already owned by anyone, cancel
  unconditionally with message 82 "already populated" (`:75855`, `:75863`); (3) **only if unowned**,
  test the fleet's colonist-cargo field — the same 8-byte cargo-colonist quantity at fleet-record
  offset `0x2c` documented elsewhere in this project (`fleet-movement-scanning-cargo.md`'s Transport
  cargo slots) — with a single, exact comparison: `*(long *)(puVar28 + 0x2c) == 0`
  (`stars.exe.export.c:75856`). **This is the entire minimum-colonist check.** If cargo is exactly
  zero, cancel with message 83 "you have failed to bring along any colonists" (`:75857`-`75859`); any
  nonzero value — one single colonist is sufficient — passes through unconditionally (`goto
  LAB_10b0_58d6` at `:75861`) to (4) a scan of every design slot in the fleet for a component of
  category `0x1000` (Mechanical) with subtype `0` or `1` (**Colonization Module** or **Orbital
  Construction Module**, per `extracted-game-data/component-stats.tsv`'s category-`0x1000` table,
  idx 0/1 — matching this project's independent PRT-fingerprint confirmation in
  `ship-design-and-components.md` §14 that PRT8/Alternate-Reality is blocked from subtype 0 and
  required on subtype 1) — cancelling with message 84 "none of the ships ... have a colonization
  module" if none is found (`:75503`-`75556`), or otherwise proceeding to fold the fleet's mineral and
  colonist cargo into the new colony, mark the fleet destroyed (`:75837`, the same "removed from play"
  status bit tested throughout this project), post the fleet's routine completion message
  (`0x4e`/78, `:75836`), and hand the exact colonist-cargo count straight to `FUN_10b8_301e`
  (`:75839`) for application to the new colony — consistent with, and now confirming at the code
  level, this document's own §4 claim that a colony "starts with whatever population a player
  chooses to unload," with no scaling or floor applied.

  **Definitive answer: there is no minimum threshold beyond strictly nonzero.** The engine's only
  guard is "did you bring at least one colonist" (`!= 0`), not any specific count — 1, 100, or
  2,500 colonists are treated identically by this check. The community-cited 2,500 (or
  1,000-for-Hyper-Expansion) figures remain purely a strategic convention with zero engine backing,
  exactly as this document already said, now confirmed rather than merely unrefuted. Separately,
  `ai-opponent-behavior.md` §9's **5,000-unit cargo threshold** for the built-in AI's own
  "colonizer-capable" heuristic is confirmed to be exactly that — an AI strategy choice layered
  strictly above this much lower engine-enforced floor, not a description of the engine rule itself.

  One loose end from the earlier (mistaken) attribution is itself now explained: the "entangled with
  remote-mining-style per-application arithmetic reusing the same task-nibble field" observation was
  correct in kind, wrong in place — `FUN_10b0_1f8c` does reuse a generic multi-slot cargo-transfer
  idiom (three mineral slots plus colonists) for its own, unrelated Stargate forced-cargo-dump
  mechanic, and `FUN_10b0_3f3a`'s Colonize branch separately reuses the *same kind* of idiom
  (`:75558`-`75833`) to fold delivered cargo into the new colony — two independent uses of one shared
  coding pattern, not one function doing double duty between mining and colonizing.
- ~~**The exact mapping from a planet's raw Gravity (g)/Temperature (°C)/Radiation (mR) reading to the
  0-100 "click" scale used in §2's `g, t, r` terms**~~ **Resolved this pass (2026-09-23) by dissolving
  the question.** While tracing unrelated galaxy-generation code (`new-game-setup.md` §3/§5a), we
  found the executable generates each axis directly as a 0-100-range byte at planet-creation time
  (two summed `0-44` rolls plus a flat `31`) and stores *only* that value — there is no separate
  physical-unit (g / °C / mR) field anywhere in the planet record for this code path to convert from.
  The 0-100 click scale is therefore the game's actual internal representation, not a transform of
  some other stored physical quantity; the "linear or logarithmic" question doesn't apply because
  there is nothing being mapped. The widely-repeated real-world bounds (0.12g-8.00g, -200°C to 200°C,
  0-100mR) should be treated as a display-only convention for presenting the click value to the
  player. See §2's new note for the exact code citation.
- ~~**The in-band habitability combination formula's "unanalyzed helper" (§2) is still
  unresolved.**~~ **RESOLVED (2026-09-24 pass).** There is no separate helper function — the
  reduction is inlined in the habitability function itself (`FUN_1048_490e`), and its exact per-axis
  closed form (`axisMultiplier = 1.5 - g` for `g > 0.5`, combined by a running product) is now
  documented in §2. The earlier guess that it lived in segment 11 was itself mistaken: the function
  turned out to live in segment 10 (`FUN_1048_*`), found by tracing forward from the population-growth
  driver rather than by sampling segment 11 further.
- ~~**Rounding/truncation rule for population growth is still unconfirmed.**~~ **RESOLVED (2026-09-24
  pass) — with a negative result relative to the "plausible answer worth checking" flagged previously.**
  The population-growth/decline function (`FUN_1038_47d0`, located this pass — see §3) does **not**
  use the mining pipeline's RNG-based stochastic rounding. It uses a distinct, deterministic
  fractional-carry-byte mechanism instead: a persisted 0-99 remainder is added to (growth) or
  subtracted from (decline) a per-planet byte each turn, carrying/borrowing a whole colonist when the
  byte overflows/underflows 100. No RNG call appears anywhere in the function. This also resolves the
  "capacity field" real-unit-meaning question tracked here in earlier passes — see the first bullet
  above and §3 for the full writeup.
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
