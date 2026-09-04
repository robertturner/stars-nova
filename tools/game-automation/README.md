# Game automation harness

`automation.ps1` drives the real Stars! game via PowerShell + raw Win32 API calls (P/Invoke) — no
dedicated computer-use tool was available, so this fills that gap. Load it with `. .\automation.ps1`
from a PowerShell session, then compose the functions below to click through the actual game,
screenshot it, and read the screenshots back for empirical verification of behavior-spec claims.

Requires the game + otvdm already set up per `docs/PROJECT-STATUS.md`'s "Testing setup" section
(as of 2026-09-03, that's `C:\StarsGame\` on the `ROBSAMD` machine — game and otvdm binaries are not
in this repo, they're copyrighted/large and live outside version control).

`nova_automation.ps1` is the equivalent harness for driving our own built `Nova.exe` (added
2026-09-04, once the project pivoted to forking Stars! Nova — see `docs/PROJECT-STATUS.md`'s "Live
playtesting findings" section for what it found). It targets controls directly via
`SendMessage`/`PostMessage`/`BM_CLICK` rather than cursor coordinates, which proved more robust for
a WinForms app — see the lessons-learned comment block at the top of that file before using it, in
particular the list-view double-click warning and the ListView deadlock note.

## Functions

- `Pin-GameWindow` — moves whichever game window is currently frontmost to a fixed screen origin
  `(100,100)` so click coordinates stay stable across calls. Call this after any navigation that
  might open a new window or dialog, before computing click coordinates from a screenshot.
- `Get-GameWindowHandle` — returns the topmost visible window belonging to the `otvdm` process
  (whatever dialog/screen is currently frontmost), regardless of its title.
- `Click-InGame x y` / `Type-InGame text` — click or type relative to the pinned origin, always
  re-focusing the game window first. Use for anything inside the main pinned window.
- `Click-Absolute x y` — click at real screen coordinates, no origin offset. Use for popup dialogs
  Windows places on its own (e.g. centered on screen) rather than relative to the pinned window —
  mixing this up with `Click-InGame` is the most common source of missed clicks.
- `Screenshot path` — captures the full screen to a PNG at `path`. Read it back with Claude's `Read`
  tool (coordinates in the raw PNG file are actual screen pixels, 1:1 — no scaling. The *displayed*
  thumbnail Claude's image viewer shows is scaled down and reports its own scale factor; don't
  confuse the two coordinate spaces).
- `Get-VisibleWindows` — lists all visible top-level windows with title + owning PID; useful for
  figuring out what a newly-opened dialog is actually called when clicks/keys aren't landing where
  expected.

## Lessons learned (2026-09-03 session)

- **Mouse clicks on the game's own menu bar became unreliable partway through the session**, for no
  clear diagnosed reason (window was confirmed focused and at the expected position). Keyboard
  shortcuts (e.g. `F5` for the Research screen) and Alt+mnemonic accelerators (e.g. `%c` style
  `SendKeys` for a direct accelerator) worked reliably throughout — prefer keyboard navigation over
  clicking menus when a shortcut exists.
- **A dialog is not always the same window as the one you last pinned.** `Get-GameWindowHandle`
  always re-resolves to whatever's frontmost, but if a *new* popup opens (e.g. Ship Design dialog
  from an accelerator key), it usually appears at a Windows-chosen position, not at the pinned
  origin — use `Click-Absolute` for it, and re-run `Pin-GameWindow` + a fresh screenshot before
  going back to `Click-InGame` on the main window.
- **Keystrokes can leak to the wrong window if focus drifts** between taking a screenshot and
  clicking/typing (e.g. if something else steals foreground focus in between calls) — this
  literally happened once, typing a serial number into the Claude Code chat box instead of the
  game's serial dialog. `Type-InGame`/`Click-InGame`/`Click-Absolute` all call `Focus-GameWindow`
  immediately before acting specifically to guard against this, but it's still worth a sanity
  screenshot after anything that types sensitive/stateful text.
- Clicking the same on-screen spot repeatedly (e.g. a planet with multiple fleets in orbit) cycles
  selection through each fleet there, then the planet itself — there's no separate single click
  target that jumps straight to the planet if fleets are stacked on it.
