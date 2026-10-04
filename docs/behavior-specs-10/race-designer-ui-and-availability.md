# Race Designer Interaction and Planet-Availability Estimate

This specification covers behavior observed in the game client's race-design, research-preference, and battle-plan editing screens. It deliberately describes externally meaningful state, validation, and calculations only. It does not define rendering, operating-system event handling, storage layout, or any game-turn resolution that is not established by these screens.

## Scope and confidence

The client implements a six-stage race-design flow. The exact labels and art assets are presentation details and are outside this specification. The behavior below is suitable for a compatible user-facing editor.

The planet-availability calculation is directly supported by the exported client logic. It has now been cross-checked against the host's actual galaxy-generation distribution (see below) and found to match structurally, but it remains, mechanically, the designer's own display-side calculation rather than a call into shared code with the generator.

**Resolved this pass: the designer's display formula matches the actual galaxy-generation distribution, once the generator itself is correctly identified.** A previous pass (recorded in `population-growth.md` §2) attributed the environment-axis-generation step to `FUN_1078_1334`'s "two summed 0-44 rolls plus a flat 31" arithmetic. Re-tracing that same per-star record this pass shows that attribution was itself mistaken: that specific roll shape, and the flag it is gated behind (`DAT_1128_0080 & 1`), is the routine `new-game-setup.md` §3 already documents, separately and correctly, as the **mineral-concentration** generator (writing the three per-star record bytes at relative offsets `9`-`11`, `stars.exe.export.c:50530`-`50566`) — confirmed identical by the shared "maximum minerals" gate and the identical `roll(0,44)+roll(0,44)+31` shape, not merely similar. `population-growth.md` has been corrected accordingly.

The **real** environment-axis generator sits a few bytes earlier in the same per-star record and uses a different roll shape entirely (`stars.exe.export.c:50496`-`50514`): two of the three axes (record offsets `12` and `13`) are each set to `roll(0,89) + 1`, then incremented by a further `roll(0,9)` (`50496`-`50501`, `50503`-`50508`); the third (offset `17`, mirrored into a cached copy at offset `14`) is set to a single `roll(0,98) + 1` (`50510`-`50511`, `50514`) — a plain uniform draw, with no second roll added.

**The math.** For the two two-roll axes, the sum of a uniform draw over 90 values (`0`-`89`) and an independent uniform draw over 10 values (`0`-`9`) has the classic discrete-trapezoid shape: writing `X` for the final 1-99 "click" value, the (unnormalized) weight is `X` for `1≤X≤10`, a flat `10` for `10≤X≤90`, and `100−X` for `90≤X≤99` — a ramp of width 10, a flat plateau, and a mirror-image ramp of width 10 down. That is, up to the 1-unit indexing shift already characteristic of the original game's "click scale" boundary conventions (population-growth.md's own §2 note on the generated range running 1-99 rather than a clean 0-100), this is **exactly** this document's own `w(x)` formula: `w(x)=x` for `0≤x<10`, flat `w(x)=10` for `10≤x≤89`, `w(x)=100−x` for `90≤x≤100`. The third axis's single `roll(0,98)+1` is a plain uniform draw over `1`-`99`, exactly matching `C_uniform`'s own flat-distribution assumption.

This lines up with the documented axis ordering, not just the formula shapes: `race-traits.md` §4 already documents that Radiation's real distribution is uniform while Gravity/Temperature's are centered/middle-weighted, and this document's own §"Environmental-tolerance stage" already states "the first two controls use the middle-weighted distribution and the third uses the uniform distribution." The generator's own layout matches that exactly — two trapezoid-shaped axes followed by one pure-uniform axis — though this pass did not independently pin down which of the two trapezoid axes (record offset 12 vs. 13) is Gravity and which is Temperature, only that both use the identical middle-weighted roll shape the third (offset 17, uniform) axis does not.

**Conclusion: this is a genuine match, not merely a plausible one.** The race designer's displayed `w(x)`/`C_middle`/`C_uniform` formula is — up to the ordinary 1-unit click-scale boundary fuzziness already documented elsewhere in this project — a structurally exact reproduction of the host's real per-axis galaxy-generation distribution, for all three environment axes. This closes the open item; it does not need to be read as a defensive "designer's own estimate, unverified against the host" caveat any longer.

## Race-design draft

Maintain an editable `RaceDraft` with at least these groups of fields:

- Identity: a name, an optional summary text field, an emblem or color selection, and one primary archetype.
- Environment: one tolerated interval or immunity selection for each of three environmental axes.
- Traits: selectable optional traits, including states that change the range or availability of other settings.
- Economy: population efficiency and industrial parameters.
- Research: a cost class for each research discipline and related options.

The wizard operates on the draft. Earlier selections may lock later controls. In a non-editable context, all mutable controls are disabled while the stored values remain visible.

## Identity and archetype stage

The user may either select a preset archetype or retain a custom draft. Selecting a preset replaces the draft's race-design fields with that preset's complete configuration. It also supplies generic default identity text when the name is empty.

The primary-archetype selection is exclusive. The editor displays one selection at a time and stores the selected option in the draft. A visual chooser can cycle through a finite palette of appearance variants in either direction, wrapping at both ends.

**Confirmed by inspection of the exported client:** exactly **8** preset archetypes are offered on this stage, and the appearance chooser cycles through exactly **32** portrait variants. Both counts were previously unconfirmed. Verified stage order for the six-stage wizard overall: Identity/Presets → Environment-tolerance-and-growth-rate (combined into one stage, not two separate ones) → Economic sliders → Primary Racial Trait (10 mutually-exclusive options) → Lesser Racial Traits (14 independent checkboxes) → Research-cost class (per-field Cheap/Normal/Expensive, plus the "starts at tech 3/4" checkbox). No separate seventh "summary/leftover-points" page was found — the running point total is evidently shown as a persistent status readout across all six stages rather than living on its own page.

**Located by a later pass: the six stages above are implemented as six explicitly-named dialog procedures.** Following the same pattern already found for the Research dialog, the object/record browser, and the rename surfaces (each a specially-named real dialog procedure hiding among a block of generically-named helper functions in its code segment), this project's economy/point-cost segment turns out to hide exactly six such named procedures — numbered 1 through 6, one per wizard stage, and called "stage-*n* procedure" below — interleaved among the already-documented helper functions (the point-cost formula, the slider drag-thumb widget, and the `.r1` file save/checksum logic). This closes out that segment's previously-unitemized "remaining UI-plumbing" note: its behavior had been reverse-engineered piecemeal, through the effects of its helper functions, without ever identifying that a fixed one-procedure-per-stage structure sits on top of them. The first of the six was confirmed, via the identity/name-field controls it initializes on open, to be the Identity/Presets stage described above. **The remaining five are now individually re-confirmed, by reading each procedure's own dialog-item-ID loops rather than relying on count/position alone:**

- The stage-2 procedure walks dialog control IDs `0x123`-`0x125` (a 3-item range, each individually checked/enabled) — exactly the three environment axes with their own immunity special case, confirming this is the Environment-tolerance-and-growth-rate stage.
- The stage-3 procedure centers on one custom-painted drag-thumb control plus a single PRT-conditional disable check (control `0x23` is disabled specifically when the race's PRT equals the index this project's convention maps to Alternate Reality) — consistent with, though a less structurally crisp match than the others for, the Economic-sliders stage (Alternate Reality's economy works differently, so a slider being conditionally disabled for that one PRT is a plausible economic-stage detail).

  **Stronger confirmation found this pass, closing the gap the count/ID-match approach left open for this one stage.** The stage-3 procedure's dialog-initialization handler (`stars.exe.export.c` line 93276-93286) initializes dialog control `0x123`'s checked state directly from the race's stored value for per-race trait bit `0x1f` (read through the trait-bit accessor `FUN_10e0_226e`) — and that exact bit is independently confirmed elsewhere in this project (see the "Random" archetype note above) to be `race-traits.md` §1a's **Germanium-cost-discount checkbox**, one of §5's own documented Step-5/Economic-sliders-page controls. A checkbox whose own subject matter (a mineral-cost discount checked on this specific stage) matches the stage's documented content area is materially stronger evidence than the drag-thumb-widget/PRT-disable-check corroboration alone — this stage's identification as the Economic-sliders page is no longer meaningfully more provisional than the other four.
- The stage-4 procedure walks dialog control IDs `0x10f`-`0x118` (a 10-item range treated as a mutually-exclusive group) — exactly the documented 10 Primary Racial Trait options, confirming this is the Primary Racial Trait stage.
- The stage-5 procedure walks dialog control IDs `0x123`-`0x130` (a 14-item range) — exactly the documented 14 Lesser Racial Traits, confirming this is the Lesser Racial Traits stage. Its initialisation (`stars.exe.export.c:93855`-`93871`) gives checkbox *i* the label dynamic string 306 + *i* and the checked state of lesser-trait bit *i* (read through `FUN_10e0_226e`). A click on control `0x123 + i` writes bit *i* back through `FUN_10e0_2292` (`:93896`-`93899`). Checkbox order, bit index and accessor selector are therefore one 0-based numbering: 0 Improved Fuel Efficiency, 1 Total Terraforming, 2 Advanced Remote Mining, 3 Improved Starbases, 4 Generalized Research, 5 Ultimate Recycling, 6 Mineral Alchemy, 7 No Ram Scoop Engines, 8 Cheap Engines, 9 Only Basic Remote Mining, 10 No Advanced Scanners, 11 Low Starting Population, 12 Bleeding Edge Technology, 13 Regenerating Shields. The page's description text is string 320 + *i* for the last-clicked checkbox (`:93824`-`93830`). The primary trait is not in this word. It is byte 14 of the race settings array, set by the stage-4 procedure.
- The stage-6 procedure walks dialog control IDs `0x10f`-`0x11e` in steps of 3 (six groups of 3, each group a Cheap/Normal/Expensive radio triple) plus a separate single control `0x123` checked independently — exactly the documented 6 research fields' cost-class radios plus the "starts at tech 3/4" checkbox, confirming this is the Research-cost class stage.

This closes out the "not individually re-confirmed" gap for four of the five (2, 4, 5, 6) with an exact structural fingerprint match against each stage's documented shape; stage 3's identification originally rested on corroborating detail (drag-thumb widget, single PRT-conditional disable) rather than an equally crisp item-count match, but is now backed by an additional, independently-sourced content match (see the note directly below the stage-3 bullet above) — its identification is no longer meaningfully weaker than the other four's. Separately, the custom-paint routine behind the Environmental-tolerance stage (three axes, each with its own immunity special case, matching the axis count and immunity handling below) was found to compute, inline, the exact edge-weighted coverage formula given below in "Environmental-tolerance stage" — an independent code-level confirmation that this formula (not just its result) lives in the executable as described, rather than being only inferred from behavior.

When the stage is accepted, the editor commits the identity fields, selected archetype, and appearance variant to the draft. Cancelling leaves the stored draft unchanged.

**A "Random" archetype option, confirmed by inspection of the exported client, previously undocumented.** One of the 8 preset archetype slots is not a fixed named preset but a full random-race generator, most plausibly reached the same way the other 7 presets are (an archetype selection replacing the draft's fields). Its traced behavior:

- It rolls fresh values for all three environment-tolerance axes (either an immune special state, or a randomized center/type/width triple, with the exact roll shape depending on a preliminary difficulty-band die), a set of economy/growth-rate style values (a further contiguous run of stored fields, each clamped against a fixed per-index min/max lookup pair — now recovered numerically, see "Economic-settings stage" below), and a block of optional-trait bits (an independent random coin-flip per bit across roughly 14 trait bits, plus two further individual bits set separately) — consistent with the environment axes, economy sliders, and Lesser-Racial-Trait checkboxes already documented in the "Race-design draft" and "Trait stage" sections above.
- It assigns a default race name and plural name by decompressing a fixed name-table entry into the draft, then checks whether the result exactly duplicates a specific already-in-use name; if so, it redraws a different name-table entry rather than keeping the collision.
- It then repeatedly re-rolls and nudges the drafted values — first trying to toggle an axis's immune state, then flipping trait bits, then adjusting an economy field — checking after each attempt whether the draft's total race-design point cost (the same point-cost formula documented in `race-traits.md` §1a) has landed inside a target window of 0-50 points. If no combination of nudges succeeds after a bounded number of attempts, the generator gives up and overwrites the entire draft with a fixed 192-byte fallback template (confirmed this pass to be the Humanoid preset record, the first of the seven built-in presets — see "Economic-settings stage") and its own canned default name instead of leaving an out-of-budget random result in place.

This is the client's "Random" race generator: it does not merely roll independent random values and accept whatever point total results — it actively iterates toward a sensible (in-budget) random race, and falls back to a known-good template rather than ever presenting an invalid one. **Which of the 8 archetype-selector positions is "Random" is now confirmed:** `client-ui-dialog-catalog.md`'s real dialog-resource extraction recovered the Identity page's exact 8 button labels in order — Humanoid / Rabbitoid / Insectoid / Nucleotid / Silicanoid / Antetheral / Random / Custom — putting Random at the 7th position (0-indexed 6), with Custom as the 8th/final slot.

**The two individually-set trait bits are now identified, by cross-referencing their bit indices against other call sites of the same generic per-race trait accessors used throughout this project.** In `FUN_10e0_3e04` (the generator itself, `stars.exe.export.c` line 95069), the 14-bit LRT coin-flip loop runs over bit indices 0-13 (`FUN_10e0_2292`, one call per LRT checkbox, matching `race-traits.md` §1a's 14-entry wizard-order LRT table exactly), immediately followed by two more individual `FUN_10e0_2292` calls at bit indices `0x1d` (29) and `0x1f` (31) — well outside the 0-13 LRT range, confirming these are two *different* boolean settings, not two of the 14 LRTs. Both indices' accessors (the read side, `FUN_10e0_226e`, sharing the same per-race bit-field convention) are independently used elsewhere in this exact codebase in unambiguous contexts:

- **Bit `0x1d` (29)** is read at `stars.exe.export.c` line 94024, inside the stage-6 procedure's (the Research-cost-class stage) dialog-initialization handler, to set the checked state of dialog control `0x123` — the same control this handler's immediately-preceding code (line 94015-94021) dynamically labels "starts at tech level 3" or "...4" depending on whether the race's PRT is Jack Of All Trades. This is `race-traits.md` §1a item 8's **"starts at tech level 3 (4 for JOAT)" checkbox** (flat −180 raw / −60 RW points).
- **Bit `0x1f` (31)** is read at `stars.exe.export.c` line 93280, inside the stage-3 procedure's (the Economic-sliders stage) dialog-initialization handler, to set the checked state of dialog control `0x123` there. This is `race-traits.md` §1a item 10's **Germanium-cost-discount checkbox** (flat −175 raw / ≈−58 RW points).

So the Random generator's two individually-set trait bits are the **research "starts at tech 3/4" checkbox** and the **Germanium build-cost-discount checkbox** — the same two flat-cost checkboxes `race-traits.md` §1a already itemizes as items 8 and 10, confirmed here as living in the per-race trait-bit space (not the 14-slot LRT array) at fixed indices 29 and 31 respectively. This also independently corroborates `race-traits.md` §1a's own identification of those two checkboxes' point costs, via a completely different code path (the Random generator) than the one that originally found them (the point-total formula itself).

## Environmental-tolerance stage

Each environmental axis has a lower and upper tolerance bound in the normalized interval 0 through 100. An immunity setting is represented as a special state rather than an ordinary narrow or wide interval.

For a non-immune axis, the editor shows a central acceptable interval and unsuitable regions on both sides. Changing either endpoint updates the displayed availability estimate immediately. Turning on immunity sets that axis to its special full-tolerance state; turning immunity off restores the ordinary editable interval defined by the draft (confirmed default reset values: a 20-80 band centered on 50).

**Confirmed constraint, not previously documented:** adjusting a tolerance bound via the stepper controls enforces a **minimum band width of 20** (out of the 0-100 scale) — if a nudge would narrow the band below that width, the editor re-centers it to force exactly 20 rather than allowing a narrower interval.

The editor estimates the percentage of worlds matching all three selected intervals. Let the lower and upper bounds for an axis be `L` and `U`, inclusive where the control admits a discrete setting. Define the edge-weight function:

\(w(x) = x\) for \(0 \leq x < 10\);

\(w(x) = 10\) for \(10 \leq x \leq 89\);

\(w(x) = 100 - x\) for \(90 \leq x \leq 100\).

For each of the two middle-weighted axes, use the normalized coverage:

\(C_{middle}(L,U) = \frac{1}{9} \sum_{x=L}^{U} w(x)\).

For the uniformly distributed axis, use interval width:

\(C_{uniform}(L,U) = U - L\), with a full-span selection normalized to 100.

An immune axis contributes 100. The estimate's unscaled coverage is:

\(C = C_1 \times C_2 \times C_3\).

The interface clamps a zero or sub-unit result up to one unit before deriving its human-readable rarity message. This prevents the display from reporting an impossible or undefined result for a deliberately tiny tolerance area.

The ordering of the three controls is significant: the first two use the middle-weighted distribution and the third uses the uniform distribution. A compatible editor must not apply one uniform formula to all three axes.

## Economic-settings stage

**Recovered numerically from `stars.exe` itself (this pass), closing the long-standing "bound table is binary data, not extractable" gap shared by `production-queue.md` and `research-tech-tree.md`.**

The stage presents seven stepper rows plus one checkbox. Each row stores a small integer in one of the draft's generic per-race setting slots (the same 16-slot byte array, at race-record offset `0x3e`, whose slot 14 is already documented project-wide as the PRT index). The stage never hard-codes a bound. Every change goes through one shared clamp step: the candidate value is raised to the slot's minimum, then lowered to the slot's maximum, then stored. The minimum and maximum come from two 16-byte tables indexed by slot number. A whole-record validation pass (run at the start of every point-total evaluation) re-applies the same two tables to all 16 slots and sets a "record was corrected" flag if anything moved (`stars.exe.export.c` lines 94122-94133).

**Where the tables are, and why earlier passes missed them.** The decompiler renders the table reads as plain offsets `0x2f8` (maximum) and `0x308` (minimum), which look like references into the shared data segment. At those offsets the shared data segment holds only zeros. The clamp routine's compiled bytes actually read both tables relative to its own code segment. This is the same same-segment constant-data idiom that `ship-design-and-components.md` §15a documents. The tables therefore sit inside segment 29 (file start `0x897c0` from a freshly re-parsed NE segment table, alignment shift 6). They are contiguous with the three point-cost tables `race-traits.md` §1a already recovered from that segment: the research-bias refund table at `0x2ba`, the per-LRT table at `0x2c8` and the per-PRT table at `0x2e4`, which ends exactly at `0x2f8`. Re-reading the per-LRT and per-PRT tables from the same parse reproduced all 24 published values exactly (for example, Cheap Engines +240 and Space Demolition −150). That confirms the offset arithmetic before any new byte is trusted. Three small per-row layout tables follow at `0x318`, `0x31f` and `0x326`, and then comes code, starting at `0x32e`, the entry point that the wizard's driver hands to its first dialog. So the whole block is bounded by code on both sides, with no gap.

| Slot | Meaning (stage row) | Min | Max | Default | Displayed as |
|---|---|---|---|---|---|
| 0 | Colonists per 1 resource (row 1) | 7 | 25 | 10 | value × 100 → **700 – 2,500**, default 1,000 |
| 1 | Resources produced per 10 factories (row 2) | 5 | 15 | 10 | as stored |
| 2 | Resources to build one factory (row 3) | 5 | 25 | 10 | as stored |
| 3 | Factories operable per 10,000 colonists (row 4) | 5 | 25 | 10 | as stored |
| — | *(Germanium-discount checkbox is positioned here, between rows 4 and 5)* | | | off | factory Germanium cost 4 kT, or 3 kT when checked |
| 4 | kT of each mineral produced per 10 mines (row 5) | 5 | 25 | 10 | value + "kT" suffix |
| 5 | Resources to build one mine (row 6) | **2** | 15 | 5 | as stored |
| 6 | Mines operable per 10,000 colonists (row 7) | 5 | 25 | 10 | as stored |
| 7 | Leftover-advantage-point spending choice (Identity stage dropdown) | 0 | 6 | 0 | 5 options populated: Surface minerals, Mineral concentrations, Mines, Factories, Defenses |
| 8-13 | Research cost class, one per field | 0 | 2 | 1 | radio order in the dialog resource: 0 = "Costs 75% extra", 1 = "Costs standard amount", 2 = "Costs 50% less" |
| 14 | Primary Racial Trait index | 0 | 9 | 9 | the 10 PRTs; the default preset is Jack of All Trades |
| 15 | (unused by the wizard) | 0 | 0 | 0 | — |

Row-to-slot mapping is the identity (row *n* → slot *n* − 1, per the seven-byte table at `0x318`). The row labels are the dynamic-string pairs 246-259, drawn in row order; each pair is a sentence split around the row's value (row 1 reads as one resource per year for every N colonists, row 2 as N resources per year from every 10 factories, and so on through row 7's mines-operated sentence). A per-row display table at `0x31f` marks rows 1 and 5 as carrying a suffix string: "00" for row 1 (hence the ×100 display) and "kT" for row 5.

**Defaults.** The "Default" column is the first of the seven built-in preset records: Humanoid, stored in the shared data segment at offset `0x0d0a` as seven consecutive 192-byte race records. Every new-race entry point copies this preset into the working draft (`stars.exe.export.c` lines 1737, 7652 and 54894). The Random generator's fallback template, described above, is the same record (line 95433). A second, fully independent code path confirms the economic defaults as literals visible in the decompiled text. When the PRT stage selects Alternate Reality, it resets slots 1-6 to exactly **10, 10, 10, 10, 5, 10** and clears the Germanium-discount bit (lines 93759-93766). The bytes were also cross-checked against an earlier session's live memory dump of the running game's data segment, and all seven preset records were byte-identical to the file.

**Consistency checks that validate the extraction.**
- Every slider range matches the independently published race-wizard ranges: 700-2,500 colonists, factory output 5-15, factory cost 5-25, factories operated 5-25, mine output 5-25, mines operated 5-25.
- The one surprise, a mine-cost minimum of **2** rather than the sometimes-quoted 3, is corroborated by the point-total formula itself. That formula prices mine cost with an ordinary linear term for values of 3 and up and a separate flat branch reserved for "below 3" (`stars.exe.export.c` lines 94347-94352). A branch that handles only one value makes sense only if 2 is a legal setting.
- Likewise, the point formula caps its colonists-per-resource input at 25 and gives values below 8 their own branch, so 7 must be reachable (lines 94253-94262).
- The Humanoid defaults (1,000 / 10 / 10 / 10 / 10 / 5 / 10, Germanium discount off) match the empirically observed default build costs in `production-queue.md`: Mine 5 resources, Factory 10 resources + 4 kT Germanium.

**The other named presets, as stored (slots 0-6, then PRT):**

| Preset | Col/res | Fact. output | Fact. cost | Fact./10k | Mine output | Mine cost | Mines/10k | PRT |
|---|---|---|---|---|---|---|---|---|
| Humanoid | 1,000 | 10 | 10 | 10 | 10 | 5 | 10 | JOAT |
| Rabbitoid | 1,000 | 10 | 9 | 17 | 10 | 9 | 10 | IT |
| Insectoid | 1,000 | 10 | 10 | 10 | 9 | 10 | 6 | WM |
| Nucleotid | 900 | 10 | 10 | 10 | 15 | 5 | 3 | SS |
| Silicanoid | 800 | 12 | 12 | 15 | 10 | 9 | 10 | HE |
| Antetheral | 700 | 11 | 10 | 18 | 10 | 10 | 10 | SD |

Each preset's PRT agrees with its well-known in-game identity, which is a further check that the record layout is being read correctly. The Random slot's stored record holds 10/10/10/10/10/3/10. The Random generator's own economy step (lines 95231-95253) works as follows:
- One time in three, it copies Humanoid's seven economic values verbatim and picks the leftover-points choice uniformly from 0-4. Those are the five populated options, which independently confirms slot 7's five labels.
- Otherwise, it rolls each of slots 0-7 uniformly across that slot's own [min, max] range from the table above.

Slot 7's maximum is 6, so this second path can produce a leftover-points choice of 5 or 6, which has no dropdown label. This is a harmless quirk of the looser-than-needed bound: at game start values 5 and 6 act as Surface minerals (`new-game-setup.md` §3, "Leftover advantage points at game start"). Research-cost classes are likewise all Normal one time in three, or each uniform 0-2 otherwise. The PRT is uniform over 0-9.

**Stepping behavior.** Each row has a pair of step buttons, one incrementing and one decrementing. A click steps by 1, or by 3 while Shift is held, and auto-repeats while the button is held down. Every step goes through the clamp above, and only the changed row is redrawn (`FUN_10e0_2154`, lines 93514-93557).

**Alternate Reality variant.** When the draft's PRT is Alternate Reality, row 1's label pair is swapped for the AR text pair (dynamic strings 260/261, which state AR's income rule: annual resources equal the planet's value times the square root of population times Energy tech divided by the row's value). The value is shown without the "00" suffix, so an AR player sees the raw 7-25 slot value as the divisor, with a default of 10. Rows 2-7 are drawn in the disabled color with their step buttons marked disabled. This matches the point-total formula, which for AR skips every factory/mine term and applies a flat adjustment instead (`race-traits.md` §1a; lines 94280-94281).

## Trait stage

Each optional trait is a binary choice. Enabling a trait changes its visual state and associated advantage-point total. The stage redraws only the affected trait row when possible, but a compatible implementation may refresh the whole stage.

The editor maintains a draft-local trait set. It does not finalize the race merely because a trait checkbox changes. The trait stage is accepted or cancelled as a unit.

## Research-preference editor

Maintain one research preference for every research discipline, plus one global allocation mode. The selected discipline and allocation mode are encoded independently; changing either marks the draft modified.

When opened, the editor loads the stored values for the currently selected player or race. It computes a display-only total from that player's existing research-related records and presents the preference controls with localized labels.

On acceptance, write the selected discipline, allocation mode, and global mode only if at least one differs from the original values. A successful modification schedules the normal persistent-state update and, where applicable, a network/state synchronization notification. Cancelling makes no change.

## Battle-plan editor

Maintain an ordered list of battle-plan records per owner. Each record contains a name and compact selections for targeting, behavior, and opponent policy. The editor keeps a working copy of the active record; selecting another record first commits any pending working-copy edits, then loads the newly selected record.

The first plan is protected from deletion. Additional plans can be created until the owner's plan-count limit is reached. Creating a plan copies the active plan as a template, assigns a default name, and makes the new record active. If the name ends in a recognized one-character numeric suffix enclosed by parentheses, increment that suffix with wraparound after nine; otherwise append a generic default suffix. This is naming convenience only and must not alter plan behavior.

Changing a target class, behavior choice, opponent policy, or plan name sets the dirty state. The editor writes the working copy when the user changes records, creates a record, deletes a record, or accepts the dialog. Cancelling without any of those commits leaves the selected stored record unchanged.

Opponent choices exclude the owner when the game is configured for distinct competing owners. In a constrained or special game mode, the editor substitutes the mode's fixed opponent policy and disables the ordinary opponent selector.

## Cross-stage persistence

Every stage treats its editable values as a temporary working copy until an explicit accept action. Acceptance writes only that stage's relevant fields and then returns a result that determines the next stage or closes the wizard. Cancellation returns without committing the stage's unaccepted changes.

The editor may request help from any stage. Help is informational and never mutates the draft.
