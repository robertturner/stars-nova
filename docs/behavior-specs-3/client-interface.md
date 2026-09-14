# Client Interface Specification

This specification defines the observable desktop interface exposed by the exported client. It uses generic terminology for screens and controls. It specifies user-facing state and transitions, not implementation technology, window messages, resource identifiers, artwork, or original text.

## Application shell

The application has a single top-level workspace with these persistent regions:

- A title area that identifies the current game or document context.
- A command menu for file/session operations, map navigation, object operations, reports, configuration, and help.
- A toolbar that exposes frequently used navigation and object commands as icon buttons.
- A primary map canvas.
- Transient status text, tooltips, progress feedback, and modal dialogs.

The workspace supports a draggable divider between its map and auxiliary regions. While the divider
is dragged, the client shows a live split preview and clamps the result to minimum usable sizes for
both regions. Releasing the pointer commits the new layout and redraws the affected views. This is
a layout preference only; it does not alter game data.

Commands may be available in more than one place. A toolbar button and its corresponding menu command must have the same availability, effect, and enablement state.

The interface responds to resizing by recomputing the usable client area and redrawing the active view. Controls that depend on text width or current display scaling must be repositioned rather than clipped.

## Command availability

Every command is evaluated against the current session, selected owner, selected object, and editing permission. A command is disabled when its required context is absent or read-only. Disabled controls remain visible unless the surrounding mode explicitly hides them.

Opening a modal editor suspends conflicting workspace commands until the editor is accepted or cancelled. Opening an informational view does not itself modify game state.

All editable dialogs use a working copy. Accept commits validated changes; cancel discards uncommitted edits. If a dialog changes a record before the user moves to another record, the interface commits the previous record's working copy before loading the new record.

## Map canvas

The map is the primary navigable surface. It displays spatial objects and accepts selection, navigation, and contextual interaction.

- Selecting an object makes it the active subject for object-specific commands and information panes.
- Selecting a fleet displays its pending route on the map. The route is drawn as an ordered series of legs from the fleet's current position through its remaining waypoints, so its intended travel path is inspectable without opening the order editor.
- The active fleet's current position, route origin, intermediate waypoints, destination, and leg direction are visually distinguishable. The selected route overlay takes precedence over ordinary map clutter and is redrawn whenever the selected fleet, its orders, or the map viewport changes.
- Selecting a different fleet replaces the previous route overlay. Deselecting the active fleet, closing the game context, or entering a mode that hides fleet data removes the overlay.
- Object-specific actions are unavailable until a compatible object is selected.
- A search dialog can locate a named or otherwise filterable object and then focus the map on the result.
- Contextual interaction opens a compact popup command surface whose available actions depend on the pointed object and current authority.
- Map printing opens a configuration dialog before output is produced.

The map supports additional overlays and inspector windows for scanning and minefield-related information. These views are derived from the current game state and refresh when the relevant selection or viewpoint changes.

**Starbase capability indicators, confirmed by inspection of the exported client.** The map's planet-icon painter draws up to three small colored square overlays ("dots") on a planet's icon, one per independent boolean condition, each in its own fixed position relative to the icon and its own distinct color:

- **Starbase presence** — read directly from the planet's stored status: a specific status bit is "has a starbase." When present, the code separately inspects the starbase's own design record: if a specific leading field of that design equals a distinguished value, the dot is drawn in a second, different color instead of the ordinary one — i.e., the client distinguishes at least one specific starbase design/type from an ordinary starbase using a different color for this same dot, though the exact design/type this represents could not be identified (no label/string data survives in the analyzed material).
- **Stargate capability** — a dedicated check walks the starbase's installed components looking for any component in a shared "orbital special equipment" category whose subtype falls in the lower half of that category's range, consistent with the game's roughly six Stargate tiers. Drawn as a second dot, in a second color, at a different fixed offset from the starbase dot.
- **Mass Driver capability** — the same shared component category is checked again, this time for a subtype in the upper part of its range, mapped to a driver level; the *highest* level found across all installed components is used. This range and mapping line up exactly with the game's known Mass Driver → Super Driver → Ultra Driver progression (levels 5 through 13). Drawn as a third dot, in a third color, at a third fixed offset.

**Scope caveat.** This detailed indicator rendering was only confirmed to fire for the planet matching the client's currently-tracked *selected-object* position (the same selection state the object-summary panel uses) — a compatible implementation should verify against live play whether every colonized planet's icon shows these dots simultaneously, or only the currently-selected one, before assuming universal rendering.

## Navigation controls

The client provides several related navigation affordances:

- A toolbar for direct map and object-navigation commands.
- Tooltip text for toolbar actions after pointer hover.
- A browser-style selection window for moving through a collection of records.
- A find dialog for locating a specific target.
- A compact popup window for local contextual commands.

The toolbar is a vertically arranged, finite action strip. It supports hover feedback, press and
release feedback, disabled actions, and at least one action with a finite preset-value drop-down.
Changing that preset immediately updates the associated view setting and its displayed value.

Confirmed by inspection of the exported client: the toolbar's button set and ordering are
**user-configurable and persisted** across sessions via a saved preference string, rather than
being a fixed layout — not previously documented here. A separate, similarly-persisted preference
governs an autosave/backup interval, clamped to the range 100-30,000 (milliseconds or an
equivalent internal unit), defaulting to 5,000.

List and browser views maintain a current item. Moving to a different item updates dependent controls and must not lose pending edits to the previous item.

## Object information and orders

The client provides dedicated views for planets, fleets, minefields, scans, messages, and reports. An object-information dialog displays the current object's known state and exposes only actions valid for that object.

Confirmed by inspection of the exported client: the order-information dialog referenced elsewhere in this document has no observed initialization step that populates it from a specific order's live data — a compatible implementation should not assume this dialog is more dynamic than a fairly static informational popup unless further evidence of a data-driving caller is found.

The message view exposes exactly **four** category-selection controls (confirming the count stated in the dialog catalog) and maintains, per message, an associated target object and type code; activating a message (via Enter or double-click) jumps to one of at least eleven distinct destination views/actions depending on that message's recorded type — object-information views, a rename prompt, a technology/component availability check, and others — rather than only ever opening a single generic "jump to source" view. Hovering the portion of a message's text that carries a jump target shows a distinct pointer affordance.

The minefield inspector includes a compact child selector for its active display option. Selecting
a different option updates the stored local view preference and redraws the inspector; it does not
modify the minefield itself.

Fleet-oriented interaction includes:

- A route or order editor.
- An order-information dialog for inspecting an assigned order.
- A cargo-transfer dialog for moving eligible quantities between compatible endpoints.
- A fleet-merge dialog that combines compatible selected fleets after confirmation.
- Rename dialogs for user-editable labels.

The transfer interface validates both sides of a transfer before committing it. A compatible implementation must prevent quantities from becoming negative and must respect the selected source and destination's eligibility and capacity constraints.

Order and production editors offer a compact, list-based editing surface. Selecting a row loads its associated data; inserting, removing, or changing a row marks the relevant working copy as modified. A production-order editor and a compressed order/production editor are both present, allowing the client to use an appropriate interface for normal and constrained display contexts.

## Planning and diplomacy

The client exposes independent editors for research preferences, battle plans, and inter-owner relations.

- Research preferences select a focus discipline and allocation behavior.
- Battle plans are ordered, named records with targeting, movement/behavior, and opponent-policy attributes. The first record cannot be removed; later records can be created, selected, edited, and removed subject to the applicable plan limit.
- Relations are edited through a dedicated dialog and are separate from battle-plan targeting preferences.

These editors must preserve the selected owner context, distinguish a changed working copy from a stored record, and synchronize accepted changes through the ordinary persistence pathway.

## New-session and game-setup flow

The client supports both simplified and detailed new-session flows. The detailed flow is divided across multiple dialogs and presents one coherent setup draft. A random-seed dialog is available where reproducibility or explicit randomization configuration is required.

The setup flow includes a six-stage race designer. Its stages cover identity and archetype, environmental tolerance, optional traits, economy, research, and finalization. The full behavior of that wizard is specified in the race-designer specification.

The setup flow must retain user-entered values while moving between stages and must not create a usable session until required stages have accepted valid input.

## Reports, messages, and playback

The client has separate information surfaces for messages, reports, scores, tutorials, and battle or event playback.

- Message and report windows present recorded information without changing it merely by opening it.
- A score view presents comparative standings.
- A tutorial view can be opened independently of game-state editing.
- A replay controller presents recorded event progression using transport-like controls; playback changes only the displayed point in the recording, not the underlying game state.
- Long-running actions use a progress dialog with a gauge and may display a failure or recovery dialog when the action cannot complete normally.

## Authentication and sensitive settings

The client contains entry and replacement dialogs for a local access secret. These dialogs must keep entered characters concealed, require explicit acceptance to save a replacement, and discard incomplete or cancelled input. The secret entry surface must not reveal the stored value through tooltips, reports, or ordinary editing controls.

## Reusable control behavior

Several custom controls supplement standard text fields, lists, and buttons:

- A styled editable field behaves as a text input while preserving the application's visual treatment.
- A styled selection field behaves as a compact single-choice control.
- A list control supports selection changes and event-driven redraw.
- Tooltip controls describe adjacent actions without mutating state.

For all of these controls, keyboard activation and pointer activation must reach the same logical command. Focus changes must be visible and must not silently commit unrelated input.

## Help and error feedback

Most modal editors provide contextual help. Invoking help leaves the editor's working copy unchanged. Invalid operations or unrecoverable conditions produce a generic message or recovery dialog rather than silently changing state.

The UI must preserve a distinction between an informational warning, a validation failure, and a cancelled operation. Only an accepted operation may alter persistent game or configuration state.
