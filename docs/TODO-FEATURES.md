# Future features (not scheduled)

A parking lot for features we intend to build one day. Nothing here is committed work. Each entry says what we want, what it touches, and the questions to settle before starting.
Spec-driven gaps are tracked separately in `behavior-specs-11-coverage.md` and `behavior-specs-10-questions.md`; this file is for product features beyond the specs.

---

## 1. Ship Design tab: single-tap details, cargo capacity and other stats (Avalonia UI)

**Today.** On the Ship Design tab, tapping an existing design does not show its details in one step. The design summary also omits some figures the original game showed.

**Wanted**
- A **single tap** on a design in the "existing designs" list shows its details straight away (no second tap, no long-press, no double-tap).
- The details panel shows **cargo capacity**, plus every other figure the original game's design screen displayed. To check against the specs rather than memory: `docs/behavior-specs-11/ship-design-and-components.md` (design summary / cost / stats sections) and `client-ui-dialog-catalog.md` (Ship Designer). Candidates to confirm there:
  - mass, cost (resources + ironium / boranium / germanium), fuel capacity, cargo capacity;
  - armor, shields, initiative, battle speed (movement), cloak percentage, scanner range (normal and penetrating), jammer, deflector and capacitor effects, beam/torpedo/bomb totals, mine-laying and terraforming ability, mass-driver rating, built-in Jump Gate;
  - the design's tech requirements and whether it is currently buildable (the graded tech-shortfall helper `Nova.Client/DesignAvailability.cs` already exists);
  - the fleets and starbases using it (the slot-removal confirmation helper exists but is not wired).
- Same behaviour on desktop and Android; the rendered result should be covered by a headless view test in `Nova.Avalonia.Tests`.

**Where it lives.** `Nova.Avalonia/ViewModels/Panels/ShipDesignViewModel.cs` and its view (`OwnedDesignRowViewModel` for the list rows); pure formatting in `Nova.Client`; design figures come from `ShipDesign.Update()` / `ShipDesign.Summary`.

**Open points**
- Which figures the original actually showed, and their wording and order (confirm in the specs; strings are not in the repo, so wording stays a stand-in).
- Whether the tap should also select the design for editing. The editor currently has add and delete only; an Edit flow does not exist, and `DesignCommand` Edit currently scraps every ship of the edited design (spec says editing an in-use design only prompts).

---

## 2. Server / client split with a web client

**Goal.** Turn the game into a hosted, multi-user service: a backend server (container-friendly) that runs games, and multiple clients - including **web client** - that connect to it. Players log in, see a lobby of their games, and play from a browser (phone-sized or desktop-sized) or Android or PC clients.

### 2a. Backend server (dockerable)

- A standalone service (ASP.NET Core on .NET 9, reusing `Common`, `ServerState`, `Nova.Ai` and `Nova.Client`) shipped as a **Docker image** with a persistent data volume for game folders and user data.
- Owns game creation, order submission, AI turns and turn generation (the in-process loop in `Nova.Avalonia/TurnHost.cs` and the headless runner in `Nova.Sim` are the starting points). Turn generation must stay deterministic and run inside the game's own settings (see `GameRandom`, `ServerData.UseSettings`).
- API surface (REST + a push channel such as WebSocket/SSE for "your turn is ready"): authentication, lobby, create/join game, fetch the player's intel/report for the current turn, submit orders (the existing command objects), submit/un-submit turn, scores and messages, race upload, admin endpoints.
- Fog of war is enforced **server-side**: a client only ever receives its own empire's intel (the `IntelWriter` / `EmpireData` view), never `ServerData`.
- Config via environment variables / a mounted config file; health endpoint; structured logging; graceful shutdown that never leaves a half-written game folder.
- Support submitting games from clients which have gone offline. Support submitting either once re-connected or via upload.

### 2b. Accounts and lobby

- **Login** with email address, display name and password. Passwords stored as salted slow hashes (Argon2id or bcrypt), never in clear; sessions via short-lived tokens with refresh; rate limiting and lockout on repeated failures.
- **Predefined user list** supplied to the server at start-up (config file or environment, e.g. a seed list of email / display name / initial password or invite) so the very first administrators exist without a database step.
- **Admin rights.** Admins can add users and promote or demote other users to admin. Users can change their own password and display name. At least one admin must always remain.
- **Lobby** (the first screen after login): the user's games with status (waiting for me / waiting for others / finished), turn year, players, and a **Join / Continue** action to open their current game. Admins also see a user-management page and can create games and assign users to player slots.

### 2c. Web client

- A browser app talking only to the server API. Candidate stacks: ~~Blazor WebAssembly (shares C# models and `Nova.Client` logic, closest to the existing Avalonia code) or~~ Avalonia's WebAssembly/browser target (shares the actual views). Decide after a short spike; either way, game rules and rendering maths come from the shared `Nova.Client` code, not reimplemented in JavaScript.
- **Two layouts, one app**
  - **Phone-size layout**: mirrors the existing Avalonia mobile UI (single-screen navigation, map plus panels as pages, burger menu, touch gestures).
  - **Large-screen layout**: mirrors the original game and the desktop Avalonia shell: **dockable dialogs/panels** (planet, fleet, production, research, ship design, battle plans, relations, messages, score, map), menu bar and accelerators.
  - **Switching by window size**: resizing the browser window across a breakpoint swaps between the two layouts live, **without losing state** (selection, open orders, unsaved edits, scroll/zoom). A shared view-model layer drives both layouts so the swap is just a different shell.
- Full parity target: everything the Avalonia client does today (map overlays and zoom, orders, production queue and templates, research, designs, race designer, battle plans, messages, score, reports), plus the New Game / race designer flows for admins.
- Installable as a PWA; works on touch and mouse; accessible keyboard navigation in the large layout. Native Windows and Android Avalonia apps also available.
- Can work offline

### 2d. Cross-cutting

- **Shared client core.** Move anything the Avalonia UI keeps in view models that is really client logic into `Nova.Client` so both front ends share it (much of it already is).
- **Security.** HTTPS only (terminate TLS in the container's reverse proxy), CSRF/CORS policy, input validation on every order (reuse `Command.IsValid`), no trust in client-supplied state, audit log for admin actions.
- **Multi-game hosting.** Many games per server; per-game locks so turn generation never overlaps order submission; backups of game folders.
- **Testing.** API tests, a scripted multi-user session test (the simulation harness can drive AI clients), browser tests for both layouts and the breakpoint swap.
- **Offline / local play stays.** The desktop and Android apps keep working standalone; the server is an additional way to play, and a sync target for them.

### Open questions to settle first
1. Hosting model: ~~one server per group of friends (simple, single volume) or~~ a multi-tenant public service?
2. Turn handling: wait for all humans, a deadline per turn, or both? Who can force a turn?
3. Stack choice for the web client (Blazor WebAssembly vs Avalonia browser target) after a spike on map rendering performance and touch handling. Answer: Avalonia browser target (closest to native experience & code reuse)
4. Where race files and AI personality choices live and who may upload them. Server user data. Any user can upload 
5. Password reset and invitation flow (email sending is out of scope unless wanted: admin-set temporary passwords may be enough at first).
6. Whether existing Avalonia clients can also log in to the server (a "remote game" mode) or whether the web client is the only network client. Answer: Yes
7. Data retention: finished games, replays, and exporting a game back to a local folder.

### Suggested staging
1. Server skeleton + Docker image + auth + seeded admin users + lobby API.
2. Read-only web client: login, lobby, view the current turn's map and reports.
3. Orders and turn submission; server-side turn generation with AI players.
4. Phone layout parity, then the large-screen docked layout, then live breakpoint swapping.
5. Admin tools (user management, game creation), polish, security review.
