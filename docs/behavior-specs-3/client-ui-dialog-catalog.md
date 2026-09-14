# Client UI Dialog Catalog

This catalog extends the client-interface specification with every distinct interactive surface identified in the exported client. Names below are functional descriptions, not copied identifiers. An entry states only behavior supported by the client surface; it does not infer rules that belong to turn processing.

## Global dialog contract

Every modal surface has these common attributes:

- It is initialized from an explicit subject, selection, or session draft.
- It supports accept, cancel, and contextual help when help is available for that surface.
- Accept returns a meaningful result to its caller. Cancel returns a distinct no-change result.
- Editable surfaces use a local working state and commit only their own scoped fields.
- Read-only and authority-limited contexts disable mutation controls while retaining useful inspection controls.
- Keyboard and pointer activation of an accept, cancel, list, or button control follow the same state transition.

## Workspace surfaces

### Main workspace frame

The top-level frame creates and owns the map workspace, command menu, toolbar, status content, and subordinate information windows. It routes keyboard accelerators to the active child surface when that child is entitled to receive them. It supports normal, minimized, and restored display states.

The frame handles controlled shutdown. When unsaved state or an active session requires a decision, it opens a confirmation flow rather than terminating immediately. It may save or prepare pending session data before completing the close action.

### Title and status area

The title/status area presents the current context and supports lightweight updates without rebuilding the map. It refreshes when the active owner, open game, or current view changes.

### Toolbar, tooltip, and contextual popup

The toolbar maintains an ordered group of icon actions. Each icon has an enabled state, a press/hover state, and explanatory tooltip content. Hovering does not change the selected object or persistent state.

The contextual popup is built from the current pointer target and selection. It contains only operations legal for that target. Closing it without activation has no effect.

### Search and record browser

The search surface accepts a query, evaluates it against the relevant object collection, and returns a selected result or no result. The browser surface presents a navigable collection with one current item and can update the workspace focus to that item.

## Object inspectors and operational editors

### Planet inspector

The planet inspector is an object-bound information surface. It redraws from the selected planet's known state, maintains a current display mode, and enables only planet-appropriate actions. Changing the active planet refreshes the inspector rather than reusing the previous planet's data.

### Fleet orders and order details

The order editor manages an ordered list of fleet instructions. The detail surface shows the parameters of one selected instruction. A fleet with pending waypoints has its route displayed on the map while it is selected, as specified in the client-interface document.

### Cargo transfer

The transfer editor is bound to two compatible inventories. It offers a quantity choice for each transferable category, updates the candidate result as values change, and commits only after validation. The editor cannot create a negative source amount or exceed a legal destination capacity.

### Fleet merge

The merge editor lists eligible fleets in the current context and requires an explicit choice before combining them. It excludes incompatible or unavailable entries. Accepting performs the merge request; cancelling preserves the pre-merge state.

Confirmed by inspection of the exported client: the merge editor pre-selects the fleet the merge was invoked from (and both fleets automatically, when exactly two are eligible). Per ship-design slot being merged, a design already at full fuel merges without penalty; a design below full fuel undergoes a probabilistic check that can leave some ships behind rather than merging them, with the result reported through one of five graduated messages. Designs with genuinely incompatible cargo or fuel capacity abort the merge outright rather than partially combining. After a successful merge, special-ability stats are recombined per field — some take the maximum value across the merged ships, others sum with a fixed cap. See `fleet-movement-scanning-cargo.md` for the full mechanic.

### Rename surfaces

The client has both ordinary rename and compressed-context rename surfaces. Each accepts a text value for the selected object, applies length and field validation, and commits only on accept. The compact variant has the same logical result and differs only in presentation.

Confirmed by inspection of the exported client: the two variants differ in *when* validation runs, not only in presentation. The ordinary rename surface filters and rewrites its text on every keystroke (live input filtering), in addition to validating again at accept time; the compact variant validates only once, at accept time, and shows different prompt wording depending on whether the target slot was previously empty.

### Slot/equipment editor

The slot editor presents a finite collection of assignable positions and compatible choices. Selecting a position loads its current assignment; selecting a choice updates the working design or order. Incompatible choices are not offered as valid assignments.

Confirmed by inspection of the exported client: compatibility checking is graded, not a simple yes/no — the editor can distinguish a choice that is fully available from one that is exactly one technology level away from being available, from one that needs multiple fields raised, from one that is unavailable for other reasons. This finer distinction is not conveyed by the general phrasing above and should be reflected in a compatible implementation's feedback to the user.

### Minefield and scanner views

The minefield view is an object-bound visual inspector. The scanner view displays scan-related coverage or results relative to the current selection or observer. Neither view grants information beyond the client's current known state. Both refresh when their input object, owner, or display parameters change.

## Session creation and configuration

### New-session flow

The client offers a simplified setup path and a multi-stage detailed setup path. Both operate on a session draft. The detailed flow separates general settings from later configuration stages; advancing carries the current draft forward, while cancellation leaves no finalized session.

See `new-game-setup.md` for the concrete parameter set (galaxy size/density options, player-slot assignment encoding) and the galaxy-generation algorithm this flow ultimately invokes, and `victory-conditions.md` for the detailed flow's final configuration stage.

### Randomization seed

The seed surface accepts a session-generation seed and validates its input before returning it to the setup draft. It distinguishes user-provided values from the request to generate a fresh random value.

### Local access-secret entry and replacement

The access-secret entry surface requests an existing secret. The replacement surface collects a new value and confirmation. Mismatched replacement entries fail validation and remain uncommitted. Both surfaces conceal typed characters and do not display stored secret content.

## Planning and management

### Production and compact production editors

The production editor manages an ordered construction queue. The compact variant provides the same logical queue operation for a constrained context. Each supports selection of an active queue item and changes to that item's requested work. Queue changes remain local until accepted through the applicable editor or workflow.

**Correction, verified against the exported client:** the "compact variant" identified in the exported client is not the same logical queue operation in a smaller window — it is a distinct **saved production-template manager** exposing exactly 4 named template slots, each holding up to 12 auto-build entries plus one setting flag, that can be applied to any planet's queue in one action (see `production-queue.md` §9). The full per-planet queue itself is edited through the (uncompacted) production editor only. A compatible implementation should model these as two related but functionally distinct surfaces, not one editor in two sizes.

### Research preferences

The research editor has independently editable discipline focus and allocation settings. It displays the applicable current settings, records a modification only when a value changes, and commits the changed preference set upon acceptance.

### Battle plans and plan names

The battle-plan editor manages per-owner ordered plan records. It can choose an active record, create a record from an existing template, remove non-protected records, edit targeting and behavior settings, and open a name editor for a new record. Plan details are committed when changing records or accepting the editor; cancellation avoids a final uncommitted write.

### Inter-owner relations

The relations editor presents a selected other owner and the current relationship policy. It restricts choices according to session mode and owner identity, then commits accepted relationship changes through the ordinary state-update path.

Confirmed by inspection of the exported client: exactly **three** mutually-exclusive relationship states are offered. A relationship change is submitted as a queued order rather than written immediately, taking effect at the next turn. See `diplomacy-relations.md` for the full mechanic, including how this setting interacts with battle-plan targeting and an automatic per-turn decay of the underlying relationship value.

## Information, reports, and output

### Messages

The message view presents recorded messages and maintains a current position within the collection. It includes four category-selection controls, a current-message area, and a scrollable or otherwise selectable message list. Changing a category or row refreshes the current-message area. Reading or navigating messages does not alter their underlying content. The current message and navigation availability update when the collection changes.

### Reports

The report viewer presents a selected report and supports navigation among report entries. Its content is informational; accepting or closing the viewer does not modify the report data.

Confirmed by inspection of the exported client: one shared report-grid window class serves **four** distinct report types, switched via a mutually-exclusive main-menu selection; the grid's visible columns are individually configurable, and clicking a column header versus clicking a data row dispatch to different handlers, consistent with (though not fully confirmed as) column-sort and row-navigate behaviors respectively.

### Score display

The score surface displays comparative standings derived from the current session information. It is read-only and may provide selectable display categories without changing game state.

Confirmed by inspection of the exported client: the surface offers **three** cyclable display modes (an icon/medal grid, a per-category leader table, and a per-category history graph) over **nine** comparison categories. Every value shown is read directly from a pre-computed per-player-per-turn record — no code in this surface computes a combined single "score" number from the nine categories, so a compatible implementation should not assume this dialog is where an aggregate score gets calculated, only where per-category standings get displayed. See `victory-conditions.md` for the separate victory-condition system, which may or may not consume the same underlying score record.

### Tutorial surface

The tutorial surface presents instructional material and can be opened independently of editing workflows. It may maintain a current topic or page. Opening, navigating, and closing it do not change the game draft or session state.

Confirmed by inspection of the exported client: this surface is the window/message-routing layer over a separate, turn-gated step-sequencer state machine — see `tutorial-system.md` for the full mechanic (lesson/step structure, advancement conditions, auto-advance behavior).

### Event replay

The event replay surface has a current playback position and transport controls. Moving playback position changes the displayed event state only. It cannot change the saved game state or event recording.

Confirmed by inspection of the exported client: this surface is used for tactical-battle replay and is **conclusively pure playback** — every value it displays or animates (token position, whether a beam or missile fired, resulting damage) is read from a pre-computed, round-by-round event log; no targeting, accuracy, or damage computation happens in this surface's own code. Its own grid-drawing and hit-testing code independently confirms the underlying tactical board is a 10×10 grid. See `combat-resolution.md` §8 and `turn-generation-engine.md` §2 for where the actual battle computation happens.

### Map printing

The map-print surface collects output configuration before the print action. Configuration changes remain local until the user confirms printing. Cancel stops the operation without sending output.

### Progress and failure feedback

The progress surface presents a numeric or visual completion indicator during lengthy work. The client may update the indicator repeatedly while the operation runs. A separate recovery or failure surface explains that an operation could not proceed and offers only safe follow-up choices; it must not silently fabricate a success result.

## Custom widgets

### Styled text entry

Styled text entry exposes ordinary editable text semantics: focus, text selection, insertion, replacement, validation, and commit. Its custom appearance must not change these semantics.

### Styled single-choice selector

The compact selector exposes one active choice from a finite option set. Changing the selection notifies the owning editor, which decides whether to mark the draft dirty.

### Styled list

The list widget maintains zero or one active row according to its owner's policy. It supports selection changes, redraw after content changes, and a callback into the owner for the new row. It does not autonomously persist an edit.

## Known boundaries

The exported client identifies these UI surfaces but does not, by their existence alone, establish exact localized captions, pixel layout, icon art, keyboard mnemonic letters, or host-side game-rule outcomes. A compatible clean-room implementation may choose different presentation while preserving the behaviors above.
