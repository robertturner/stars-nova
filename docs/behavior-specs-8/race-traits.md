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

### 1a. The point-total formula, reconstructed from the exported client

A pass over the executable's race-wizard save/finish logic located and substantially reconstructed the point-total calculation this document's Open Questions previously flagged as entirely undocumented. **The function is now precisely identified: `FUN_10e0_2e9a`, in code segment `0x10e0` (this project's segment 29, the same segment that hosts the six `RACEWIZARDDLG1`-`RACEWIZARDDLG6` procedures — confirmed by direct disassembly-offset inspection of `stars.exe`'s NE segment table, segment 29, file offset `0x897c0`).** The function computes an internal raw total, which is **divided by 3** at the very end (`return (int)((long)local_e / 3);`, confirmed as literally the function's final statement) to produce the RW point figure the wizard displays; the race can only be saved once this final divided total is ≥ 0. Recovered structure (raw units, before the final ÷3, unless noted):

1. **Baseline**: 1,650 raw (≈550 RW points) before any adjustment.
2. **Growth-rate term**: the clamped growth-rate slider value feeds a non-linear breakpoint table, then the result is multiplied by an internal "habitability quality" score (a discretized numerical-integration sampler over the three tolerance intervals, distinct from the per-planet habitability formula in `population-growth.md`) divided by 24, and subtracted — the concrete mechanism behind "a narrower habitability band lets you afford a higher growth rate."

   **The breakpoint table itself is now recovered directly from `FUN_10e0_2e9a`'s own body (this pass).** For growth-rate slider value `g` (1-20), the function computes two things from `g` before the subtraction: a slider-dependent baseline contribution to the running total, and a multiplier (`iVar9` below) applied to the quality score:

   | g | Baseline contribution (raw) | Multiplier (`iVar9`) |
   |---|---|---|
   | 1 | 22,650 | 1 |
   | 2 | 18,450 | 2 |
   | 3 | 14,250 | 3 |
   | 4 | 10,050 | 4 |
   | 5 | 5,850 | 5 |
   | 6 | 5,250 | 7 |
   | 7 | 3,900 | 9 |
   | 8 | 2,250 | 11 |
   | 9 | 1,875 | 13 |
   | 10-13 | 1,650 (unchanged baseline) | 15, 17, 19, 21 |
   | 14-19 | 1,650 (unchanged baseline) | 24, 27, 30, 33, 36, 39 |
   | 20 | 1,650 (unchanged baseline) | 45 |

   The final growth-rate term is `baseline − (multiplier × quality / 2000) / 24`, where `quality` is `FUN_10e0_3404`'s return value (read as a 32-bit `DX:AX` pair, not the 16-bit value an earlier pass assumed — the decompiler's `void` return type for that function is wrong; it genuinely returns a value through the register pair, confirmed by the `in_DX`/`uVar4` combination consumed at this call site). **`FUN_10e0_3404` is confirmed this pass to be the "habitability quality" sampler**: it is a triple-nested loop over discretized sample points on each of the three tolerance axes (sample counts read from `local_50[3]`/`[4]`/`[5]`, one per axis), with a separate immune-axis special case (loop variable `local_1a` running 0-2, widening the effective per-axis window via an `0xfffd`/`0xfffe` bitmask when an axis's stored bound is the all-`0xff` immune sentinel) — structurally exactly the "discretized numerical-integration sampler over the three tolerance intervals" already described qualitatively. Its own internal accumulation/normalization into a final 32-bit figure was not fully traced this pass (see Open Questions below for how this affects the 725/1,448-point quantification attempt; a later pass did read the function's full body and substantially extends this description — see the Open Questions entry's own follow-up for the three-pass structure, per-pass weights, and the newly-identified trait-bit-1 widening gate). **Now fully resolved, numerically, and validated against the live client — see §1b below.** Correction to this paragraph: the per-axis sample counts are not race-stored fields; they are fixed at 11 for a non-immune axis and 1 for an immune axis (§1b).
3. **Per-axis center-deviation refund**: each non-immune axis adds `abs(center − 50) × 4` raw units (≈1.33 RW points per percentage-point the axis's center sits off 50) — applied identically and symmetrically to all three axes, including Radiation (this document's §4 claim that radiation is comparatively "free" is about galaxy-wide *availability*, a different calculation — this point-cost term itself treats all three axes the same).
4. **Two-or-more-immunities penalty**: if 2 or more axes are immune simultaneously, an additional flat **−150 raw (≈−50 RW points)** lopsidedness penalty applies, on top of whatever per-immunity cost is charged elsewhere in the (unrecovered) immunity-cost table — see Open Questions regarding the 725/1,448-point figures below.
5. Further quadratic/cubic penalty terms apply to two of the economic-slider fields (**factory output and factories-per-10,000-colonists — corrected this pass; field index 0 is colonists-per-resource itself, not one of these two penalized fields**, fields index 1 and 3 in the generic per-race field accessor) once either exceeds a value of 10, with an extra multiplier for one specific PRT. **Identified this pass:** the PRT is **Hyper Expansion (PRT 0)** — the code computes the field-3 penalty term's multiplier as `((raceIsHE ? 1 : 0) + 2)`, i.e. a flat **3x** multiplier for Hyper Expansion versus **2x** for every other PRT, before applying it to the quadratic/cubic penalty.
6. ~~**LRT stacking-penalty formula is unknown.**~~ **RESOLVED structurally, and now RESOLVED numerically.** A 14-entry signed per-LRT cost table (matching the 14 LRT checkboxes) is summed across every selected LRT, then an escalating penalty is added on top:
   - if `(#positive-effect LRTs selected + #negative-effect LRTs selected) > 4`: an extra `(count − 4) × count × 10` raw units.
   - if `#positive − #negative > 3`: an extra `(diff − 3) × 60` raw units.
   - if `#negative − #positive > 3`: an extra `(diff − 3) × 40` raw units.

   **The 14 individual per-LRT raw costs are now recovered from the binary itself** (see Open Questions item for the extraction method and cross-validation). In wizard checkbox order (IFE, TT, ARM, ISB, GR, UR, MA, NRSE, CE, OBRM, NAS, LSP, BET, RS — **corrected this pass**: positions 4-6 are Generalized Research, Ultimate Recycling, Mineral Alchemy, in that order, per the LRT name strings at `dynamic-strings.txt` IDs 306-319 and the live Step-3 checkbox control IDs 291-304; an earlier pass listed them as UR, MA, GR and so attached the three values below to the wrong names), the raw table (added directly to the running total — negative means "this LRT costs points," positive means "this LRT refunds points") reads:

   | LRT | Raw | ÷3 (RW points) |
   |---|---|---|
   | Improved Fuel Efficiency (IFE) | −235 | ≈ −78 |
   | Total Terraforming (TT) | −25 | ≈ −8 |
   | Advanced Remote Mining (ARM) | −159 | ≈ −53 |
   | Improved Starbases (ISB) | −201 | ≈ −67 |
   | Generalized Research (GR) | +40 | ≈ +13 |
   | Ultimate Recycling (UR) | −240 | −80 |
   | Mineral Alchemy (MA) | −155 | ≈ −52 |
   | No Ram Scoop Engines (NRSE) | +160 | ≈ +53 |
   | Cheap Engines (CE) | +240 | **exactly +80** |
   | Only Basic Remote Mining (OBRM) | +255 | ≈ +85 |
   | No Advanced Scanners (NAS) | +325 | ≈ +108 |
   | Low Starting Population (LSP) | +180 | +60 |
   | Bleeding Edge Technology (BET) | +70 | ≈ +23 |
   | Regenerating Shields (RS) | +30 | +10 |

   This table is cross-validated: this document's own §3 already cites a secondary source calling Cheap Engines "worth up to 80 points" — the recovered table gives **exactly 240/3 = 80** for Cheap Engines, an exact match with no rounding. Every entry's sign also matches this document's own Positive/Negative/Mixed categorization (Positive traits are costed — negative raw value; Negative/Mixed traits are refunded — positive raw value). With the corrected row order there is no exception at all: the +40 refund belongs to Generalized Research, a Negative/mixed trait, not to Ultimate Recycling. **Live-verified this pass (§1b):** toggling GR, UR, MA, or IFE alone on the Humanoid preset moves the wizard's display from 25 to exactly 38, −55, −26, and −53 respectively, matching these corrected assignments to the point.

   A further PRT-dependent flat adjustment (distinct values for three specific PRTs) applies for one specific LRT selection beyond this schedule — **identified this pass, see item 3 in Open Questions below: the LRT is No Advanced Scanners (index 10), and the three PRTs are Packet Physics (−280 raw), Super Stealth (−200 raw), and Jack Of All Trades (−40 raw), each an *additional* deduction on top of NAS's own +325 base refund.**
7. **Research-cost-class net-bias term**: summing `(costClassValue − 1)` across all six fields (**the stored class value's polarity is corrected this pass: 0 = Expensive, 1 = Normal, 2 = Cheap — confirmed via the Economic-settings dialog's own radio-button order and the Nucleotid preset's stored values, inverting the direction this document previously assumed. So `costClassValue − 1` is −1 for Expensive and +1 for Cheap**) gives a net bias; a net-positive bias costs a quadratic `−(bias²) × 130` raw units (plus extra flat corrections at specific bias values), while a net-negative bias instead refunds points via a lookup table — the concrete mechanism behind this document's own guidance to "offset every Cheap field with a matching Expensive field."
8. **The "starts at tech level 3 (4 for JOAT)" checkbox** costs a flat **−180 raw**, which divides to **exactly −60 RW points** — an exact confirmation of this document's §6 "flat 60 points" claim, down to the number. The label logic driving the "3 vs. 4" distinction independently confirms **PRT index 9 = Jack Of All Trades**.
9. **Alternate Reality pays an additional, previously undocumented penalty**: if the race is AR and Energy research is specifically set to Expensive, a flat **−100 raw (≈−33 RW points)** additional penalty applies, on top of whatever the normal research-cost-class term above already charges. This is a brand-new finding not previously documented anywhere, and is mechanically consistent with AR's Energy-tech-driven resource formula (§5 below).
10. **The Germanium-cost-discount checkbox** costs a flat **−175 raw**, which divides to **≈−58 RW points** — confirming this document's §5 "flat 58 points" claim almost exactly.

**Arithmetic conventions (confirmed this pass, needed to reproduce the displayed figure exactly):** every step uses C-style integer arithmetic that truncates toward zero. The growth-rate term takes the quality score integer-divided by 2,000 first, then multiplies by the breakpoint multiplier, then integer-divides by 24. The final ÷3 also truncates toward zero, so a raw total of −1,642 displays as −547, not −548.

**Also confirmed structurally:** the growth-rate slider is clamped to the documented **[1, 20]** range at the code level (§4 below), and environment-tolerance bounds are clamped to **[0, 100]** with a previously undocumented **minimum band width of 20** enforced whenever a bound is adjusted via the spinner controls (a narrower band is not permitted through that control, though the exact interaction with directly-typed values was not checked).

**The directly-typed-value question is now resolved, by reading `RACEWIZARDDLG2` (the Environment-tolerance-and-growth-rate stage's dialog procedure, `stars.exe.export.c` line 92540) and its mouse/adjustment handler `FUN_10e0_1844` (line 93050) in full.** `RACEWIZARDDLG2` itself handles exactly five message classes: `WM_SETCURSOR` (0x20, over the custom-painted track), `WM_INITDIALOG`/`WM_PAINT`/`WM_ERASEBKGND`/`WM_CTLCOLOR` (layout and redraw), `WM_COMMAND` (0x111, but only for the three immunity checkboxes, control IDs `0x123`-`0x125`, and the dialog's OK/Cancel/Help buttons), and `WM_LBUTTONDOWN`/`WM_LBUTTONDBLCLK` (0x201/0x203, dispatched to `FUN_10e0_1844`). No `EN_CHANGE`-style edit-control notification is handled anywhere in this procedure, and no edit-box control ID is referenced at all — this dialog stage has **no directly-typed numeric entry control for the tolerance bounds in the first place**. `FUN_10e0_1844` itself implements two adjustment paths for a bound, both reached only from the mouse messages above: dragging the custom thumb widget directly (which recomputes both bounds from the drag position and is not separately width-clamped, since a drag re-centers the whole band rather than moving one endpoint), and a click-and-hold, timer-repeated single-step nudge of one endpoint (the `FUN_1040_2a28`/`FUN_1040_2ab2` loop) — and it is this second path that contains the min-width-20 enforcement (`stars.exe.export.c` lines 93193-93196: `if ((int)local_4._1_1_ - (int)(char)local_4 < 0x14) { ... re-center to force width exactly 0x14 }`), applied unconditionally to every endpoint nudge regardless of direction. **Conclusion: the question is moot rather than merely answered — there is no separate directly-typed-value input path in this dialog stage for the min-width-20 clamp to be bypassed through, because every adjustment to a tolerance bound in `RACEWIZARDDLG2` funnels through this one function's one clamp.** (This does not rule out some other surface, such as a `.r1` race-file hand-edit loaded directly, producing a sub-20 band outside the wizard entirely — only that the wizard's own UI cannot produce one.)

**11. A 10-entry per-PRT flat baseline adjustment, recovered numerically this pass — but it is not an "immunity" table.** `FUN_10e0_2e9a` also subtracts a fixed, PRT-indexed raw value from the running total (`local_e -= table[prtIndex]`), read directly from the binary (segment 29, offset `0x2e4`, ten consecutive signed 16-bit words, immediately adjacent to and contiguous with the LRT table above at offset `0x2c8`). In the established PRT-index order (HE, SS, WM, CA, IS, SD, PP, IT, AR, JOAT):

   | PRT | Table value | Net effect on total (raw) | ÷3 (RW points) |
   |---|---|---|---|
   | Hyper Expansion (HE) | 40 | −40 | ≈ −13 |
   | Super Stealth (SS) | 95 | −95 | ≈ −32 |
   | War Monger (WM) | 45 | −45 | −15 |
   | Claim Adjuster (CA) | 10 | −10 | ≈ −3 |
   | Inner Strength (IS) | −100 | +100 | ≈ +33 |
   | Space Demolition (SD) | −150 | +150 | +50 |
   | Packet Physics (PP) | 120 | −120 | −40 |
   | Interstellar Traveler (IT) | 180 | −180 | −60 |
   | Alternate Reality (AR) | 90 | −90 | −30 |
   | Jack Of All Trades (JOAT) | −66 | +66 | ≈ +22 |

   This reads as a flat per-PRT "balance tax/subsidy" baked directly into the point-total formula — strong PRTs (Interstellar Traveler, Packet Physics, Super Stealth) are costed extra points just for being selected, while support-flavored PRTs (Space Demolition, Inner Strength, Jack Of All Trades) get a flat refund. This lines up qualitatively with community lore cited elsewhere in this document: **Space Demolition's +50-RW-point entry is the single largest refund in the table**, matching §2's own note that SD is "commonly cited as netting the most starting advantage points of any PRT," and Interstellar Traveler's −60-RW-point entry is the single largest cost, consistent with its unusually broad set of Stargate-related bonuses.

   **This table answers half of Open Questions item 2 below, but not the 725/1,448-point immunity question specifically** — see that item for why: nothing in `FUN_10e0_2e9a` indexes a table by *axis* or by *immunity state* the way a dedicated "cost of immunity" table would; the only immunity-specific term found anywhere in the function is the already-documented flat −150-raw two-or-more-immunities penalty (item 4 above). A single axis's immunity is not charged anywhere as a distinct line item in this formula; see Open Questions for the mechanism this pass concluded is actually responsible for immunity's real-world point cost.

### 1b. The habitability-quality sampler, fully resolved, and the whole point formula validated against the live client (this pass)

**How the two "opaque" constants were recovered.** Earlier passes read the two scaling constants used by `FUN_10e0_3404` (`stars.exe.export.c:94715`-`94728`) as unknown `DAT_1128_1dca`/`DAT_1128_1dd2`. This pass first tested whether they were really CS-relative, like the segment-29 race-cost tables. They are not. The raw instruction bytes are at segment-29 offsets `0x3869`-`0x38b5` (file `0x897c0` + offset; segment 29's NE-table file offset is again `0x897c0`, the same base that located the LRT/PRT tables at `0x2c8`/`0x2e4`). They are an x87 multiply and load with default DS addressing and no CS-override prefix: `9B DC 0E CA 1D` (multiply by the qword at DS:`1DCA`) and `9B DD 06 D2 1D` (load the qword at DS:`1DD2`). So these really are shared-data-segment values. They are initialized data in segment 38 (DGROUP, file offset `0xb0bc0`, file length `0x2292`), so they can be read directly from `stars.exe`. The same bytes appear in this project's earlier live dump of the running game's data segment, which cross-checks the reading. The function also uses three more constants that Ghidra hid entirely, and its per-sample evaluator uses two more. All seven, as IEEE doubles:

| DS offset | Value | Role |
|---|---|---|
| `0x1dc2` | 0.0 | initial value of every accumulator |
| `0x1dca` | **0.01** | "width ÷ 100" scale for a non-immune Gravity or Temperature axis |
| `0x1dd2` | **11.0** | scale for an immune Gravity or Temperature axis |
| `0x1dda` | 0.1 | final scale, applied after the three passes (not visible in the decompile) |
| `0x1de2` | 0.5 | final rounding offset (not visible in the decompile) |
| `0x1d02` | 1/3 | per-sample habitability evaluator (`FUN_1048_490e`): mean of the per-axis squares |
| `0x1d0a` | 0.9 | per-sample habitability evaluator: rounding offset added after the square root |

Two runtime facts were confirmed from raw bytes as well:
- `FUN_1120_0e40` sets the FPU control word's rounding field to "toward zero" before its integer store, so it truncates rather than rounds.
- `FUN_1120_0dc2` is definitively the square root. It passes the runtime library a descriptor at DS:`0x1858` whose name field reads "sqrt". Neighbouring descriptors read "pow", "log", "log10", and "exp".

This settles the project-wide "FPU-sqrt idiom" identification by direct evidence, not inference.

**Per-sample habitability evaluator, including the part the decompile hid (`FUN_1048_490e`, segment 10, file `0x30300` + `0x490e`).** The routine uses 32-bit 386 instructions that Ghidra partly dropped. Read from raw bytes, it keeps two running sums over the three axes:
- An **immune axis** adds 10,000 to a "closeness" sum.
- An **in-band axis** adds `(100 − ⌊100·d/h⌋)²` to that sum. Here `d` is the distance from the ideal center and `h` is the center-to-edge distance on that side.
- **Separately**, an in-band axis also scales a running "ideality" factor, seeded at 10,000, by `(3h − 2d)/(2h)` when `2d > h`. This is the already-documented `1.5 − g` edge factor.
- An **out-of-band axis** adds `min(distance outside the band, 15)` to a penalty sum.

If the penalty sum is nonzero, the result is its negative. Otherwise the result is `⌊√(closeness ÷ 3) + 0.9⌋ × ideality ÷ 10,000` in integer arithmetic. **Cross-document note:** `population-growth.md` §2 currently gives the in-band value as the ideality product alone and omits the square-root closeness term, which lives only in the hidden FPU code. That section should be revisited against this reading. The race-wizard reproduction below depends on the square-root term and matches the live client only with it included.

**Sampler structure (`FUN_10e0_3404`), now fully specified.**
- **Three passes.** The sampler makes three passes with a terraforming allowance `k`:
  - Pass 0 uses `k = 0`.
  - Pass 1 uses `k = 5`, or 8 with Total Terraforming (trait bit 1; see Open Questions for the identification).
  - Pass 2 uses `k = 15`, or 17 with Total Terraforming.
- **Sampling grid per axis:**
  - An **immune axis** is sampled once, at 50, with multiplier 11.
  - A **non-immune axis** is sampled at exactly **11 points**, spanning from `max(0, low − k)` to `min(100, high + k)`. Point `i` (0-10) sits at `start + ⌊i × width / 10⌋`.
  - The sample counts are these constants, not race fields; an earlier pass mistook the local grid-descriptor array for race data. For a "fresh race" (Humanoid, bands 15-85 on every axis), each non-immune axis therefore has 11 samples at 15, 22, 29, …, 85.
- **Terraforming pull (passes 1 and 2).** Each non-immune sample coordinate is pulled up to `k` clicks toward the race's center, as if terraformed. The leftover distance on each axis is recorded with its sign: positive below center, negative above. If the **signed** sum of the three leftovers exceeds `k`, the sample's habitability is reduced by the excess, floored at 0. Because the sum is signed, leftovers on opposite sides of center can cancel. This quirk was confirmed live: an absolute-value sum instead mispredicts every preset.
- **Weighting.** Each sample's habitability is squared and weighted by 7, 5, or 6 for pass 0, 1, or 2.
- **Radiation (innermost) sum.** This sum is an integer. It is multiplied by 11 if radiation is immune, otherwise by `width_rad` and integer-divided by 100.
- **Temperature and Gravity sums.** These are doubles, multiplied by 11.0 if the axis is immune, otherwise by `width × 0.01`.
- **Result.** The three passes are summed, and the sampler returns `⌊total × 0.1 + 0.5⌋`.

**Worked numbers.**
- **Humanoid preset** (JOAT, no LRTs, all bands 15-85, growth 15%): the sampler returns Q = **3,293,786**. The growth term is `27 × ⌊3,293,786 / 2,000⌋ / 24` = `27 × 1,646 / 24` = **1,851** raw. With every other §1a term, the raw total is **75**, which displays as **25**, the wizard's real opening figure.
- **Humanoid with Gravity immune:** Q = **6,345,682**, and the growth term rises to **3,568** raw. The raw total becomes 75 + 1,851 − 3,568 = **−1,642**, which displays as **−547**.
- **Tri-immune:** every sample scores 100, so the passes contribute 10,000 × 1,331 × (7 + 5 + 6) = 239,580,000, and Q = **23,958,000** exactly. The growth term is **13,476** raw, plus the −150 two-or-more-immunities penalty. The display is **−3,900**.

**Live validation (otvdm-master-2697, Stars! 2.70j, Custom Race Wizard).** This pass implemented the complete formula in a scratch calculator (§1a items 1-11, plus this section). It then compared every prediction against the wizard's "Advantage Points Left" display. All **18 of 18** readings matched exactly:

| Configuration | Predicted | Live |
|---|---|---|
| Humanoid / Rabbitoid / Insectoid / Nucleotid / Silicanoid / Antetheral presets | 25 / 32 / 43 / 11 / 9 / 7 | 25 / 32 / 43 / 11 / 9 / 7 |
| Humanoid + TT / + GR / + UR / + MA / + IFE (one at a time) | −115 / 38 / −55 / −26 / −53 | identical |
| Humanoid, all LRTs cleared again | 25 | 25 |
| Humanoid, Gravity immune | −547 | −547 |
| Gravity reset to 20-80, Temperature immune | −431 | −431 |
| Gravity and Temperature at 20-80, Radiation immune | −326 | −326 |
| + Gravity immune (two immunities) | −1,464 | −1,464 |
| + Temperature immune (all three) | −3,900 | −3,900 |
| All immunities cleared (bands now 20-80) | 178 | 178 |

A side observation: clearing an immunity checkbox does not restore the previous band. The axis comes back as a centered 20-80 band, shown as 0.31g-3.20g, −120°C to 120°C, and 20-80 mR. The rows above therefore record the band each step actually had.

**The 725 / 1,448 figures: resolved as not reproducible in v2.70j.** With the formula now exact, immunity's cost can be computed for any baseline:
- From the **Humanoid** preset: one immunity costs **572** points, two cost **1,730**, and all three cost **3,925**.
- From the wizard's own post-toggle **20-80** default band: **504**, **1,642**, and **4,078**.

This pass swept every centered band width from 20 to 100 clicks (all three axes alike) against every growth rate from 1 to 20:
- The single-immunity cost ranges from **8 to 1,192** points, so no one "cost of immunity" exists.
- The two-immunity cost is always at least **2.5×** the single-immunity cost. Every immune axis multiplies the sampler's grid volume, and the −150-raw penalty adds to that.

The community figures imply a second immunity costing about the same as the first (1,448 ≈ 2 × 725). That shape cannot arise from this version's formula for any centered baseline. Single-immunity costs near 725 do occur, for example 729 for 20-80 bands at 19% growth, but never alongside a 1,448 two-immunity total. The figures should be read as specific to their source's unstated race, or possibly to another game version. They are not constants of the game, and implementers should compute immunity cost from the formula, not from them. §4 and Worked Example A below cite these figures only as the community source's own numbers.

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
| **Inner Strength (IS)** | Defensive specialist | Planetary defenses cost **40% less**; colonists defend better and heal faster; colonists aboard freighters keep reproducing (at half rate) and beam down surplus population; exclusive Super Freighter, Croby Sharmor combined armor/shield, Jammer torpedo-deflectors, and Speed Trap minefields (confirmed omission, added this pass — see §3a/Open Questions). Trade-off: weapons cost **25% more**, and it has no access to Smart/Neutron/Enriched-Neutron/Peerless/Annihilator bombs. |
| **Space Demolition (SD)** | Minefield specialist | Sole access to essentially all mine types and both dedicated mine-laying hulls; own minefields **decay at 1%/planet/year** versus **4%/planet/year** for everyone else's; can remotely detonate its own standard minefields; crosses enemy minefields **2 warps faster** than the safe limit; minefields double as non-penetrating scanners. No offensive/defensive combat bonus of its own. Commonly cited as netting the most starting advantage points of any PRT. |
| **Packet Physics (PP)** | Mass-driver specialist | Starts with a second homeworld-tier planet (non-tiny universes), a Warp 5 mass accelerator already built at the home starbase, and Tech 4 Energy (not a Mass Driver tech level — see below), eventually able to fling packets up to Warp 13; mineral packets are cheaper/smaller, carry a built-in penetrating scanner, and can terraform the target planet on arrival (50% chance of +1% to one stat, plus a small chance per 100 kT of mineral not caught); can sense every player's packets in flight. |
| **Interstellar Traveller (IT)** | Stargate specialist | Starts with two Stargate-equipped planets; Stargates cost **25% less**, can eventually be built with **no range/mass limit**, can carry cargo through gates without it counting against the gate's mass limit, and gate-overrun is less likely to destroy the ship. Trade-off: Mass Drivers are only half as effective at catching incoming packets, are worse at flinging them, and its own flung packets always decay regardless of speed. |
| **Alternate Reality (AR)** | Lives on starbases, not planets | Population lives aboard starbases (culminating in the huge Death Star hull) rather than on the planet surface; maximum population is set by **starbase size**, not planet habitability; can remote-mine its own planets and has an intrinsic ability to scan for enemy fleets; resource production is driven by a **PRT-specific formula keyed to Energy tech level** rather than the standard population/habitability formula (see §5); starbases cost 20% less (does not stack with Improved Starbases). Trade-off: cannot build any planetary installations, loses **3% of any in-transit fleet's population per year** of travel, and — a previously-undocumented risk, confirmed this pass directly from the recovered trait-description string (see §3a) — **if a starbase is destroyed, every colonist orbiting that world is killed outright.** |
| **Jack Of All Trades (JOAT)** | Generalist / beginner-friendly | Starts at **tech level 3** in all six research fields (see §6); maximum population per planet is **+20%**; Scout/Frigate/Destroyer hulls get a built-in **penetrating** scanner (confirmed this pass; the recovered trait text specifies "penetrating" explicitly) scaling with Electronics tech. Considered the most forgiving PRT to design and play but with no standout economic or military edge. |

[wiki: Primary racial traits; wiki: Custom Race wizard; wiki: Race Design; GameFAQs Strategy Guide PG2-01 through PG2-10 and ANR-00]

### 2a. Packet Physics/Interstellar Traveler's second home planet — the real, live mechanism (found this pass, 2026-09-23)

The Open Questions entry below (originally filed 2026-09-05) documents a per-PRT starting-ship `switch` that would, if it ran, distinguish every PRT's starting fleet/planet loadout — but that switch sits in dead code and never executes. That earlier finding is still true and is kept below, but it is **not** the mechanism actually responsible for Packet Physics' and Interstellar Traveler's second planet. A live, executing mechanism was located this pass, inside the same galaxy-generation continuation routine already documented in `new-game-setup.md` §3 (segment 16, function `FUN_1078_1334` — rated Full coverage). It directly explains both PRTs' second-planet behavior and supersedes the vaguer "certain player types" phrasing `new-game-setup.md` §3 previously used for this bonus.

**What the code does, traced structurally:** during per-player setup (the same pass that assigns each player's home star and starting fleet), the routine reads the player's race PRT index via the client's generic per-race field accessor (the identical accessor and field selector — `0xe` — used elsewhere in this project to identify the PRT field, e.g. the segment-17 Stargate-transit-restriction gate). Two, and only two, PRT values reach a shared "place a second home-tier planet" code block:

- **PRT 6**, gated additionally on the galaxy not being the smallest size.
- **PRT 7**, gated on the identical galaxy-size condition, reached immediately after that PRT's own starting-fleet ship templates are added.

Both PRT values jump into the *same* shared block, which: scans the galaxy's already-placed, still-unclaimed stars for ones lying within an inner-to-outer squared-distance band from the player's home star (roughly 15%–23% of the galaxy's diameter — the same style of concentric distance-band weighting already documented for the star-placement relaxation pass in `new-game-setup.md` §3), picks uniformly at random among whichever candidates fall inside that band (falling back to the single nearest unclaimed star outside the band if none are inside it), marks the chosen star as owned by the player, and then makes up to 100 attempts at an additional per-candidate validity check before accepting it — if all 100 attempts fail, it falls back to copying the home planet's own environment-tolerance readings onto the new planet rather than rolling fresh ones (corrected this pass from an earlier "mineral concentrations" characterization — see the fully-traced finding below). This is the exact mechanism `new-game-setup.md` §3 already described in outline ("a second home-world-tier planet placed via a nearest-valid-position search, up to 100 attempts within galaxy bounds") — this pass adds the missing PRT-gating detail that outline lacked.

**PRT-index identity of the two gated values, cross-checked three independent ways:**
- PRT 6 also gates a one-field starting-tech-level assignment (Energy only) in the same function, matching this document's §6 table's **Packet Physics: Energy 4** entry exactly (case 6 in that switch sets the Energy field to a nonzero starting level and no other field).
- PRT 7 also gates a two-field starting-tech-level assignment (matching §6's **Interstellar Traveler: Propulsion 5, Construction 5** entry exactly) and, separately, is exactly the same numeric PRT index (7) already identified as Interstellar Traveler via the independent segment-17 Stargate-transit-restriction cross-reference (`code-coverage-report.md`'s segment 17 row).
- Both readings agree with `ship-design-and-components.md` §5's independently-reconstructed PRT-index convention (Hyper Expansion=0, War Monger=2, Claim Adjuster=3, Inner Strength=4, Space Demolition=5, **Packet Physics=6**, **Interstellar Traveler=7**, Alternate Reality=8), which is itself corroborated by this document's live-client PRT-9/JOAT confirmation (§1a item 8).

**All three independent conventions agree with no conflict**: PRT index 6 is Packet Physics and PRT index 7 is Interstellar Traveler, and both are the two PRTs gated into the shared second-planet-placement code. This resolves the contradiction the project owner flagged from live gameplay (Interstellar Traveler does get a second starting planet) — the mechanism is real, live, and shared with Packet Physics, not the dead switch, and not something limited to Packet Physics alone as the Open Questions entry below might otherwise suggest by omission.

**The per-candidate validity check is now traced directly (later pass).** Reading the shared placement block in full (`stars.exe.export.c:51473`-`51502`) shows the check is exactly a **habitability threshold, not an isolation-distance check** — confirming the "most likely" guess this section originally offered, and pinning down the exact threshold. Immediately after a candidate star is marked owned, the code calls `FUN_1048_490e` — the same per-planet habitability evaluator already established project-wide (`population-growth.md` §2; also the quality sampler's per-sample evaluator in §1a above) — against the new planet's own record and the current player index, and loops `while (habitability < 10)` (`stars.exe.export.c:51473`-`51474`): the candidate is accepted the first time its habitability (the same 0-100 scale `FUN_1048_490e` returns elsewhere in this project) reaches **at least 10%** for the owning race. Each failed attempt (`stars.exe.export.c:51480`-`51488`) re-rolls the planet's own three environment-tolerance bytes at record offsets `+0xc`/`+0xd`/`+0xe` — the identical Gravity/Temperature/Radiation byte offsets `FUN_1048_490e` itself reads, per this project's own established convention for that function's argument layout (§1a above) — to a fresh uniform value in `[2, 98]`, mirroring the same value into a second, adjacent 3-byte field at `+0xf`/`+0x10`/`+0x11` (consistent with a planet record storing both a "current" and a "native/pre-terraform" copy of each axis, naturally identical at the moment of creation). The loop is bounded at 100 attempts (`stars.exe.export.c:51477`, `bVar6 = 99 < local_118`), matching the already-documented cap exactly.

**This also corrects a specific detail this section's opening paragraph above previously carried as a guess, rather than leaving it unresolved: the fallback triggered by exhausting all 100 attempts (`stars.exe.export.c:51490`-`51502`) copies the *home* planet's own environment-tolerance bytes (both the `+0xc` and `+0xf` fields, read from the home planet's record at the same relative offsets, stride `0x38` per planet) onto the new planet — not "mineral concentrations."** Actual mineral-quantity rolling for the new planet is a separate, later step in the same function (`stars.exe.export.c:51516`-`51523`, an unrelated pair of fields) that runs unconditionally for every new planet regardless of which branch above was taken — immediately adjacent in the same function, and the most likely source of the earlier pass's "mineral concentrations" phrasing being attributed to the wrong branch. Since the home planet is by definition habitable for its own race, falling back to its exact environment reading guarantees a valid (if unoriginal) result when 100 random rolls all come up short — consistent with this fallback existing to guarantee loop termination rather than to produce a second interesting planet.

**The two starting-ship template IDs queued for PRT 7 immediately before the jump into the shared block are now fully identified — see `new-game-setup.md` §5 for the complete per-PRT starting-fleet trace this resolved as a side effect.** They are template indices 7 and 8 of a previously-unattributed 22-entry starting-ship design-template table this pass located and read directly out of the binary (segment 2's data region, immediately preceding the already-known 32-entry hull table). Those two indices name real designs: **"Stalwart Defender" (a Destroyer-hulled design) and "Swashbuckler" (a Privateer-hulled design)** — an exact, no-hedging match for the dead `StarMapInitialiser.cs` switch's own one-line comment for Interstellar Traveler, "a destroyer and a privateer," now confirmed as the *real, live* starting-fleet addition in the actual exported client rather than only as an unimplemented aspiration in that unrelated downstream file.

### 3. Lesser Racial Traits (choose zero or more, individually priced)

LRTs are grouped by the wiki into "positive" (net beneficial, so always worth taking if the point
cost is acceptable), "negative" (net drawback, taken only to recoup points), and a few with mixed
effects.

| LRT (abbrev.) | Category | Effect |
|---|---|---|
| **Improved Fuel Efficiency (IFE)** | Positive | Ships use **15% less fuel**; unlocks the Fuel Mizer and Galaxy Scoop engines; starting Propulsion tech **+1** level. |
| **Total Terraforming (TT)** | Positive | ~~Planetary hab values can be nudged **±3%** immediately at colonization~~ — **corrected (§3b, fourth pass):** the race can terraform **±3%** from the first turn, because its "Total Terraform ±3" item needs no tech at all. This is ordinary paid terraforming, not an automatic change at colonization. Ordinary (non-Claim-Adjuster) terraforming can eventually reach **±30%**; terraforming resource cost is **30% lower**. |
| **Advanced Remote Mining (ARM)** | Positive | Unlocks 3 extra mining hulls and 2 extra mining robots; starts with 2 Midget Miners already built. Mutually exclusive in effect with OBRM (see below). |
| **Improved Starbases (ISB)** | Positive | Unlocks the light-ship-capable Space Dock and the heavy Ultra Station starbase designs; starbases cost **20% less** and carry an inherent **20% cloak**. |
| **Ultimate Recycling (UR)** | Positive | Scrapping a fleet at a starbase recovers **90%** of its minerals (plus some resources) instead of the default; scrapping at a bare planet recovers about half that rate. |
| **Mineral Alchemy (MA)** | Positive | Converting resources into minerals (alchemy) is **exactly 4x** more efficient than the baseline rate available to every race (confirmed as a plain "four times," not an approximation, by the recovered trait-description string — see §3a). |
| **Generalized Research (GR)** | Negative/mixed | Only **half** of the resources allocated to the player's chosen research field are actually applied there; **15%** of the total additionally splashes onto *each* of the other five fields, for a **125%** aggregate return — confirmed exactly, including the odd total, by the game's own help text (see `research-tech-tree.md` §4; the disputed "115%" figure was a secondary source's transcription error, not a real discrepancy). |
| **No Ram Scoop Engines (NRSE)** | Negative | All free-fuel "ram scoop" engines above Warp 4 become unavailable; in exchange, the race gains the Interspace-10 engine (safe travel at Warp 10 without a high Propulsion level). |
| **Cheap Engines (CE)** | Negative/mixed | Engines cost **50% less** to build and starting Propulsion is **+1** level, but at Warp 7–10 (sources differ on whether the threshold is Warp 6 or Warp 7 — see Open Questions) there is a **10%** chance per turn the engines simply fail to engage that year. One source lists this LRT as worth "up to 80" advantage points. |
| **Only Basic Remote Mining (OBRM)** | Negative/mixed | Restricts remote mining to the basic Mini-Miner hull/robot only, but raises maximum population per planet by **+10%**. Overrides ARM if both are somehow selected. |
| **No Advanced Scanners (NAS)** | Negative/mixed | No penetrating scanners are available, but every conventional (non-penetrating) scanner gets **double** its listed range. |
| **Low Starting Population (LSP)** | Negative | Starting colony population is **30% lower** than the default. |
| **Bleeding Edge Technology (BET)** | Negative/mixed | ~~Any tech level not yet reached by *every other player* costs 2x the normal research cost, dropping back to normal cost only once the field is at least 1 level ahead of every other player's~~ — **corrected this pass, see §3a: the recovered trait-description string ties the 2x penalty to a specific item's own prerequisites, not to standing versus other players.** Any newly-reachable tech initially costs **2x** to build, dropping back to normal only once the race's own level in *every one of that item's prerequisite fields* exceeds the item's requirement by at least one level; in exchange, miniaturization of component costs/mass progresses **5% per tech level up to an 80% cap**, versus the default **4% per level up to 75%**. |
| **Regenerating Shields (RS)** | Mixed | All shields are **40% stronger** than their listed rating and regenerate **10%** of their strength between rounds within a single battle; armor is worth only **50%** of its listed rating. |

### 3a. LRT and remaining PRT text, confirmed by direct observation of the running client (2026-09-10)

Continuing the empirical-testing method already used above for War Monger/Claim Adjuster, this pass ran the actual client (same otvdm setup) far enough into the Custom Race Wizard to read the live description text for all 14 LRTs (Step 3 of 6) and the 7 PRTs not previously spot-checked (Step 2 of 6: Hyper-Expansion, Super Stealth, Space Demolition, Packet Physics, Interstellar Traveler, Alternate Reality, Jack of All Trades — War Monger and Claim Adjuster were already confirmed above, and Generalized Research's LRT text was already fully captured and quoted in the Open Questions entry below). Per this project's legal constraints, the descriptive prose itself is not reproduced verbatim here; findings are the underlying mechanical facts, paraphrased. Screenshots preserved at `docs/ui-reference/wizard-lrt-page.png`, `docs/ui-reference/wizard-prt-page.png`, and `docs/ui-reference/wizard-research-cost-page.png`.

**Every LRT and PRT's numeric modifiers in the tables above matched the client's own description text exactly**, with these specific refinements/corrections:

- **Improved Starbases'** light-hull design is named **"Stardock,"** not "Space Dock" as the secondary sources have it — a straightforward name correction.
- **Total Terraforming's** ±30% terraform range and 30%-cheaper cost are confirmed, plus a previously-undocumented mechanical detail: this LRT's terraforming capability is described as being driven by investment in Biotechnology specifically, rather than the standard (unspecified-field) terraforming mechanism.
- **Ultimate Recycling's** 90%-at-starbase / half-that-at-a-bare-planet split is confirmed exactly, plus two new details: the recovered minerals and (separately) a portion of resources are both returned, and the resources specifically become available starting the following year rather than immediately. **The deferred-resources mechanism is now confirmed at the code level** (`stars.exe.export.c`, Scrap Fleet's handler inside the segment-23 task dispatcher, `FUN_10b0_3f3a`): scrapping a fleet whose owning race carries this trait (tested via the same generic per-race trait-capability accessor used throughout the client, category index 5) is diverted into a dedicated branch that computes the recovered value from a different per-component data field than the ordinary (immediate) recovery path uses, and deposits it into a separate, per-planet **deferred-resources array** (allocated once per turn-generation pass) rather than crediting the planet's stockpile directly. That array is read back during each planet's own resource-production calculation later in the same turn-generation pass — the code that consumes it blends the deferred amount into that planet's resource output for the planets that still have it pending, which is the concrete mechanism behind "available starting the following year": by the time a deferred value is written, the current pass's own production numbers have already been finalized for the planet in question, so the credit only shows up from the next turn's production onward. This resolves what an earlier pass of this project mistook for a second, unrelated "task nibble 5" mechanic — it is Ultimate Recycling's own code path within Scrap Fleet, not a separate task.
- **Mineral Alchemy's** ~4x efficiency figure is confirmed, plus: it is explicitly usable at any planet the race owns (no facility prerequisite is mentioned).
- **No Ram Scoop Engines'** exclusive Interspace-10 engine is confirmed to reach Warp 10, with the added detail that it does so without incurring the ordinary Warp-10 damage/destruction risk documented in `fleet-movement-scanning-cargo.md` §1 — i.e. Interspace-10 is a full, unconditional exemption from that risk, not merely a reduced one.
- **Cheap Engines'** half-build-cost, +1-starting-Propulsion, and "in excess of Warp 6 → 10% chance per year the engines fail to engage" figures are all confirmed exactly, independently corroborating the identical mechanism already documented from decompiled logic in `fleet-movement-scanning-cargo.md` §1 (two independent methods — public/empirical text and decompiled control flow — now agree on both the trigger threshold and the 10% figure).
- **Interstellar Traveler's** description explicitly ties its "reduced chance of losing overgated ships" (already flagged as unquantified in `fleet-movement-scanning-cargo.md` §5) to exceeding a stargate's safety limits specifically being "less likely to kill your ships" — confirming the mechanic's existence and framing, though still not a numeric figure.
- **Packet Physics'** starting Mass Driver tier, second-homeworld bonus, and packet terraform-on-arrival chance are all confirmed as already documented; the wizard text additionally frames the terraform chance as conditional on the packet not being "fully caught" by the receiving Mass Driver.
- All other LRT/PRT entries in the tables above (Advanced Remote Mining, Only Basic Remote Mining, No Advanced Scanners, Low Starting Population, Bleeding Edge Technology's 2x-cost/5%-miniaturization-per-level/80%-cap figures, Regenerating Shields, Hyper-Expansion, Super Stealth, Space Demolition, Alternate Reality, Jack of All Trades) matched the client's own text with no corrections needed.

**Research-cost-class page (Step 6 of 6), confirmed exactly:** the six independent per-field radio groups (Energy/Construction/Weapons/Electronics/Propulsion/Biotechnology Research, each "Costs 75% extra" / "Costs standard amount" / "Costs 50% less") documented structurally in `research-tech-tree.md` were confirmed with their real control labels, plus a single previously-unconfirmed checkbox: **"All 'Costs 75% extra' research fields start at Tech 3"** — an exact-text confirmation, in the client's own words, of the base-case tech level for the "-60-point starts-at-tech-level-3 (4 for JOAT)" checkbox already reconstructed in §1a above.

Commonly documented pairings: **IFE + NRSE** (buys into the efficient Fuel Mizer engine while
letting NRSE's refund offset IFE's cost) and **ISB + RS** (favors small, shield-tanked early
warships that rarely carry armor anyway). [wiki: Lesser racial traits; GameFAQs Strategy Guide
PG3-01 through PG3-14; wiki: Chapter 3: Building a Monster Race]

### 3b. Broader line-by-line audit against the decoded dynamic string table (this pass)

The Open Questions entry below flagged only a **5-trait spot-check** (Super Stealth, War Monger,
Interstellar Traveler, Claim Adjuster, Inner Strength, plus a since-resolved Packet Physics
correction) of this document's trait text against `extracted-game-data/dynamic-strings.txt` — a
different, independent extraction method from §3a's live-client reading, and one that had not yet
been extended to any LRT. This pass reads **all 14 LRT description strings** (dynamic-string IDs
320-333, immediately following the 14 LRT name strings at IDs 306-319, `dynamic-strings.txt` lines
321-334) and the 4 remaining PRT descriptions not yet cross-checked this way (Hyper Expansion, IDs
276-278; Space Demolition, IDs 291-293; Alternate Reality, IDs 300-302; Jack Of All Trades, IDs
303-304) — 18 entries in total, well beyond a minimal sample. Per this project's clean-room rules,
findings are paraphrased mechanical facts, not verbatim reproductions, except where a short phrase
is directly load-bearing.

**Confirmed as exact matches, no corrections needed:** Improved Fuel Efficiency, Advanced Remote
Mining (exact hull/robot/Midget-Miner counts), Improved Starbases (second independent confirmation
of "Stardock," matching §3a), Generalized Research, Ultimate Recycling (second independent
confirmation of the "available next year" deferred-resources detail), No Ram Scoop Engines (second
independent confirmation of Interspace-10's unconditional Warp-10 safety), Cheap Engines (third
independent confirmation of the "in excess of Warp 6" threshold), Only Basic Remote Mining, No
Advanced Scanners, Low Starting Population (exact "30% fewer colonists" figure), Regenerating
Shields, Hyper Expansion, and Space Demolition (this string also independently re-confirms SD's
Propulsion‑2/Biotechnology‑2 starting tech from §6 and the "2 mine-laying ships" figure from
`new-game-setup.md` §5 — a third convergent source for both numbers).

**Refinements found:**

- **Mineral Alchemy** — the recovered string states the efficiency multiplier as a plain "four
  times," not a hedge — this document's "~4x" is now tightened to an exact figure (see §3 table).
- **Jack Of All Trades** — the recovered string specifies the Scout/Frigate/Destroyer built-in
  scanner is a **penetrating** scanner, a detail this document's §2 entry previously left
  unspecified (now added).
- **Regenerating Shields** — the recovered string clarifies the 10%-per-round regeneration is 10%
  of the shield's **maximum** rating specifically (not an ambiguous "its strength"); a small wording
  tightening, no numeric change.
- **Total Terraforming** — the Biotechnology-only mechanism and the 30%/30%-cheaper figures are
  reconfirmed, but the recovered string says nothing about the "±3% immediately at colonization"
  detail this document's §3 table also carries. This is not a contradiction (the string simply may
  not need to mention it, or it may come from a different, uncaptured UI surface), but it remains
  independently uncorroborated by either this pass or §3a's live-client pass — flagged rather than
  silently carried forward as settled. **Re-checked directly (later pass)** by reading dynamic-string
  ID 321 itself (`extracted-game-data/dynamic-strings.txt` line 321) rather than relying on this
  pass's own summary above — confirms the finding word-for-word: the string covers only the
  Biotechnology-only mechanism, the ±30% eventual range, and the 30%-cheaper cost, with no mention of
  an immediate at-colonization adjustment. A further, broader search across every `terraform`-related
  line in both `dynamic-strings.txt` and `extracted-game-data/message-strings.txt` (roughly 20 lines
  total) found no other string describing a fixed percentage applied at the moment of colonization,
  for this or any other trait — the closest hits are an unrelated auto-terraform-completion message
  (`message-strings.txt` ID 342, reporting an arbitrary already-reached percentage, not a fixed
  constant) and a colonization-time message about terraforming raising future population growth
  (`message-strings.txt` ID 174), neither of which describes an instant habitability adjustment. The
  ±3%-at-colonization detail remains uncorroborated (not contradicted) after two independent passes
  and a widened search; still flagged rather than removed, since this project's other LRT/PRT text
  has otherwise matched the real game text closely enough that one isolated, never-corroborated
  figure is worth continued suspicion. A future pass with access to `stars.hlp` directly, rather than
  only the dynamic-string table, would be the natural next place to check. **Third pass (this
  one), focused on colonization-event messages specifically:** every `message-strings.txt` entry
  about colonizing or settling was read. That covers IDs 0-14 (landing and ground combat), 81-94
  (colonize-order outcomes, including the "settling" artifact find), and 171-174 (new-planet
  habitability notices). None reports a habitability change at the moment of colonization. The
  only fixed-percentage environment messages are Claim Adjuster's 1% drift (ID 348) and the
  terraform/retro-bomb/auto-terraform reports (IDs 123, 189, 300-303, 342, 378-379), all of which
  are ongoing actions. Still uncorroborated. One possibly related structural fact, recorded
  without drawing a conclusion: the race-wizard sampler's first terraforming tier (§1b) models
  Total Terraforming as exactly **3 clicks** more reach than a normal race (8 versus 5). That
  would be a natural seed for a garbled "±3%" claim in a secondary source, but nothing in the
  client ties it to colonization.

  **Fourth pass (this one): resolved as a misreading, using a new angle. The component data was
  checked instead of the strings.** Category `0x2000` (Terraforming) in
  `extracted-game-data/component-stats.tsv` has 20 entries. Subtypes 0-7 are "Total Terraform ±3,
  ±5, ±7, ±10, ±15, ±20, ±25, ±30". Subtype 0, **Total Terraform ±3**, requires **0** in every
  tech field; subtypes 1-7 need Biotechnology 3, 6, 9, 13, 17, 22, and 25. Every other race's
  terraform items need Propulsion, Energy, or Weapons, plus Biotechnology 1-4, and start at the
  per-axis ±3 tier.

  The category resolver `FUN_1008_5194` (`stars.exe.export.c:3583`-`3594`) makes subtypes 0-7
  unavailable unless the race has trait bit 1, which is Total Terraforming (§1a/Open Questions).
  Two list builders then **skip** the per-axis ±3 items (subtypes 8, 12, and 16) for a TT race,
  because the total item replaces them: `:80211` and `:89039`.

  So a Total Terraforming race can terraform every environment axis by up to 3% toward its ideal
  from the very first turn. It needs no research, and the change applies to any world it owns,
  new colonies included. This is the concrete, code-level fact behind the community "±3%
  immediately" wording. It is still an ordinary queued terraform action. It costs resources at the
  TT rate of 70 instead of 100 (§1a Open Questions, the `FUN_10d0_221a` item), and it is not an
  automatic change at colonization. No colonization code reads trait bit 1: the five reads of
  selector 1 anywhere in the executable are at `:3590`, `:80211`, `:87856`, `:89039`, and
  `:94508`, and all of them concern terraforming items, costs, or the race-wizard sampler. The
  sampler's first-tier allowance, 8 against 5, fits the base 5 plus the tech-free ±3. Its
  second-tier increment is only +2 (17 against 15), so that tier is not fully explained this way.

  §3's table row is corrected accordingly. A live check was not attempted this pass.

**One substantial correction, resolving a discrepancy this project had not previously noticed
between its own two documents:**

- **Bleeding Edge Technology.** This document's own §3 table stated the 2x research-cost penalty
  applies to "any tech level not yet reached by *every other player*," sourced from the community
  wiki. The recovered trait-description string (ID 332) instead frames the penalty exactly the way
  `research-tech-tree.md` §3 already does — as costing double until *the race's own level in every
  prerequisite field exceeds that item's own requirement by at least one level* — with no mention
  of other players' standing anywhere in the sentence. This matches, independently, the code-level
  mechanism this document's own §7 (`FUN_1050_7d44` step 7) already traced: that doubling is gated
  on the race's own miniaturization progress against the *component's* requirement, never on a
  comparison against other empires. Three independent sources (this recovered string,
  `research-tech-tree.md`'s own reading, and this document's own code trace) now agree with each
  other and against the community-wiki-sourced "versus every other player" framing this document's
  §3 table previously carried unchallenged; §3's BET row is corrected accordingly. Notably, §3a's
  own earlier live-client pass had already read this same text and reported "no corrections
  needed" — that pass verified the **numbers** (2x, 5%, 80%) matched but did not catch that the
  **trigger condition's wording** differed from this document's own table; this is a documentation
  gap within this project's own prior passes, not a new discrepancy in the source material itself.

**New omissions found (not corrections, but details this document's tables previously lacked
entirely):**

- **Alternate Reality** — the recovered string (ID 301) states that **if a starbase is destroyed,
  every colonist orbiting that world is killed** — a previously undocumented risk, now added to
  §2's AR row. The same string also confirms AR's "intrinsic ability... to scan for enemy fleets,"
  independently corroborating this document's §5 finding that AR has its own population-driven
  scan-range formula.
- **Inner Strength** — this document's Open Questions entry below already identified, but had not
  yet folded into §2's actual table, that Inner Strength can lay **Speed Trap** minefields
  (confirmed again directly against the recovered string, ID 289, this pass); §2's IS row now
  includes it.

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
about 723 more on top of the first). [wiki: Chapter 3: Building a Monster Race] **These are not
game constants (§1b).** The real cost depends on the baseline race. For the default Humanoid it is
572 points for one immunity, 1,730 for two, and 3,925 for all three, and in v2.70j a second
immunity always costs well more than the first.

**Habitability and availability are distinct calculations.** A planet's actual habitability is
not a simple product of three normalized axis values; the per-planet formula, including its
nonlinear edge penalty, belongs to the population-growth specification. Immunity makes that axis
contribute its best value to the per-planet calculation.

Separately, the race designer shows an estimate of how common compatible worlds will be. That
display multiplies three distribution-coverage factors: the first two axes use a middle-weighted
distribution and the third is uniform. This observed client calculation is specified in
`race-designer-ui-and-availability.md`. It must not be substituted for the per-planet
habitability formula.

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

(The exact machine-level form, validated live, is in §5a. It adds a Planet Value floor of 25%, an Energy floor of 1, a round-up, half weighting for over-capacity colonists, and a minimum of 1.)

where `EfficiencyCoefficient` is the same Step-5 "factory efficiency" dial every race sets, and
`EnergyTechLevel` substitutes for the role Energy tech plays nowhere else in the standard formula.
This is the clearest example in the source material of a PRT literally swapping out a formula owned
by another subsystem (production) rather than just multiplying its output. AR's related scanning
range formula is also distinct: `ScanRange = sqrt(Population / 10)`. [GameFAQs Strategy Guide
ANR-00]

**The "no literal `sqrt` anywhere in the decompile" finding is resolved this pass — the game does
use an inline square root, exactly as this document's own Open Questions entry speculated, just
not one Ghidra could render as a named call.** Both single-operand floating-point operations in
this executable (this being 16-bit code, arithmetic on non-integer values goes through the x87
coprocessor, or a software emulation layer standing in for one) are issued through one shared
two-call idiom used at roughly 90 call sites across the whole binary: a "dispatch the pending
unary FPU op" function (three thin wrappers over one real implementation, keyed to how many
operands are already sitting on the FPU stack) immediately followed by a second function whose
entire body is "round the top-of-stack value to an integer and return it." Ghidra has no way to
know, from the call site alone, *which* single-operand FPU instruction (`FSQRT`, `FABS`, `FRNDINT`,
etc.) a given occurrence of this pair represents — it shows up identically every time — which is
exactly why a plain-text search for `sqrt` across the exported source turns up nothing, and why
this document's prior pass could only report a negative result. **One concrete, PRT-8-gated
occurrence of this pair is now identified with high confidence as AR's own scan-range formula
already cited above.** It lives in a function (segment 8) that computes a ship or fleet's
effective scan range: the non-AR branch resolves the best planetary-scanner-category component
installed (the same category/subtype resolver this project's `ship-design-and-components.md`
already documents) and doubles the result if the race carries the No Advanced Scanners trait bit
(an exact, independent match for this document's own §3 NAS entry, found sitting in exactly the
place NAS's "double range" bonus would need to be applied); the branch gated on PRT index 8
(Alternate Reality) instead runs the population value through the shared FPU-op pair described
above and rounds the result — the concrete shape of `ScanRange = sqrt(Population / 10)`, now
confirmed at the code level rather than resting on the strategy guide alone.

**A second, structurally identical PRT-8-gated occurrence of the same FPU-sqrt idiom was found in
a different function**, one that sums a per-planet numeric value across every planet a race owns
(called from a race-wide aggregator). Its non-AR arithmetic — a bisection-style refinement of a
mineral/cargo quantity against a design's carrying capacity, finished with a "+9, then divide by
10" round-up-to-the-next-integer idiom — reads more like a mineral-surplus-transport or
turns-to-clear-backlog estimate than an annual resource-production total, so this document is
**not** claiming this second site is the home of the `Resources = HabitabilityValue x
sqrt(Population x EnergyTechLevel / EfficiencyCoefficient)` formula boxed above. What it does
establish is that AR-gated-sqrt is a real, repeated, deliberate substitution pattern in this
codebase — found live, executing, in more than one place — not a one-off.

**The home function is now found (later pass): it lives in segment 10 (`FUN_1048_*`), not segment 8, which is exactly why every earlier segment-8-only search came up empty.** `production-queue.md` independently cites `FUN_1048_4faa` (`stars.exe.export.c:27950`-`28005`) as the per-planet annual resource-production routine — it is called once per owned planet from the segment-24 turn-generation summary loop (`FUN_10b8_0000`/`FUN_10b8_0756`) and computes each planet's factory-derived resource income for the turn. Reading it directly confirms it is exactly this document's missing AR-resources home function: after an initial mine/capacity-interpolation step, it reads the owning race's PRT (`FUN_10e0_222c(..., 0xe)`, `stars.exe.export.c:27982`) and explicitly branches:

In outline: when the PRT is 8 (Alternate Reality), the routine calls the per-planet habitability evaluator `FUN_1048_490e` and then the shared FPU-sqrt idiom (`FUN_1120_0dc2` followed by `FUN_1120_0e40`); for every other PRT it takes the lesser of the planet's operable and built factory counts and applies the standard resources-per-10-factories slider. (`stars.exe.export.c:27983`-`28000`.) The `else` branch is visibly the ordinary factories-times-the-Step-5-"resources per 10 factories"-slider formula (matching this document's §5 economic-slider description), confirming the function's identity beyond `production-queue.md`'s own citation. The `if (PRT==8)` branch takes the now-familiar two-call FPU-sqrt idiom (`FUN_1120_0dc2` then `FUN_1120_0e40`) immediately after calling `FUN_1048_490e` — the exact same habitability function `population-growth.md` §2 has since identified and fully documented (segment 10, `stars.exe.export.c:27571`-`27642`). `FUN_1048_490e`'s own return value is discarded by the caller at this call site; because the x87 floating-point stack is a shared machine resource that this codebase's Ghidra output cannot show being threaded between calls (the same limitation already documented for the scan-range and second-sqrt sites above), the most plausible reading is that `FUN_1048_490e` leaves an intermediate floating-point value on the FPU stack as a side effect of its own internal computation, which the caller's immediately-following `FUN_1120_0dc2`/`FUN_1120_0e40` pair then consumes to produce the final `sqrt(...)`-and-round result — rather than `FUN_1048_490e` being called here purely for its C-level return value.

**This resolves the "which segment" half of the search, but not the exact operand list.** `FUN_1048_490e` is confirmed to be a habitability/environment-tolerance evaluator, not a plain population accessor, so the boxed formula's exact claim — that AR resources scale with `Population x EnergyTechLevel / EfficiencyCoefficient` specifically, with `HabitabilityValue` multiplying the result afterward — was not independently re-derived from this branch's own arithmetic this pass; only the branch's existence, its PRT-8 gating, and its use of the project's confirmed sqrt idiom immediately after a habitability-shaped call are confirmed. A future pass should trace `FUN_1048_490e`'s exact inputs at this specific call site (`stars.exe.export.c:27984`) and whatever value(s) end up on the FPU stack when it returns, to pin down the operand(s) the sqrt actually consumes. Net effect on confidence: the formula's *home function* is now a fact, not a guess — closing the "not conclusively located" gap below — while the formula's *exact* multiplicands remain open.

**The exact operand list is now confirmed — not at the FPU-trace level, but by the game's own displayed formula text, a stronger source than the strategy guide this boxed formula was originally credited to.** `extracted-game-data/dynamic-strings.txt` IDs 260-261 (file lines 261-262) hold the literal Step-5 economic-slider page text substituted in for a standard race's five economic controls when the designer's PRT is Alternate Reality: a template reading (paraphrased minimally, since this is a recovered formula string rather than prose) `Annual Resources = Planet Value * sqrt(Population * Energy Tech / [EfficiencyCoefficient value])`. This is a verbatim, in-game confirmation — from the wizard screen itself, not a secondary strategy guide — that **Population**, **Energy Tech**, and the Step-5 efficiency dial (**EfficiencyCoefficient**) are exactly the sqrt's operands, with "Planet Value" (this project's `HabitabilityValue`) multiplying the result afterward, precisely matching this document's boxed formula's shape. A second, independent, simpler string exists elsewhere in the same table (ID 242: "Your resources at the planet are equal to the square root of its population") which drops Energy Tech and the coefficient — read as a simplified summary sentence for a different UI surface (most likely a planet-report tooltip) rather than a conflicting formula, since it is consistent with, not contradictory to, the fuller Step-5 template.

**Attempted this pass: reconciling that confirmed formula against the code-level FPU-blind-spot from the paragraph above — partially successful, still not resolved end-to-end.** *(Superseded by §5a. The raw machine code shows that `FUN_1048_490e`'s return value is used, not discarded, and that Energy tech and economic field 0 are both read. The "discarded" and "never read" statements here and in the paragraph above are wrong. The `local_a` population reading is right.)* Re-reading `FUN_1048_4faa`'s AR branch (`stars.exe.export.c:27950`-`28005`) alongside `FUN_1048_490e` in full:

- `FUN_1048_490e` itself (`27571`-`27642`) contains its *own*, self-contained second occurrence of the shared FPU-sqrt idiom, used only on its "planet fully within tolerance on every non-immune axis" return path (`27634`-`27639`): it multiplies that idiom's rounded result by `local_14` (the running per-axis habitability-multiplier product this document's population-growth cross-reference already establishes is seeded at 10000 = 100.00%) and divides by 10000, returning the result as a plain C `int`. Because this is a complete, self-contained `return` statement, `FUN_1048_490e`'s own use of the idiom cannot be the source of whatever pending FPU value `FUN_1048_4faa`'s *separate*, subsequent idiom call (immediately after `FUN_1048_490e`'s return is discarded) goes on to consume — that would require the FPU stack to still hold something after a fully-resolved C-level `return`.
- Comparing this call site against a control example elsewhere in the codebase where the same idiom's operand *is* legible (`stars.exe.export.c:39719`-`39720`, `local_f0 = (double)local_20 / (double)local_1e;` immediately followed by `FUN_1120_0dc2();` with no argument threading shown) establishes the idiom's general convention: Ghidra renders the operand as an ordinary preceding floating-point C statement when it can, and the idiom call itself never carries an explicit argument in the decompile. At the AR resources call site, there is no such statement anywhere between `FUN_1048_490e(...)` and the following `FUN_1120_0dc2()` — a stricter, more specific confirmation of the previously-documented decompiler gap than "the return value is consumed via the FPU stack": there is no visible C expression of any kind supplying this operand, discardable return value or otherwise.
- One concrete, well-motivated candidate for at least the **Population** operand was identified: `local_a`, a 32-bit `long` computed unconditionally just before the PRT branch (`27968`-`27981`) as the planet's actual population (`*(long *)(param_1 + 0x14)` — confirmed, by byte-offset cross-reference against `FUN_1038_47d0`'s independently-documented population read in `population-growth.md` §3, to be planet-record offset `0x28`, i.e. population — a match, since `FUN_1048_4faa`'s `int *param_1` typing makes `param_1 + 0x14` an offset of 20 *int* elements = 40 bytes = `0x28`) clamped to the lesser of population and the race's capacity ceiling via a bisection-style interpolation. `local_a` is computed but then never read in the AR branch at the C level (only the `else` branch uses it) — making it the sole substantial, unused, population-shaped local variable in scope at exactly the point the sqrt idiom runs, and hence the strongest available circumstantial candidate for the operand — but this remains a plausibility argument, not a proof, since (like every other occurrence project-wide) Ghidra shows no instruction actually feeding it to the FPU stack.
- **A meaningful negative result:** neither Energy tech level nor any Step-5 economic-slider field (including the field-0 "colonists per resource" value read earlier in the function at `27967`, which is silently overwritten and discarded by the AR branch without ever being used) is read anywhere in `FUN_1048_4faa`'s own visible AR-branch code or in its sole callee, `FUN_1048_490e`. If EnergyTechLevel and EfficiencyCoefficient genuinely enter this specific computation — and the recovered wizard string above says they must — they can only be doing so through the same invisible FPU channel already responsible for the missing sqrt operand, not through any ordinary field read this decompile can show.

**Conclusion (superseded by the next sub-section):** the *what* (the formula's operands) was settled by the recovered in-game string. The *how* could not be recovered from the exported `.c`/`.h` decompile alone; raw disassembly was needed.

#### 5a. The AR resource formula, read from raw machine code and validated live (this pass)

**Technique.** This used the same approach that cracked §1b. `FUN_1048_4faa` sits in NE segment 10 (selector `0x1048`), whose segment-table entry gives file offset `0x30300` and length `0x8888`. The function's bytes are at file `0x30300` + `0x4faa`, running to `0x5137`. They were disassembled directly, with the segment's relocation table applied so that far calls and FPU-emulator fixups could be told apart. Ghidra's decompile had dropped the whole AR computation: the x87 instructions, one direct race-record byte read, and two data-segment constants.

**What the AR branch really does (file bytes `0x3535b`-`0x353b3`, segment offsets `0x505b`-`0x50b3`).** In order:
1. **Energy tech.** It reads one signed byte straight from the owning race's record, at record offset `0x1a` (absolute DS `0x59dc` + player × `0xc0`). The decompile shows no expression for this read. The byte is confirmed to be Energy tech by the galaxy-setup starting-tech switch (`stars.exe.export.c:50852`-`50888`). That switch writes 4 to this byte for Packet Physics and 1 for Alternate Reality, and 3 to all six bytes from here to `0x59e1` for Jack of All Trades. This matches §6's table exactly. The six bytes in order are Energy, Weapons, Propulsion, Construction, Electronics, and Biotechnology. The value is floored at 1.
2. **Planet Value.** It calls `FUN_1048_490e`, and its return value is **kept, not discarded**. The earlier reading here was wrong. The value is the planet's habitability percentage for the owner. It is **floored at 25**, so a planet worth less than 25%, including a negative-value one, still produces as if it were worth 25%. This floor is new; no string or secondary source mentions it.
3. **Population.** The operand is the 32-bit local already computed before the branch (`local_a`, planet-record offset `0x28`). The earlier circumstantial reading was right. Population is stored in **units of 100 colonists**. The homeworld is seeded with 250, or 175 under Low Starting Population (`stars.exe.export.c:50956`-`50961`), which is 25,000 or 17,500 colonists. Before the PRT branch, and for every PRT, the stored population is adjusted against the planet's capacity from `FUN_1048_4a8e`. If population P exceeds capacity C, the routine uses C + (P − C) / 2, capped at 2C. Colonists above capacity therefore count at half weight, and nothing counts beyond three times capacity.
4. **Coefficient.** The divisor is race economic field 0, read at `27967` for every PRT. The earlier "silently discarded" reading was wrong: the AR branch uses it. For a normal race this field is colonists per resource (stored as 7-25, displayed ×100). For AR it is the Step-5 EfficiencyCoefficient, shown unscaled (range 7-25, default 10).
5. **The x87 sequence.** It loads the population as an integer, divides by the coefficient, multiplies by Energy tech, and takes the square root through `FUN_1120_0dc2`. It then multiplies by the floored Planet Value, multiplies by the double at DS `0x1d12`, adds the double at DS `0x1d1a`, and truncates toward zero through `FUN_1120_0e40`. The division is floating-point, so nothing is truncated before the root.
6. **Minimum.** A result of 0 is raised to 1. This shared ending applies to both branches.

**The two constants.** Both are ordinary DS-relative operands, with no CS override. They were read from DGROUP (segment 38, file offset `0xb0bc0`), and this project's earlier live data-segment dump holds identical bytes:

| DS offset | Value | Role |
|---|---|---|
| `0x1d12` | **0.1** | scale factor |
| `0x1d1a` | **0.999** | offset added before truncation (an effective round-up) |

They sit immediately after the 1/3 and 0.9 used by `FUN_1048_490e` (§1b).

**Exact formula.** Let P be the adjusted population in hundreds, c the coefficient, E the Energy tech level, and H the Planet Value in percent:
- Annual resources = ⌊ √( P ÷ c × max(E, 1) ) × max(H, 25) × 0.1 + 0.999 ⌋, and at least 1.
- In real colonists this is exactly the wizard's formula text: (H ÷ 100) × √(colonists × E ÷ c), rounded **up** unless the fractional part is under 0.001.
- Example: 25,000 colonists (P = 250), c = 10, E = 1, H = 100 gives √25 × 100 × 0.1 = 50.0, plus 0.999, truncated to **50**.

**Live validation (otvdm-master-2697, Stars! 2.70j).** Two single-player Tiny games were started, each with a custom Alternate Reality race. Each race was the Humanoid preset switched to AR, plus No Advanced Scanners to keep the point total positive; the wizard showed −27, then 81, as §1a predicts. The homeworld showed "Value: 100%" in both games. Its "Resources/Year … of N" total and population were read each turn, with Energy tech confirmed in the Research dialog. All **12 of 12** readings matched:

| Coefficient | Population | Energy | Predicted | Live |
|---|---|---|---|---|
| 10 | 25,000 / 28,700 | 1 | 50 / 54 | 50 / 54 |
| 10 | 33,000 / 38,000 | 2 | 82 / 88 | 82 / 88 |
| 10 | 43,700 / 50,200 | 3 | 115 / 123 | 115 / 123 |
| 10 | 57,700 | 4 | 152 | 152 |
| 17 | 25,000 / 28,700 / 33,000 | 1 | 39 / 42 / 45 | 39 / 42 / 45 |
| 17 | 38,000 / 43,700 | 2 | 67 / 72 | 67 / 72 |

Seven of these readings rule out ordinary rounding, which predicts 81, 87, 114, 38, 41, and 44 where the game shows 82, 88, 115, 39, 42, and 45. One more rules out plain truncation: 53 against a live 54. The coefficient-17 game confirms the coefficient is the divisor. The Energy steps from 1 to 4 confirm Energy is a multiplier inside the root. The research readings came out at Energy 4 after six turns of the coefficient-10 game and at Energy 2 after four turns of the coefficient-17 game.

**Not live-tested.** The 25% Planet Value floor and the over-capacity half weighting both come from the machine code alone. Testing them would need an AR colony on a low-value world or an overcrowded starbase.

**By-product for the non-AR branch** (for `production-queue.md`): the ordinary formula is ⌊P ÷ colonists-per-resource field⌋ + ⌈operable factories × factory-output field ÷ 10⌉, with the same capacity-adjusted P and the same minimum of 1. The ceiling comes from adding 9 before dividing by 10.

#### 5b. "Conversion ratio" and "threshold" — checking against §5a, and against a genuinely separate AR mechanic

The project owner flagged a possible gap: an "unspecified per-race conversion ratio and threshold" for AR. Checked against everything §5a already established:

- **None of §5a's own figures are a "conversion ratio" or "threshold" in the sense that phrase would naturally mean.** `EfficiencyCoefficient` (race field 0, 7-25, default 10) is a *divisor inside a square root*, not a ratio applied to a leftover quantity; the 25%-Planet-Value floor is a *floor*, not a threshold gating whether something happens at all; the 0.1/0.999 FPU constants are a fixed scale-and-round-up pair, not race-customizable. None of these terms were being used loosely for something already documented — they are genuinely different concepts, so no terminology cross-reference was needed here.
- **The actual match is a different, already-partially-documented AR mechanic: the Mineral Alchemy auto-conversion in `turn-generation-engine.md` §6.** That section already states, in almost the project owner's own words, that AR races get "an automatic, non-queued per-turn conversion: any leftover planetary resources beyond a **threshold** are converted directly into minerals at a race-specific **ratio** (`resources * ratio / 100`, where `ratio` is a per-race stored byte)". This is almost certainly what "conversion ratio and threshold" refers to — but that section never pinned down a function name, byte offset, or the actual numeric threshold/ratio values, and this project's own cross-reference index did not previously connect it to this document. **This pass's attempt to locate and resolve that function's exact numbers is reported in `turn-generation-engine.md` §6** rather than duplicated here, to keep one authoritative location; see that section for the result (resolved figures, or an honest negative if the function could not be pinned down this pass).
- **A genuinely new, fourth PRT-8-gated site was found while searching for the above.** `population-growth.md` §3 already documents `FUN_1048_4a8e` (the per-planet population-capacity function) as reading a per-hull-type capacity table for Alternate Reality, but had explicitly flagged that table's real byte values as "not extracted." **This pass extracted them** (DGROUP-relative, file offset `0xb0bc0 + 0x80c`, keyed by a hull-type ordinal that runs 0-31 for ship hulls then 32-36 for the 5 starbase chassis): Orbital Fort → 2,500, Space Dock → 5,000, Space Station → 10,000, Ultra Station → 20,000, Death Star → 30,000 colonists, terminated by a `0xFFFFFFFF` sentinel — see `population-growth.md` §3 for the full derivation and cross-checks. `FUN_1048_4a8e`'s AR branch (`stars.exe.export.c:27664`-`27670`) checks `FUN_10e0_222c(race, 0xe) == 8` directly and contains no FPU-sqrt idiom of its own, so it is confirmed structurally distinct from — and a genuine fourth instance alongside — the three PRT-8-gated sites already catalogued in §5a above (the resource formula, the scan-range formula, and the still-unidentified second sqrt-idiom site in the mineral/cargo-capacity function). A project-wide sweep of every `FUN_10e0_222c(..., 0xe) == 8`-shaped comparison (60+ raw hits, most unrelated to AR-specific formulas — e.g. race-wizard UI greying-out logic, save-file defaults, and unrelated per-PRT branching already covered elsewhere) did not surface a *fifth* distinct formula-substitution site beyond this one; the search is not claimed exhaustive given the volume of `== 8` matches project-wide, but no further candidate stood out.
- **`dynamic-strings.txt`/`message-strings.txt` searched broadly this pass** for "Orbital", "Habitat", "convert"/"Convert", "efficiency"/"Efficiency", "ratio", and "threshold" beyond the IDs already cited (260-261, 242, 300-302). Nothing new turned up describing a distinct AR conversion or threshold concept — AR's own flavor text (IDs 300-302) only mentions living in orbit, remote-mining own worlds, starbase-determined population maximums, and the Death Star, with no textual hint of the resource-to-mineral auto-conversion mechanic at all. This is consistent with that mechanic being silent/automatic (no player-facing message was found for it either — see `turn-generation-engine.md` §6's own note), rather than any string having been missed.

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

### 7. Ship-design cost modifiers, found by inspection of the exported client

*(Resolved at the end of this section. The paragraph below is an early, garbled summary of `FUN_1050_7d44`, and no separate routine exists. It is kept for the record.)* Distinct from the point-total formula (§1a) and the per-field research-cost sliders (§6), a separate general-purpose "compute this race's adjusted build cost for a hull design" routine was found: it takes a design's base resource/mineral costs and applies, in sequence, a habitability-based cost multiplier for certain hull/tier combinations, then flat roughly-±25% adjustments keyed to specific hull-type categories crossed with specific race-trait checks, and finally doubles the cost outright if the design's habitability is non-positive and a specific trait is absent. This is a previously undocumented cost-modifier layer that a ship-design cost preview would consult — it modifies the *build cost of a specific design*, separate from the racial economic sliders in §5 that set the *base* factory/mine/component costs those designs are built from. The exact hull-type categories and trait indices involved were identified only numerically, not matched to named hulls/traits with full confidence — see Open Questions.

**Follow-up this pass — the "same function as `ship-design-and-components.md` §8?" question is now partially answered: they are two different functions, one directly calling the other.** Tracing `ship-design-and-components.md` §8's "cost estimator gated by a race-trait boolean flag (trait index 12)" precisely (it is `FUN_10f0_50e0`, segment `10f0`, confirmed by finding the exact trait-12 read the earlier pass described: `FUN_10e0_226e(..., 0xc)` on the owning race's record) shows that when the gate is set, the function does **not** apply an inline ±25%-style percentage transform itself — instead it copies the design's per-slot data and calls out to a **different, previously unattributed function in segment 8**, `FUN_1038_2df8`, to recompute the copy's cost/value totals before using them. `FUN_1038_2df8` turns out to be a much more general routine, called from over a dozen sites across the client — cross-referencing it against `ship-design-and-components.md` §9 shows it is the same routine described there as populating each design's **cached per-design value/combat-power fields**, not a bespoke ±25%-adjustment routine. Its actual internal logic (accumulating per-slot cost/value contributions with a few category-specific scaling rules, including one case that **halves** a specific category's contribution when the design's own "type" tag equals 7 *and* the owning race has trait bit 13 set) does not resemble this section's "habitability multiplier, then ±25% hull-category-crossed-with-trait adjustments, then non-positive-habitability doubling" shape at all. **Conclusion: `FUN_10f0_50e0` (the §8 AI cost estimator) and this section's own routine are confirmed genuinely different mechanisms, connected only in that the former calls a third function (`FUN_1038_2df8`, itself §9's cache-populator) rather than either being a disguised copy of the other.** This section's own "adjusted build cost for a hull design" routine — the one with the habitability multiplier and ±25% hull/trait terms — was not itself relocated this pass; see Open Questions.

**A further, bounded search this pass also failed to relocate it, a negative result worth recording.** Segment `10d0` (this project's segment 27, host of the production-queue cost calculator `FUN_10d0_221a` already documented in `production-queue.md`) was checked in full — it contains only 12 functions total, and its one other large function, `FUN_10d0_2a10`, was read end-to-end and is a "years to complete at current production" turn-simulation loop (iterating up to 99 simulated turns), not a cost-multiplier routine. A keyword-style scan for the routine's likely code shape (a call to the generic per-race boolean-trait accessor `FUN_10e0_226e` followed, within the same function, by both a `×2`-style doubling and a 25%-scale literal) was run against all 62 call sites of `FUN_10e0_226e` across the whole executable and did not surface a clean match either (one hit, inside `FUN_10d0_221a`'s already-documented Defenses cost-class switch, uses a 25/100 pair for an unrelated base-resource-cost lookup, not a cost multiplier applied to a hull design). This section's own routine remains unlocated; the search is not considered exhaustive (`FUN_10e0_226e`'s 62 sites were pattern-matched, not all individually read in full), but the two most likely candidate locations (segment 27 in full, and the trait-accessor idiom search) are now ruled out.

**A strong new candidate, found via a different investigation (tracing why Stargate/mass-driver costs looked doubled — see `ship-design-and-components.md` §15).** The shared build-cost quadruplet routine `FUN_1050_7d44` (`stars.exe.export.c:36923`) applies exactly this section's described shape — a flat ~25% reduction (`x - x>>2`), keyed to specific component categories crossed with a specific PRT check on the owning race — confirmed concretely for two cases: Stargates (category `0x0200`, subtypes 0-6 only) reduced 25% for Interstellar Traveler (PRT 7), and a parallel block applying the same ±25% to Beam/Torpedo/Bomb categories keyed to other PRT values. This is a component-category-crossed-with-PRT adjustment, not literally a "hull-type category" adjustment as this section's opening paragraph describes it — close enough in shape to be a promising match, but not confirmed identical to the "habitability multiplier, then ±25% hull-category adjustments, then non-positive-habitability doubling" routine this section originally described, since `FUN_1050_7d44` doesn't appear to touch habitability at all. Worth a focused follow-up pass reading `FUN_1050_7d44` in full to check whether it's actually the same routine under a broader description, or a genuinely separate, third cost-adjustment mechanism.

**Follow-up pass — `FUN_1050_7d44` read in full (`stars.exe.export.c:36923`-`37092`, all 170 lines): confirmed as a genuinely different routine, with no habitability step anywhere in its body.** The function's complete shape is:

1. **Stat-buffer copy** (`36939`-`36955`): copies a component's four base cost fields (3 mineral costs plus a resource cost) into the caller's working buffer; returns immediately, with no further adjustment, when the requesting race index is the generic "no race" sentinel (`-1`).
2. **Miniaturization discount** (`36956`-`37016`) — **not a habitability multiplier.** For component categories outside a small excluded-subtype range, it computes, per tech field, the race's current tech level minus that field's requirement for this component (skipped fields excluded), takes the *minimum* surplus across all six fields (clamped to a ceiling of 19), and converts that into a percentage discount applied to all four cost fields — **4% per level of surplus, capped at 75%,** or **5% per level capped at 80%** when the owning race carries trait bit `0xc` (`stars.exe.export.c:36984`-`36996`). Trait bit `0xc` is index 12 in this project's established 14-entry LRT wizard-order table (§1a), i.e. **Bleeding Edge Technology** — an exact match for this document's own §3 BET entry ("miniaturization... 5% per tech level up to an 80% cap, versus the default 4% per level up to 75%"). This is the concrete home of that already-documented miniaturization curve, not previously pinned to a function.
3. **Stargate 25% reduction for Interstellar Traveler** (`37017`-`37031`) — already documented above.
4. **Beam/Torpedo/Bomb ±25%** (`37032`-`37053`) — component categories `0x10`/`0x20`/`0x40` cost 25% less under War Monger (PRT 2) and 25% more under Inner Strength (PRT 4), matching this document's §2 entries for both PRTs exactly.
5. **Terraforming category halved for Claim Adjuster** (`37054`-`37058`) — component category `0x2000` (Terraforming) has its cost fields right-shifted by 1 (a flat 50% reduction) when the owning race's PRT is 3 (Claim Adjuster) — a previously unattributed confirmation of this document's §2 "terraforming is free and instantaneous" framing at the build-cost level.
6. **Engine category halved for trait bit 8** (`37059`-`37069`) — component category `1` (Engines) has its cost fields halved (`x - x>>1`) when the owning race carries trait bit `8`. Trait bit 8 is index 8 in the same 14-entry LRT table used for bit `0xc` above — **Cheap Engines** — an exact match for this document's own §3 CE entry ("Engines cost 50% less to build"), not previously tied to a function.
7. **A no-miniaturization-yet doubling step** (`37070`-`37091`, label `LAB_1050_7fc7`) — if the miniaturization percentage from step 2 came out non-positive (i.e. the race hasn't exceeded the component's requirement in *any* field yet) **and** the race carries trait bit `0xc` (BET again) **and** a global flag is clear, and at least one tech field is actually required by the component, all four cost fields are doubled outright. This is gated on the *miniaturization* value being non-positive, not on habitability — the "non-positive-habitability doubling" phrase in this section's opening paragraph does not describe anything in this function; the closest analogue found is this BET-specific "no head start yet" doubling, a different trigger entirely.

**Conclusion: `FUN_1050_7d44` is definitively ruled out as this section's habitability-multiplier routine.** Its complete body is a **component miniaturization-and-trait-cost-adjustment** function — tech-surplus-based miniaturization (with BET's steeper curve), four PRT/LRT-specific category-crossed cost adjustments (IT Stargates, WM/IS weapons, CA Terraforming, CE Engines), and a BET-specific no-miniaturization doubling — not the "habitability multiplier, then ±25% hull-category adjustments, then non-positive-habitability doubling" routine originally described here. This section's own routine remains genuinely unlocated; the negative result from the segment-27/trait-accessor search above still stands, and `FUN_1050_7d44` is no longer a live candidate to re-check.

**A new search filter this pass: habitability read plus hull/category read in the same function body.** This combination had not been used before. The search enumerated every function that calls the per-planet habitability evaluator `FUN_1048_490e`, or any of its three thin wrappers (`FUN_1048_47ec`, `FUN_1048_4a8e`, `FUN_1048_4ed0`). That gives 31 functions, counting the wrappers themselves, across segments 6, 8, 10, 12, 15, 16, 18, 19, 20, 22, 24, 25, 27, 29, and 34. Each was checked for a call to the component/hull category-subtype resolver `FUN_1008_5194`. Only three have both:
- `FUN_1078_1334`: galaxy generation (§2a).
- `FUN_10d0_010c` (`stars.exe.export.c:86077`): the production-queue "available items" list builder. It enables Terraform entries from a habitability-derived value, but prices nothing.
- `FUN_1108_5b5a` (`:112288`): a text/report formatter that prints a planet's value.

None has the described multiplier/±25%/doubling shape. **Result: negative. No function in the executable combines a habitability read with a hull-category read in a cost computation.**

**Working hypothesis, offered rather than asserted:** this section's opening description is most likely an earlier, garbled summary of `FUN_1050_7d44` itself. Its three stages map onto that function's three stages one-for-one:
1. The "habitability multiplier for certain hull/tier combinations" corresponds to the tech-surplus miniaturization discount for most categories.
2. The "±25% by hull category crossed with trait" corresponds to the component-category × PRT ±25% blocks.
3. The "doubling when non-positive and a trait is absent/present" corresponds to BET's doubling when miniaturization is non-positive.

If so, there is no missing fourth routine, and the opening paragraph should be read as superseded by the `FUN_1050_7d44` breakdown above.

**CONFIRMED (this pass): the opening paragraph is a garbled summary of `FUN_1050_7d44`, and no separate routine exists.** `FUN_1050_7d44` was re-read in the decompile (`stars.exe.export.c:36923`-`37092`), and its category tests were checked against the raw bytes (segment 11, file `0x38e80` + `0x7d44`). The check went claim by claim through the opening paragraph:
- **"Takes a design's base resource/mineral costs."** It copies an item's three mineral costs and its resource cost. Hulls and starbase chassis are ordinary items in this category scheme (`0x4000`, `0x0400`), so "hull design" is a narrower but compatible description. *Accounted for.*
- **"Habitability-based cost multiplier."** The six bytes the discount stage reads are at race-record offsets `0x1a`-`0x1f`, the Energy-through-Biotechnology tech levels (§5a). The race's habitability block is at `0x10`-`0x18`, read by `FUN_1048_490e` as center, low, and high triples at DS `0x59d2`, `0x59d5`, and `0x59d8`. The two blocks are one byte apart and are addressed the same way in the decompile, so mistaking one for the other is an easy error. The stage is a tech-surplus percentage discount, so "multiplier" is fair. *Accounted for as a mislabel.*
- **"For certain hull/tier combinations."** The discount is skipped for all of category `0x2000` (Terraforming) and for category `0x8000` subtypes 0-8 and 9-13. Those are two separate byte tests, which read naturally as "tier" ranges. Only `0x8000` subtype 14 is discounted in that category. *Accounted for.* One quirk: on the skip path the discount variable is never initialized, so the later doubling test reads a leftover stack value for these items.
- **"Flat roughly ±25% keyed to category crossed with trait."** There are four such blocks:
  - −25% for Interstellar Traveler Stargates;
  - −25% for War Monger beams, torpedoes, and bombs;
  - +25% for Inner Strength beams, torpedoes, and bombs;
  - −50% for Claim Adjuster terraforming and for Cheap Engines engines.

  The opening paragraph left out the two 50% cases but nothing else. *Accounted for.*
- **"Doubles the cost if non-positive and a specific trait is absent."** The doubling fires when the discount is ≤ 0 and Bleeding Edge Technology is **present**. It also requires a global flag (DS `0x078e` bit `0x08`) to be **clear**. Only two routines set that flag, each for its own duration, and both read Bleeding Edge Technology on entry: the Scrap Fleet recovery handler `FUN_10b0_3f3a` (`stars.exe.export.c:75569`/`75834`) and the AI cost estimator `FUN_10f0_50e0` (`:102178`/`:102254`). So scrap values and AI estimates are never doubled. "A trait is absent" is most plausibly this flag condition, merged with the trait test. *Accounted for.*
- **Order of the stages.** The stages run in the same order as described. *Accounted for.*

Every specific claim in the opening paragraph maps onto `FUN_1050_7d44`. No claim describes behavior the function lacks. Together with the earlier negative search (no function combines a habitability read with a category read in a cost computation), this refutes the idea of a separate routine. One new UI detail: when the doubling fires, the function sets DS `0x078d` bit `0x40`. The painter `FUN_10d8_1e40` (`:90178`) tests that bit and draws dynamic string 1344, "Bleeding Edge", in a highlight colour. The cost display therefore labels doubled items.

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

(The 725/1,448 prices here are the community source's own figures, not game constants. See §1b for
the real, baseline-dependent cost. The simple product is also not the game's habitability formula;
see §1b and `population-growth.md` §2.)

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

- **Spot-check of this document's PRT/LRT trait descriptions against the real in-game text, now that the dynamic string table is fully decoded (see `dynamic-string-table.md`).** This document's trait tables (§2, §3) were originally sourced from public community wiki material rather than the game's own text, and §3a's live-client pass already independently re-confirmed the mechanical facts against the real Custom Race Wizard on-screen text for every PRT/LRT with "no corrections needed." This pass re-checked a sample of that same real text as recovered directly from the decoded string table (identifiers in the 256-383 range, "descriptive text for primary and lesser racial traits" per `dynamic-string-table.md` §5.1), as an independent, non-visual cross-check of the same claim. Spot-checked traits and outcome (paraphrased per this project's clean-room rules — no verbatim quotation of the recovered strings beyond a few words):
  - **Super Stealth** — exact match: the recovered text's "travel through minefields one warp speed faster than the limits" phrasing matches this document's §2 entry verbatim in substance.
  - **War Monger** — exact match: "build weapons 25% cheaper" and a battle movement-speed bonus both appear in the recovered text exactly as this document's §2 entry describes them.
  - **Interstellar Traveler** — exact match, and an independent second confirmation of a fact §3a already found live: the recovered text's framing of exceeding a stargate's safety limits as "less likely to kill your ships" matches §3a's live-client wording closely, corroborating that finding via a completely different extraction method (decompiled string table vs. on-screen observation).
  - **Claim Adjuster** — matches in substance, with one refinement this document did not previously carry: the recovered text specifies the permanent +1%-toward-ideal environmental drift (§2) happens via a **10%-per-year chance**, a specific probability this document's §2 entry does not currently state.
  - **Inner Strength** — matches in substance, but the recovered text names one ability this document's §2 entry omits entirely: Inner Strength is described in-game as able to lay "Speed Trap" minefields, a capability not currently listed among its exclusive hulls/devices in §2.
  - ~~**Packet Physics** — a likely inaccuracy, not a match.~~ **CONFIRMED and FIXED this pass, directly against `extracted-game-data/dynamic-strings.txt` (string IDs 294-296, file lines 295-297).** The recovered text reads (paraphrased per this project's clean-room rules): the race starts with a Warp 5 mass accelerator at its home starbase and Tech 4 Energy, and will *eventually be able to fling packets at Warp 13* — a packet speed rating, not a Mass Driver component tier. "Mass Driver tech level 13" does not appear anywhere in the recovered text; it was a conflation, in this document's §2 summary specifically, of packet-fling speed (Warp 13) with the unrelated "Ultra Driver 13" component tier already discussed lower in this Open Questions list. §2's Packet Physics row has been corrected to match the real text precisely.

  Overall: the great majority of this document's trait content is corroborated by the real text (consistent with §3a's live-client pass), with one small span-of-confidence addition (Claim Adjuster's 10%/year figure), one concrete omission (Inner Strength's Speed Trap minefield access), and one likely inaccuracy worth revisiting (Packet Physics' "Mass Driver tech 13" phrasing in §2). This was a targeted spot-check (five traits), not an exhaustive line-by-line audit of every PRT/LRT entry.

  **Broadened significantly in a later pass — see §3b above.** All 14 LRT description strings and the 4 remaining un-spot-checked PRTs (Hyper Expansion, Space Demolition, Alternate Reality, Jack Of All Trades) were read directly against `dynamic-strings.txt`, 18 entries beyond this original five-trait sample. Outcome: the great majority again matched exactly; one substantial correction was found and applied (Bleeding Edge Technology's cost-penalty trigger is tied to a component's own tech requirement, not to standing versus other players — resolving a discrepancy this project's own two documents had carried unreconciled since `research-tech-tree.md` already had the correct framing); one new omission was found and fixed (Alternate Reality: destroying a starbase kills every colonist orbiting that world); Inner Strength's Speed Trap omission (identified here) is now actually folded into §2's table, not just noted; and several small wording tightenings were made (Mineral Alchemy's "roughly 4x" → exact 4x; Jack Of All Trades' scanner confirmed specifically penetrating; Regenerating Shields' regeneration confirmed as 10% of *maximum* rating). One detail (Total Terraforming's ±3%-at-colonization figure) remains uncorroborated by the recovered string, though not contradicted either. **Since resolved (§3b, fourth pass).** The figure is a misreading of TT's tech-free "Total Terraform ±3" item, an ordinary terraform available from turn one. It is not an automatic change at colonization.

- ~~**No documented absolute point-cost table.**~~ **RESOLVED numerically by direct inspection of
  `stars.exe`'s compiled data, this pass.** §1a above already reconstructed the formula's *shape*;
  this pass located the formula's home function precisely (`FUN_10e0_2e9a`, segment 29, file offset
  `0x897c0`) and, from there, located and read the two data tables it indexes directly out of the
  binary — they are **not** in the shared DGROUP data segment (segment 38, `DAT_1128_*`) as initially
  assumed by analogy with other tables in this project; they are static data embedded **in segment
  29's own code segment**, immediately following the function, at offsets `0x2c8` (14-entry per-LRT
  table) and `0x2e4` (10-entry per-PRT table) — the two tables are contiguous (LRT table ends exactly
  where the PRT table begins), and a third, 7-entry table immediately precedes them at `0x2ba` (the
  research-cost-bias negative-bias refund lookup referenced in item 7 of §1a). Real values for all
  three tables are now in §1a items 6, 7, and 11 above. **Cross-validated exactly against this
  document's own previously-cited secondary source:** the recovered Cheap Engines entry is exactly
  240 raw ÷ 3 = 80 RW points, matching this document's §3 citation of Cheap Engines being "worth up
  to 80 points" to the exact integer, with no rounding — strong evidence the table location and
  reading (offset, stride, sign convention) are correct, not a coincidental match.

  **The specific 725/1,448-point immunity figures remain unconfirmed, but the reason is now
  understood rather than merely "no accompanying label survives."** The 10-entry table this project
  previously called the "PRT-immunity/base-cost table" is confirmed, by tracing every place
  `FUN_10e0_2e9a` reads it, to be indexed **only by PRT**, never by axis or by immunity state — it is
  a flat per-PRT baseline point adjustment (§1a item 11), not an immunity-cost table at all. No
  table anywhere in this function is indexed by axis-immunity-count beyond the already-documented
  flat −150-raw two-or-more-immunities penalty (§1a item 4); a *single* immune axis carries no direct
  flat cost line item in this formula whatsoever. The practical implication: single-axis immunity's
  real point cost (the commonly-cited 725 points) must arise **indirectly** — from (a) forfeiting the
  per-axis center-deviation refund that a non-immune axis could otherwise earn by sitting near center
  50 (§1a item 3, which explicitly does not apply to an immune axis), and (b) the growth-rate term's
  coupling to a "habitability quality" score (§1a item 2) that a wide-tolerance or immune axis
  inflates, indirectly suppressing how large a growth-rate value the formula will allow for free. This
  is a genuinely new structural finding, not merely a restatement of the prior gap — but it does mean
  a literal 725/1,448 constant should not be expected to turn up anywhere in this binary as a single
  lookup-table entry, which is itself a useful (negative) result for anyone who goes looking for one.

  **Attempted quantification this pass, partially completed.** §1a item 2's growth-rate breakpoint
  table is now fully recovered (concrete numbers above), and its quality-score input is confirmed to
  come from `FUN_10e0_3404`, a genuine triple-nested discretized sampler over the three tolerance axes
  (loop-structure confirmed, including a distinct widened-window code path for an immune axis). This
  pass could not finish tracing that sampler's own internal accumulation/normalization into its final
  returned figure (a 32-bit value, not the 16-bit one an earlier pass implicitly assumed — the
  decompiler's `void` return type for `FUN_10e0_3404` is misleading; the real return travels through
  the `DX:AX` register pair and is read that way at the one call site inside `FUN_10e0_2e9a`), so a
  literal reproduction of 725 (single immunity) or 1,448 (triple immunity) points was not achieved this
  pass. What can be said from the recovered pieces alone: the per-axis center-deviation refund an
  immune axis forfeits (item 3) tops out at `50 × 4 = 200` raw (≈67 RW points) for one axis sitting at
  a maximally off-center, non-immune band — far short of 725 points by itself — which is consistent
  with this document's standing conclusion that the bulk of single-axis immunity's real cost must come
  from the growth-rate/quality coupling (item 2) rather than the center-deviation term, but does not
  independently confirm the magnitude. Finishing this — tracing `FUN_10e0_3404`'s accumulation loop
  body (not shown in the excerpt read this pass) and running the two formulas end-to-end for a
  matched-growth-rate immune-vs-non-immune comparison — is a well-scoped follow-up for a future pass,
  now that both call sites and the outer arithmetic are pinned down precisely.

  **`FUN_10e0_3404` read in full this pass (`stars.exe.export.c:94459`-`94749`, all ~290 lines).**
  This substantially extends the structural description above, though it still does not close out a
  literal 725/1,448 reproduction. Confirmed:
  - **The candidate race record is temporarily swapped into the global "current race" slot for the
    whole computation.** The function opens by copying the live global race table
    (`DAT_1128_59c2`, the same 0xC0-byte-stride per-race record this project's field accessors
    address throughout) into a 0xC0-byte local backup, then overwrites the global slot with the
    *candidate* race passed in via `param_1`; at the very end (once the outer loop below completes),
    it copies the backup back over the global slot before returning. This is exactly the "evaluate a
    hypothetical, not-yet-saved race" pattern already documented elsewhere in this project for the
    Research dialog's tech-distance-grading candidate function — confirming `FUN_10e0_3404` is safe
    to call on a race the wizard is still actively editing, consistent with its role feeding the
    live point-total preview.
  - **The outer loop (`local_1a` = 0, 1, 2) runs three full passes, not one**, each computing a
    complete weighted grid-sample sum over the three tolerance axes and adding it into a shared
    `double` accumulator (`dVar3`) that persists across all three passes. Each pass first
    recalculates a per-axis "window-widening" constant, `local_1c`, gated on race-trait bit 1
    (`FUN_10e0_226e(race, 1)`, read once at function entry as `local_30`) — **a previously
    unidentified trait-bit read specific to this function**, not previously connected to any named
    trait by this document: pass 0 always uses `local_1c = 0` (no widening); pass 1 uses `5` when
    bit 1 is clear or `8` when set; pass 2 uses `15` when clear or `17` when set. The bit's in-game
    identity was not determined this pass (no on-screen label test was located for it, unlike
    Generalized Research's bit 4 in `research-tech-tree.md` §4) — flagged as a further open item
    rather than guessed.
  - **The per-sample quality evaluator is confirmed to be `FUN_1048_490e` itself** — the same
    per-planet habitability function `population-growth.md` §2 and this document's §5 both cite —
    called once per synthetic grid sample point (`stars.exe.export.c:94662`) against a fabricated
    12-byte "planet" built entirely on the stack (`local_88`, with its three trailing bytes
    `local_7c`/`local_7b`/`local_7a` populated as the sample's Gravity/Temperature/Radiation click
    values — matching `FUN_1048_490e`'s own `param_1+0xc/+0xd/+0xe` axis-byte reads exactly). Each
    axis's sample count and step size are read from the *race's own* stored fields (`local_50[3]`,
    `[4]`, `[5]` for the three per-axis sample counts, `[0]`/`[1]`/`[2]` for step sizes, `[6]`/`[7]`/
    `[8]` for the low bound each axis sweeps from) rather than being fixed constants — i.e. the
    number of discretization points genuinely varies per race/per axis, not just the axis bounds.
  - **Each sample's squared quality contribution is weighted differently per outer pass** — a
    best-effort manual reconstruction of the 16-bit multi-precision multiply-with-carry arithmetic at
    `94673`-`94691` (not verified by execution) gives integer weights of **7** for pass 0, **5** for
    pass 1, and **6** for pass 2, summed into a per-pass `local_12`/`local_26` accumulator that is
    itself scaled by a further per-pass factor (`local_50[1]` or `local_50[2]` times a shared
    constant `DAT_1128_1dca`, or a flat `DAT_1128_1dd2` when the relevant axis's race data is
    immune — byte `< 0` at `param_1+0x11` — determined at `94716`-`94718` and `94727`-`94729`) before
    being folded into `dVar3`.
  - **Re-checked directly (later pass), specifically to verify the 7/5/6 weights rather than accept
    the earlier best-effort reading as final.** Re-tracing `stars.exe.export.c:94673`-`94691`
    symbolically confirms all three weights exactly, and shows *why* they come out as 7/5/6 rather
    than merely that they do: each pass computes `lVar6 = local_6²` (the clamped per-sample quality
    delta, squared), and then, depending on `local_1a`: pass 0 computes `2×(3×lVar6)` and adds the
    original `lVar6` on top (the shared `LAB_10e0_37a4` fall-through, `94679`-`94680`) — i.e.
    `lVar6 + 6×lVar6 = 7×lVar6`; pass 1 computes `2×(2×lVar6)` and likewise falls through to add
    `lVar6` (`94683`-`94686`) — `lVar6 + 4×lVar6 = 5×lVar6`; pass 2 computes `2×(3×lVar6)` directly,
    in a separate `CONCAT22` assignment that **bypasses** `LAB_10e0_37a4` and its `+lVar6` step
    entirely (`94687`-`94691`) — `6×lVar6` exactly, with no base term added. This confirms the
    earlier reconstruction's **7, 5, 6** with no correction needed, and additionally explains the
    asymmetry between passes 0/1 (a shared base weight of `+1`, then ×6 or ×4) and pass 2 (no base
    weight, a bare ×6) — a structural detail the earlier best-effort reading did not capture.
  - **Trait bit 1's identity: cross-referenced this pass against every other located trait-bit-index
    finding in this project's spec files, per this document's own established method of naming
    traits this way — result is a corroborated-but-still-unnamed selector, not a resolved name.** The
    same generic per-race boolean-trait accessor read here (`FUN_10e0_226e`) is called with selector
    `1` in at least one other, unrelated function: `ai-opponent-behavior.md`'s trace of
    `FUN_10d0_221a`'s catalog-items-4/5 cost formula (`stars.exe.export.c:87850`-`87867`) reads
    `FUN_10e0_226e(<race>, 1)` to choose between a 100-resource and a 70-resource base cost, and that
    document's own text states plainly that selector 1 "has not been independently named anywhere
    else in this project" — which, after this cross-reference, is no longer true in the sense that it
    is now known to be the *same* selector this function's window-widening gate reads, only in the
    sense that neither occurrence names the underlying trait. **Extrapolating the name from the
    14-entry LRT wizard-checkbox order (this document's own §1a item 6 table: index 1 = Total
    Terraforming) was considered and rejected as unsafe**, because this project's own
    `research-tech-tree.md` already demonstrates that wizard-checkbox order does not reliably predict
    this accessor's selector numbers: it independently pins Generalized Research to selector `4`
    (via a direct on-screen paint-gate label, the strongest kind of proof used anywhere in this
    project) and Ultimate Recycling to selector `5` (via this document's own §3a deferred-resources
    trace), while wizard-checkbox order would instead predict Ultimate Recycling at index `4` and
    Generalized Research at index `6` — a real, already-documented two-position mismatch, not a
    hypothetical one. (Selectors `0`=IFE, `3`=Improved Starbases, `6`=Mineral Alchemy, `8`=Cheap
    Engines, `10`=No Advanced Scanners, and `12`=Bleeding Edge Technology *do* each match
    wizard-checkbox order exactly, per this document's own §1a/§7 findings and
    `research-tech-tree.md`'s Improved Starbases entry — so checkbox order is clearly *related* to
    the real selector layout, just not a safe universal predictor, since selectors 4-6 are
    demonstrably permuted relative to it.) **Conclusion: trait bit 1 is confirmed to be a real,
    reused selector value — appearing in at least two unrelated functions across two spec documents —
    but its in-game trait identity remains unresolved.** This is reported as a well-evidenced
    negative result rather than a guess, consistent with this project's own explicit caution
    (`research-tech-tree.md`) against exactly the checkbox-order extrapolation that would otherwise
    have been tempting here.
  - **RESOLVED (this pass): trait bit 1 is Total Terraforming. Three independent lines of evidence support this.**
    1. **Live behavioral fingerprint.** On the Humanoid preset, ticking Total Terraforming alone moves the wizard from 25 to **−115**. TT's own table cost (−25 raw) would give only 16. The −115 figure is reproduced exactly by the §1b formula only when bit 1 switches the sampler's allowances from 5/15 to 8/17. Ticking GR, UR, MA, or IFE alone gives 38, −55, −26, and −53, the table cost alone in each case, with no sampler effect.
    2. **Second code site.** `FUN_10d0_221a`'s joint cost case for queue-item types 4, 5, and 12 (`stars.exe.export.c:87850`-`87867`) charges 100 resources, or **70** when bit 1 is set. Those item types are, by `dynamic-strings.txt` IDs 130, 131, and 138, **Min Terraform, Max Terraform, and Terraform Environment**. This is exactly TT's "terraforming costs 30% less." The same switch's Alchemy case, types 3 and 11, charges 100 or 25 on bit 6, which is Mineral Alchemy's 4×. This also identifies the items `ai-opponent-behavior.md` left unnamed.
    3. **The permutation that blocked the extrapolation was this document's own error.** The §1a LRT table had listed rows 4-6 as UR/MA/GR. The real order is GR/UR/MA: LRT name strings 306-319, and wizard checkbox control IDs 291-304 in that order. With the correction, research-tech-tree.md's GR = 4 and UR = 5 match checkbox order exactly. Trait-bit selectors 0-13 **are** the wizard checkbox order, with no exceptions found.
  - **The final return is a genuinely clean, fully-legible occurrence of the shared FPU idiom** —
    unlike the AR-resources call site above, `dVar3` here is an ordinary, visibly-computed `double`
    accumulator built up entirely through normal C floating-point arithmetic across all three passes,
    and the function's sole closing call, `FUN_1120_0e40()` (`94745`), operates directly on it with
    no ambiguity about the operand's identity — only the earlier "dispatch the pending op" half of
    the idiom (`FUN_1120_0dc2`) is never called here at all, meaning whichever unary FPU instruction
    this site uses must have been issued through some other, unseen path, or this final step is
    reusing an FPU value already sitting on the stack from the last inner-loop iteration.
  - **Net effect on the 725/1,448 question:** the sampler's grid dimensions, per-pass weights, and
    widening constants are now concretely pinned down for the first time, and the "immune axis
    widening" mechanism is confirmed to be gated by trait bit 1 rather than applying uniformly. A
    literal numeric reproduction of 725 or 1,448 was still not attempted by hand this pass — the
    reconstructed arithmetic (three weighted nested grid sums, race-specific sample counts, a
    not-fully-identified trait-bit gate, and 16-bit carry-propagation details not independently
    re-verified) is intricate enough that a manual reproduction risks presenting an unverified,
    possibly-wrong number with false confidence. A future pass with the ability to actually execute
    this reconstructed logic (rather than hand-trace it) is better positioned to attempt the literal
    725/1,448 reproduction than a further reading pass would be.
  - **Hand-arithmetic reproduction attempted directly (later pass), writing out every §1a term and
    plugging in values as requested, rather than deferring again to "needs execution."** Setting up
    the comparison as cleanly as the recovered formula allows: hold the race's PRT, LRT selection
    (zero), growth-rate slider, and every economic/research slider fixed between a "0 axes immune"
    build and a "1 axis immune" build, and set the non-immune baseline's three axes to sit exactly on
    center (50). Walking every term in §1a's own numbered list under these assumptions: items 1
    (baseline), 5 (economic quadratic terms, both builds' sliders ≤10), 6 (LRT table, zero LRTs both
    builds), 7 (research bias, all six fields Normal both builds), 8/9/10 (checkboxes, unchecked both
    builds), and 11 (PRT table, same PRT both builds) are **identical in both builds and cancel
    exactly** in the subtraction. Item 4 (the flat −150-raw two-or-more-immunities penalty) does not
    trigger in either build (0 or 1 immune axes, never ≥2), so it cancels too — at exactly zero on
    both sides, not merely equal nonzero values. Item 3 (per-axis center-deviation refund) is
    **exactly 0 raw on both sides** under this specific setup: the non-immune build's axis sits at 50
    (`abs(50−50)×4 = 0`), and item 3 explicitly does not apply to an immune axis at all — so there is
    no refund to forfeit in this particular comparison (a sharper, more specific result than the
    "tops out at 200 raw" bound reported above, which used a *different*, maximally-off-center
    non-immune baseline). **The entire 725-point (2,175-raw) gap must therefore come from item 2
    alone, exactly — not "the bulk of it," but all of it — under these assumptions.** Item 2's own
    formula (boxed earlier in §1a) gives the exact algebraic requirement:
    ```
    2,175 raw = (multiplier(g) / 48,000) x (Q_immune - Q_nonimmune)
    ```
    so `Q_immune - Q_nonimmune = 104,400,000 / multiplier(g)`. Reading `multiplier(g)` off §1a item
    2's own recovered breakpoint table for representative growth-rate settings: at `g=15`
    (`multiplier=27`), the quality-score sampler's output must swing by **≈3,866,667**; at `g=19`
    (`multiplier=39`, a "wide-habitability" convention rate per §4), it must swing by **≈2,676,923**.
    Applying the identical method to the second immunity (1,448−725=723 points ⇒ 2,169 raw, minus the
    now-triggered −150-raw two-or-more penalty ⇒ 2,019 raw attributable to item 2) gives a *smaller*
    required swing for the second axis (≈3,589,333 at `g=15`) than the first (≈3,866,667) —
    consistent with a saturating, diminishing-returns quality score, though this reads the two
    published figures (725, 1,448) as if they shared one fixed baseline race, which the source
    material never states explicitly.

    **This still does not produce a literal reproduced number, and the reason is now precise rather
    than general.** Two concrete blockers, not previously named this specifically: (1)
    `FUN_10e0_3404`'s inner accumulation is scaled by two floating-point literal constants,
    `DAT_1128_1dca` and `DAT_1128_1dd2` (read at `stars.exe.export.c:94715`, `94717`-`94718`, and
    `94726`, `94728`), whose actual double values are not visible anywhere in this decompile — they
    are opaque `DAT_` symbols, not literals the exported C source shows; and (2) the sampler's
    per-axis sample counts and step sizes (`local_50[3]`/`[4]`/`[5]` and related fields,
    `stars.exe.export.c:94586`-`94721`) are read from the *race's own* stored fields, and no source
    consulted — including this project's own recovery of the economic-slider default table in
    `race-designer-ui-and-availability.md` — establishes what those sample-count fields default to
    for a freshly-created race. Both are needed before `Q_nonimmune` or `Q_immune` can be computed
    from first principles, and neither is recoverable from the exported `.c`/`.h` decompile alone —
    the same category of raw-disassembly-only blocker already on record for AR's
    `EfficiencyCoefficient` FPU trace above (before a later pass resolved that specific one
    differently, via a recovered UI string rather than the arithmetic itself — no equivalent string
    was found for this sampler). **Net result: the formula is now fully reproduced symbolically, and
    the exact numeric target the unresolved sampler must hit is now known algebraically for the first
    time — but the two blocking constants remain out of reach of this decompile, so the literal
    725/1,448 figures are still not independently reproduced by hand.**
  - **RESOLVED (this pass). Both blockers are cleared, and the formula reproduces the live client exactly. See §1b for the full writeup.**
    - Blocker (1): the constants are DS-relative, not CS-relative. They were read directly from DGROUP's initialized data: `0x1dca` = 0.01 and `0x1dd2` = 11.0. The live data-segment dump holds identical bytes. The same search found three constants the decompile hid entirely: a 0.0 seed, and a final ×0.1 then +0.5 before truncation.
    - Blocker (2): the "per-race sample counts" are not race fields. The earlier pass misread a local grid-descriptor array. The counts are fixed at 11 for a non-immune axis and 1 for an immune one, so the "fresh race" question disappears.
    - Validation: a scratch implementation of the complete formula matched all 18 live readings. These were six presets, five single-LRT toggles, the cleared state, and six immunity configurations.
    - Headline: **the 725 / 1,448 figures are not reproduced, because they are not constants of v2.70j.**
      - From the default Humanoid race, one immunity costs 572 points, two cost 1,730, and three cost 3,925. The single-immunity figure (25 → −547) and the triple-immunity figure (25 → −3,900) were both confirmed live.
      - Across all centered baselines and growth rates, the second immunity always costs at least 2.5× the first. The community pair implies about 1×.
    - The earlier algebraic "required swing" for the Humanoid-like setup at g = 15 was about 3.87 million for 725 points. The real swing is 6,345,682 − 3,293,786 = 3,051,896, which produces 572.
- ~~**LRT stacking-penalty formula is unknown.**~~ **RESOLVED structurally and numerically by
  inspection of the exported client** — see §1a items 6 and 11 above for the exact thresholds,
  per-step costs, and all 14 recovered LRT base prices plus all 10 recovered PRT base prices.
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
- ~~**Generalized Research's advertised return percentage doesn't match its own arithmetic.**~~
  **RESOLVED — see `research-tech-tree.md` §4.** That document locates and quotes the full
  `stars.hlp` "Generalized Research" topic directly (a different, more complete text surface than
  the Custom Race Wizard's inline description read during this document's own 2026-09-03 testing
  pass, quoted just above): the help topic states the 15% applies "to each of the fields" (not a
  split total) and explicitly acknowledges the result adds up to 125% ("Yes, we know this adds up to
  125%"). This confirms the **125%-aggregate, 15%-to-each-field** reading used throughout this
  document and the wiki, and identifies the disputed "115%" figure as a GameFAQs guide author's own
  transcription error rather than a genuine ambiguity in the game. The Wizard screen's shorter
  "applied to all other fields" phrasing (captured directly above) is a real, independently-verified
  primary source in its own right — it is simply a less explicit paraphrase used in that particular
  UI surface, not evidence of a second, conflicting mechanic.
- ~~**Cheap Engines' warp threshold is inconsistently reported.**~~ **RESOLVED — the two phrasings
  were never actually in conflict.** "In excess of Warp 6" and "Warp 7, 8, 9, or 10" describe the
  identical range; independently re-reading the Custom Race Wizard's own text in a fresh session
  (2026-09-10, same emulated-client method) reproduced the exact "in excess of Warp 6" wording again,
  confirming this is the client's real, stable phrasing rather than a one-off transcription slip.
- **Race-designer world availability versus per-planet habitability has been reconciled.** The
  client-side availability display is now specified in
  `race-designer-ui-and-availability.md`; per-planet habitability is specified in
  `population-growth.md`. They are intentionally different calculations.
- **No confirmed value for AR's `EfficiencyCoefficient` bounds or Death Star build cost**, and the
  AR-specific production formula in §5 is still not confirmed byte-for-byte at the code level — it
  originally came from a single strategy guide alone. **Substantially advanced this pass, not fully
  resolved.** The earlier "no `sqrt`-style call or symbol found anywhere in the decompile" negative
  result is now understood, not just noted: this executable's floating-point arithmetic runs through
  a shared single-operand FPU-instruction dispatch idiom (one "issue the pending op" call immediately
  followed by one "round top-of-stack to an integer and return it" call) reused at ~90 sites for
  whichever unary FPU instruction the original code used at each site — which is exactly why the
  literal text `sqrt` never appears, and is not evidence the formula is wrong. Two concrete,
  PRT-8-gated (Alternate Reality) occurrences of this exact idiom were located and traced this pass
  — see §5's new sub-section for the full writeup. The first is confirmed with high confidence to be
  AR's own already-cited `ScanRange = sqrt(Population / 10)` formula (found inside a scan-range
  function whose non-AR branch independently exercises the already-documented planetary-scanner
  component resolver and the No Advanced Scanners double-range trait, both matching this document's
  own entries exactly). The second sits in a different function whose surrounding arithmetic looks
  more like a mineral-transport/turns-to-clear-backlog estimate than an annual resource total, so it
  is reported as a second confirmed example of the pattern, not as the resources formula's home. A
  further targeted search for the specific per-turn per-planet resource-production pass that would
  tie Population, EnergyTechLevel, and the Step-5 `EfficiencyCoefficient` dial together for AR
  (checking every one of the ~157 call sites of the generic per-race PRT-index accessor used
  throughout this project, and the generic per-race economic-slider-field accessor already confirmed
  elsewhere in this document, for a joint population/PRT-8/economic-field read) did not turn up a
  clean match.

  **Follow-up this pass — the suggested segment-8/near-population-growth candidate location was
  checked directly and ruled out as a location for this formula, a further negative result.** Segment
  8 (`FUN_1038_*`) hosts the confirmed population growth/decline function (`code-coverage-report.md`
  segment 8 row, finding 4) and, per §5 above, the confirmed AR scan-range function (`FUN_1038_3068`,
  the sole PRT-8-gated occurrence of the shared FPU-sqrt idiom anywhere in this segment). This pass
  enumerated every read of the generic per-race PRT-index field (`FUN_10e0_222c(...,0xe)`) inside
  segment 8 by address — there are exactly three: the one inside `FUN_1038_3068` (the already-known
  AR scan-range function, PRT compared against 8), one inside `FUN_1038_337e` (a per-design randomized
  combat-power/defense estimate, PRT compared against 9/JOAT, unrelated to AR or resources), and one
  inside `FUN_1038_4be5`'s host function (PRT compared against 7/Interstellar Traveler, gating an
  unrelated per-slot cargo-capacity check). No fourth, resource-production-shaped PRT-8 branch exists
  in this segment. This is a clean negative result for the specific candidate location suggested: the
  AR resource-production formula's home function is confirmed **not** to live in segment 8 alongside
  the population-growth function, despite the two being logically related in-game.

  **RESOLVED (later pass): the home function lives in segment 10 (`FUN_1048_*`), not segment 8 — see
  §5's new paragraph above for the full trace.** `production-queue.md` independently identifies
  `FUN_1048_4faa` (`stars.exe.export.c:27950`-`28005`) as the per-planet annual resource-production
  routine (called once per owned planet from the segment-24 turn-generation summary loop). Reading it
  directly shows it is this document's missing formula's home: it reads the owning race's PRT
  (`FUN_10e0_222c(...,0xe)`, line `27982`) and branches `if (PRT == 8)` into a dedicated AR-only
  path (lines `27983`-`27987`) that calls the habitability evaluator `FUN_1048_490e`
  (`population-growth.md` §2's now-fully-documented habitability function) followed immediately by
  the confirmed FPU-sqrt idiom (`FUN_1120_0dc2`/`FUN_1120_0e40`) — versus the `else` branch's
  ordinary factories-times-"resources per 10 factories"-slider arithmetic for every other PRT. This
  is why the segment-8-only search above, however thorough, could never find it: the function simply
  isn't in that segment. **Net effect on confidence:** the formula's *home function* is now a
  confirmed fact — the search that was open here is closed — but the exact operand list (whether
  `EnergyTechLevel` and the Step-5 `EfficiencyCoefficient` dial specifically enter the calculation,
  versus some other pairing) was not independently re-derived from this branch's own arithmetic this
  pass, since `FUN_1048_490e`'s return value is discarded by the caller (most plausibly consumed via
  the shared x87 FPU stack rather than a C-level return) and its call-site-specific inputs were not
  traced. This remaining gap is narrower and well-scoped for a future pass, unlike the previous
  "which segment" uncertainty.

  **AR's `EfficiencyCoefficient` bounds and the Death Star's build cost are both now resolved (later
  pass), from two sources this document had not yet cross-checked for this specific question.**
  `race-designer-ui-and-availability.md`'s own recovery of the Economic-settings stage's bound
  tables (a companion finding that closed a gap that document shared with this one) establishes that
  AR's Step-5 wizard page reuses the same underlying slot — slot 0, the "colonists per resource"
  field every other PRT sees — for its `EfficiencyCoefficient` display, merely dropping the
  ×100/"00"-suffix formatting other races get. That slot's recovered bound is **7-25, default 10**,
  which `race-designer-ui-and-availability.md`'s own "Alternate Reality variant" paragraph confirms
  is exactly the raw value AR's wizard page shows as the sqrt's divisor — so **`EfficiencyCoefficient`
  ranges 7-25, default 10**. Separately, `extracted-game-data/component-stats.tsv`'s starbase-chassis
  table (category `0x0400`, index 4) lists the Death Star hull directly: base build cost **1,500
  resources, 240 kT Ironium, 160 kT Boranium, 700 kT Germanium**, requiring Construction tech **17**,
  with an armor rating of 1,500 and 16 component slots (mass `0`, like every starbase chassis, and a
  cargo-capacity field of `65535` — the same unbounded-capacity sentinel the other three top-tier
  starbase chassis also carry). These are base costs before any race-specific discount (Improved
  Starbases/AR's own 20%-cheaper-starbase rule, §1a item on Improved Starbases; the segment-27
  miniaturization discount, §7) or actual component loadout is applied. This finding is independent
  of, and does not affect, the still-open FPU-blind-spot question above — the Death Star's *build
  cost* and AR's *resource-production* formula are unrelated mechanisms, and only the latter remains
  genuinely blocked on raw disassembly.

  **The operand list itself is now RESOLVED, this pass — not by finishing the FPU trace, but by a
  recovered in-game string that supersedes the strategy guide as the formula's source.**
  `extracted-game-data/dynamic-strings.txt` IDs 260-261 hold the literal Step-5 wizard-page text
  shown in place of the standard economic controls when the race is Alternate Reality: a template
  reading `Annual Resources = Planet Value * sqrt(Population * Energy Tech / [coefficient])` — the
  game's own words, not a secondary source, confirming Population, Energy Tech, and the Step-5
  efficiency dial are exactly the sqrt's three operands, with Planet Value (`HabitabilityValue`)
  multiplying the result afterward. See §5's newest sub-section for the full writeup, including a
  second, simpler corroborating string (ID 242) and a further attempt (only partially successful) to
  reconcile this against the FPU-blind-spot call site: a strong circumstantial candidate for the
  Population operand was identified (`local_a`, cross-confirmed via byte-offset matching against
  `population-growth.md`'s own population-field citation), and — a new negative result — neither
  Energy tech nor any economic-slider field is read anywhere in the visible code of the AR branch or
  its callee, meaning if they enter the calculation at all, it is entirely through the same invisible
  FPU channel already responsible for the missing sqrt operand generally. The *what* is resolved; the
  *exact machine-level how* remains blocked on the same inaccessible-raw-disassembly limitation as
  `research-tech-tree.md`'s identical economic-slider-defaults blocker.

  **RESOLVED (this pass) — see §5a.** Disassembling `FUN_1048_4faa` directly (segment 10, file
  `0x30300` + `0x4faa`) recovered the whole hidden AR computation:
  - capacity-adjusted population (in hundreds) ÷ race field 0, × Energy tech (race-record byte
    `0x1a`, floored at 1);
  - square root, × Planet Value (floored at 25);
  - × 0.1 and + 0.999 (DS `0x1d12` and `0x1d1a`, read from DGROUP), then truncated, with a
    minimum of 1.

  All 12 of 12 live homeworld readings matched, at coefficients 10 and 17 and Energy 1 through 4.
  Nothing on this item remains open except the two untested edge rules (the 25% floor and the
  over-capacity half weighting).
- ~~**PP's "Mass Driver tech up to level 13" was implemented as Energy tech level 4.**~~ **RESOLVED
  by cross-referencing this project's own component data (2026-09-05):** "level 13" refers to the
  mass-driver component's own tier number, not a raw Energy tech level — this codebase's `Mass
  Driver`-family components are named `Mass Driver 5/6`, `Super Driver 7/8/9`, and `Ultra Driver
  10/11/12/13`, and `components.xml` lists `Ultra Driver 13` (the tier the spec's "up to level 13"
  refers to) as requiring **Energy tech 24** to unlock, not 4 (which only reaches `Mass Driver 5`).
  Fixed in `GameInitialiser.cs`. This is a data cross-reference, not a live-game re-verification
  like the War Monger/Claim Adjuster entries above — worth confirming against the real game if it's
  ever reachable again.
- ~~**The entire "distinct starting fleet/planet-count per PRT" system is effectively
  unimplemented, beyond Hyper Expansion's 3x colony ship count and Packet Physics/Interstellar
  Traveler's second planet.**~~ **RESOLVED this pass — every PRT's real starting-fleet loadout has
  now been traced and named. See `new-game-setup.md` §5 for the full per-PRT table.** The
  second-planet bonus for Packet Physics and Interstellar Traveler was already traced (§2a above)
  to a real, live, executing mechanism in the exported client itself (segment 16, `FUN_1078_1334`).
  This pass extended that same trace to cover the *entire* starting-fleet-construction portion of
  that same function, immediately upstream of the second-planet code, and additionally recovered the
  22-entry named starting-ship design-template table it draws from (a previously-unattributed data
  block in segment 2, read directly from the executable). Every PRT gets a distinct, real,
  concretely-named result — not the uniform "one scout + one colony ship + one starbase" this
  entry previously described. **The dead `StarMapInitialiser.cs` switch turns out to be a
  strikingly accurate (if incomplete) description of the real, live mechanism**, not merely an
  aspirational comment block in an unrelated codebase: its "an armed scout for War Monger" is
  matched almost verbatim by the live client's own "Armed Probe" (a Scout-hulled design); its "a
  destroyer and a privateer for Interstellar Traveler" is matched exactly, name for name in
  substance, by the live client's "Stalwart Defender" (Destroyer) and "Swashbuckler" (Privateer);
  its "two mine layers for Space Demolition" is matched exactly by the live client's "Little Hen"
  and "Speed Turtle" (both Mini-Mine-Layer-hulled); and its "a medium freighter... a destroyer for
  Jack Of All Trades" is matched by the live client's "Teamster" (Medium Freighter, gated on
  starting Construction tech) and "Stalwart Defender" (Destroyer) among JOAT's own bonus ships. This
  is strong evidence `StarMapInitialiser.cs`'s comment block was written by someone who *had* read
  this exact mechanism (or a closely related design document) at some point, even though — per this
  document's own prior finding — that C# file's surrounding code is a separate, downstream
  reimplementation that does not actually run any of this itself. `ServerState/NewGame/StarMapInitialiser.cs`
  remains outside this project's own files and outside the scope of what `stars.exe.export.c`
  contains; it is cited here only for the comment-text match, not as a source this project analyzes
  directly.

  **Important scope clarification, carried forward:** `ServerState/NewGame/StarMapInitialiser.cs`
  does not exist anywhere in this project's own files, and its "every race currently starts with
  exactly the same one scout + one colony ship + one starbase" framing reads as a description of a
  separate, downstream clean-room-reimplementation codebase's *current, incomplete* state, not of
  the decompiled exported client (`stars.exe.export.c`) this project actually analyzes. That
  distinction is now fully resolved rather than merely flagged: the exported client's own segment-16
  routine (§2a above) does **not** give every PRT identical starting ships, and this pass finished
  the cross-check the previous pass left open — every one of the ten PRT-gated branches in that
  routine's starting-fleet section (PRT 0 through 9, all ten, not just "most") was individually
  traced to a specific named design (or pair/trio of designs) via the 22-entry starting-ship
  template table this pass also recovered. `new-game-setup.md` §5 carries the complete table. The
  practical upshot: this document's own in-project evidence not only contradicts the "every race
  starts identically" premise for the actual game, it now positively confirms, PRT by PRT, what
  each one actually starts with — largely (though not perfectly) matching what the separate,
  missing-from-this-project `StarMapInitialiser.cs` switch says each PRT *should* get, per the
  name-for-name matches itemized above.

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
