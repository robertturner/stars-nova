# Race Designer Interaction and Planet-Availability Estimate

This specification covers behavior observed in the game client's race-design, research-preference, and battle-plan editing screens. It deliberately describes externally meaningful state, validation, and calculations only. It does not define rendering, operating-system event handling, storage layout, or any game-turn resolution that is not established by these screens.

## Scope and confidence

The client implements a six-stage race-design flow. The exact labels and art assets are presentation details and are outside this specification. The behavior below is suitable for a compatible user-facing editor.

The planet-availability calculation is directly supported by the exported client logic. Whether the host uses the same calculation for galaxy generation is not established here; treat it as the designer's displayed estimate unless separately verified.

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

When the stage is accepted, the editor commits the identity fields, selected archetype, and appearance variant to the draft. Cancelling leaves the stored draft unchanged.

## Environmental-tolerance stage

Each environmental axis has a lower and upper tolerance bound in the normalized interval 0 through 100. An immunity setting is represented as a special state rather than an ordinary narrow or wide interval.

For a non-immune axis, the editor shows a central acceptable interval and unsuitable regions on both sides. Changing either endpoint updates the displayed availability estimate immediately. Turning on immunity sets that axis to its special full-tolerance state; turning immunity off restores the ordinary editable interval defined by the draft.

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
