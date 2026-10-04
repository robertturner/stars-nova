# Behavior-Specs-6 Audit — coverage vs. current implementation

Audit date: 2026-09-23. Revises `docs/behavior-specs-5-audit.md` (2026-09-22) for
`docs/behavior-specs-6/`. Everything in that prior audit still applies except where this document
says otherwise — this is a **delta audit**, not a from-scratch rewrite, since only 2 of the 18 spec
files actually changed between v5 and v6.

Legend: **CONTRADICTION** = code currently does something that conflicts with the spec (a bug).
**GAP** = spec describes something not implemented at all, or only partially. **CONFIRMED** = code
already matches. **AMBIGUOUS** = spec itself leaves a real implementation choice open, or is
simply silent on a question the code has to answer.

---

## Scope: only 2 of 18 files changed since v5

| File | Diff size | What changed |
|---|---|---|
| `fleet-movement-scanning-cargo.md` | ~40 new lines | **Directly resolves last audit's biggest open question** — the client's per-waypoint Warp Factor control is now confirmed at the code level (a 12-value range: 0-10 ordinary warp, plus a distinct 11th value meaning "Use Stargate"), Warp 0 is confirmed as a dedicated "hold position" order, and the full 10-entry Waypoint Task nibble table is now resolved end-to-end (Patrol and Transfer Fleet newly identified as real, previously-unconfirmed mechanics; Route finally named) |
| `race-traits.md` | 4 lines | Ultimate Recycling's "resources available the following year, not immediately" claim is now confirmed at the code level, with the actual mechanism described (a deferred per-planet resource array consumed during that planet's own next-turn production calculation) |

All 16 other files are byte-identical to v5 — nothing in `docs/behavior-specs-5-audit.md` needs
re-checking for those, including the entire new-in-v5 map-overlay gap (`client-interface.md`,
unchanged) and the production-queue color-coding confirmation (`production-queue.md`, unchanged).

---

## Headline result: yesterday's biggest "Ambiguous" item is now CONFIRMED

Yesterday's audit flagged this session's Stargate "use Stargate" warp-speed-selection redesign as
**AMBIGUOUS**, because no spec document traced the client-side order UI at all. v6 closes that gap
directly, and the result is about as clean a confirmation as this kind of audit ever gets:

> **The per-waypoint Warp Factor control, confirmed by inspection of the exported client.** ...The
> valid range confirmed in the movement-processing code... is exactly the UI's 12-entry range: 0
> through 10 as ordinary warp speeds, plus a distinct 12th value (nibble 11, byte value 0xB0)
> meaning "Use Stargate." ...the movement code explicitly branches on whether the stored value is
> below 0xB0 (ordinary engine movement...) or equal to 0xB0 (routed instead to the Stargate-jump
> handling code...) — confirming 0xB0 is treated as a distinct case throughout, not a large
> ordinary number.

This is a **CONFIRMED, near-exact match** for `Global.StargateWarpFactor = 11`
(`Common/GlobalDefinitions.cs`) and `TurnGenerator.TryStargateJump`'s
`waypointZero.WarpFactor != Global.StargateWarpFactor` gate (added this session, before this spec
update existed) — right down to using the value one past the highest ordinary warp speed as a
distinct sentinel rather than folding Stargate use into the ordinary eligibility check. Move this
item from "Ambiguous" to **CONFIRMED** in the running tally. Recommend folding this newly-sourced
mechanism into `fleet-movement-scanning-cargo.md`'s own permanent record next time that document is
touched (it's now spec-confirmed, not merely implementation-invented) — though that's a documentation
housekeeping note, not an action item for the code.

---

## New findings from v6

### CONTRADICTION (new): Ultimate Recycling's resource bonus is credited immediately, not deferred a year

v6 confirms, at the code level, a concrete mechanism behind Ultimate Recycling's documented
"resources... available starting the following year" behavior: the original game diverts a
UR-race's Scrap Fleet resource recovery into a **separate per-planet deferred-resources array**,
which is only read back during *that planet's own* resource-production calculation later in the
turn-generation pass — specifically **after** that pass has already finalized the current turn's
production numbers for the planet in question, so the credit can only affect next turn's output.

Current code (`Common/Waypoints/ScrapTask.cs:140`) does not defer anything — it credits
`star.ResourcesOnHand += returned` (including the UR-only resource portion, `returned.Energy`)
immediately, unconditionally, in the exact same call. And that call happens **before** production
spending even runs this same turn: `ScrapTask.Perform()` is invoked either from
`ServerState/TurnSteps/ScrapFleetStep.cs:45` (a "waypoint 0" scrap, run at
`TurnGenerator.cs:144`, near the very start of `Generate()`) or from the general per-fleet
arrival-task dispatch inside the fleet-movement loop (`TurnGenerator.cs:~165-168`) — both of which
run **before** `StarUpdateStep` (registered in `turnSteps`, processed at the end of `Generate()`),
which is what actually calls `manufacture.Items(star)` to spend `ResourcesOnHand` on the production
queue (`ServerState/TurnSteps/StarUpdateStep.cs:89`). Since both the credit and the spend happen
within the same single `Generate()` call, a Ultimate Recycling race's scrap-resource bonus is
available for that same turn's production, not "starting the following year" as now explicitly
confirmed. The doc-comment already in `ScrapTask.cs:85-88` even states the "next year" rule in
prose — the code just never implements the deferral it describes.

This is a genuine, previously-unconfirmed bug (v5's audit couldn't have caught it — the deferred
mechanism wasn't code-confirmed until this spec revision). Fixing it correctly would need a
per-planet deferred-resource accumulator (paralleling the array the spec describes) applied at the
start of `StarUpdateStep`'s per-star processing, before that star's own `manufacture.Items` call —
not a one-line fix, since it needs new persistent state that survives from one turn's
`ScrapTask.Perform()` to the next turn's `StarUpdateStep`.

### GAP (upgraded from "unconfirmed" to "confirmed real, and absent"): Patrol waypoint task

v6 resolves what was previously "no executing branch found... falls through to generic
housekeeping" into a fully-specified, real mechanic: Patrol (waypoint-task nibble 7) is a
radius-gated automatic-redirect that, each time it's evaluated, finds the nearest fleet the
patrolling race can detect and confirms hostile via the relationship table, and — if within a
stored 0-10 range value converted to light-years via `(value + 1) × 50` (value 10 = unlimited
10,000 ly) — inserts a fresh waypoint aimed at that hostile's *current* position, re-tagged with
the same Patrol task so it re-evaluates again next time. This deliberately achieves a
pursuit-like effect for exactly this one task type, without contradicting the document's separate
"no dynamic interception" finding for ordinary waypoints (Patrol works by re-generating a new
static waypoint each pass, not by making an existing waypoint track a moving target).

Nova has **no Patrol task at all** — confirmed via `Common/Waypoints/` containing only
`CargoTask`, `ColoniseTask`, `InvadeTask`, `LayMinesTask`, `NoTask`, `ScrapTask`, `SplitMergeTask`.
This was already implicitly true before (nothing pointed to it existing), but it's now a
**confirmed-real gap** rather than a documented-uncertain one — worth prioritizing accordingly if a
"defend this region automatically" order is ever wanted.

### GAP (newly resolved from unknown to a named, real mechanic): Transfer Fleet waypoint task

v6 fully resolves waypoint-task nibble 9 as **Transfer Fleet** — a cross-player fleet gift:
validates the recipient race (a valid, non-removed, non-blocked-relationship slot), rejects if the
fleet still has cargo aboard, rejects again if the recipient is already at the 512-fleet cap, then
creates a new fleet for the recipient (copying position, cargo, and each installed design —
matching capability where the recipient already owns an equivalent design), destroys the source
fleet, and messages both players. Confirmed only ever executes in the dispatcher's *final*
per-turn pass, matching "cross-player transfers always happen after movement."

Nova has **no Transfer Fleet task** in `Common/Waypoints/` — another confirmed-real gap, previously
just "task code 9, meaning unknown."

### GAP (name confirmed, still a gap): Route waypoint task

Waypoint-task nibble 8 — previously an unidentified "ninth task-code branch" with unclear purpose —
is now named **Route**: requires the target to resolve to a friendly, populated planet, falls back
to a conversion routine on an invalid target, and can re-dispatch into the Merge-with-Fleet handler
under one condition. Still not implemented anywhere in Nova — same gap as before, now with a name
attached rather than a "not conclusively pinned down" placeholder.

### CONFIRMED (mostly): Warp 0 as a dedicated "hold position" order

v6 confirms Warp 0 is a genuine, distinct "stay put this turn" order in the original game — the
per-turn movement sweep explicitly checks for a stored warp nibble of exactly 0 and skips *all*
movement/arrival processing for that fleet outright, the same early-exit used for Transport and
Lay-Mine-Field tasks (both of which also keep a fleet stationary).

Nova doesn't have an explicit check like that, but produces the same observable *outcome* for the
core case as a side effect of its existing math: `Fleet.Move()` (`Common/GameObjects/Fleet.cs:551-556`)
computes `speed = warpFactor²= 0`, giving an infinite `targetTime`/`fuelTime`, which the existing
"clamp travel time to whichever is smaller" logic (`Fleet.cs:567-577`) reduces to the turn's full
available time while covering zero distance — so a fleet ordered to Warp 0 toward a real
destination sits still and is marked `InTransit` (never `Arrived`), meaning no arrival-task
execution runs for it either. `ShipDesign.FuelConsumption` already special-cases `warp == 0` to
return 0 (`Common/Components/ShipDesign.cs:880-883`), so there's no division-by-zero or
out-of-range risk. Net effect matches the spec for the case that matters.

One minor, low-priority divergence: the spec's early-exit is a hard skip of *all* processing for
that fleet, including — by implication — the per-turn minefield check; Nova's Warp-0 fleet still
passes through `checkForMinefields.Check(fleet)` afterward (`TurnGenerator.cs`, in the same
"ordinary movement" branch), since that check isn't gated on warp factor or task type at all.
Whether a real Warp-0-holding fleet should be exposed to minefield damage isn't itself resolved by
this spec passage (it only says movement/arrival processing is skipped, not that minefield checks
specifically are) — flagging as **AMBIGUOUS**, not a confirmed contradiction, and very low
priority regardless.

---

## Everything else: unchanged from the v5 audit

No other item in `docs/behavior-specs-5-audit.md` needs revision — the 17-item contradiction
re-verification (13 fixed / 1 partial / 3 deliberately deferred / 0 regressed), the production
auto-build-throttle note, the ship-mass-display note, the map-overlay gap analysis, the
`race-traits.md` documentation-drift note (v5 was stale relative to v4 in the PRT-starting-ships
section — still true, v6 didn't touch that section), and the recommended-next-steps list all carry
forward as written. Refer to that document for the full text; this one only covers what v6 changed.

## Updated coverage tally

- **Contradictions tracked**: 17 (v4 baseline) + 1 new (Ultimate Recycling deferral) = **18**. 13
  fixed, 1 partial, 3 deliberately deferred, **1 new, unfixed** (Ultimate Recycling).
- **Ambiguous items**: 1 resolved to Confirmed this pass (Stargate speed selection). 1 new, minor
  (Warp-0 minefield exposure). Net: same count, different membership.
- **Gaps**: 2 newly confirmed-real (Patrol, Transfer Fleet), 1 newly named but still open (Route).
  Everything else in the v5 gap list is unchanged.

## Recommended next step

Same priority order as the v5 audit, with two insertions:

1. Sync `race-traits.md`'s stale PRT-starting-ships section from `behavior-specs-4`'s later
   revision (unchanged recommendation, still pending).
2. **New**: fix Ultimate Recycling's resource-deferral bug if UR races are in active use — it's a
   real, now-confirmed bug with a small blast radius (one trait, one task), unlike most of this
   backlog's larger open items.
3. Decide on the map-overlay system (unchanged from v5 — the largest real gap by volume).
4. Fold the now-spec-confirmed Stargate speed-selection mechanism into
   `fleet-movement-scanning-cargo.md` as documented behavior (housekeeping, not code).
5. Patrol and Transfer Fleet are worth a look if either "auto-defend" or "gift a fleet to an ally"
   is ever requested — previously not even worth scoping (unconfirmed whether they existed as
   mechanics at all), now they're fully-specified and just need building.
6. Everything else carries forward unchanged from the v5 audit's own list.
