# UI testing (Nova.Avalonia.Tests)

Headless tests for the shared Avalonia app (`Nova.Avalonia`). They run on any machine with the
.NET 9 SDK; no display, emulator or desktop session is needed.

```
dotnet test Nova.Avalonia.Tests
```

(When other agents share the working tree, run it under the same build lock as the main suite:
`until mkdir /tmp/nova_build.lock 2>/dev/null; do sleep 4; done; ...; rmdir /tmp/nova_build.lock`.)

## How it works

- **Framework:** NUnit 4 + `Avalonia.Headless.NUnit` (same version as the app's Avalonia, 12.1.2).
  NUnit 4 rather than the 3.x line `Tests` pins, because the Avalonia headless package needs it;
  the tests use `Assert.That` only.
- **App:** `TestAppBuilder` starts the real `Nova.Avalonia.App` (its Fluent, DataGrid and Dock
  styles and its `ViewLocator`) on the headless platform with Skia drawing, once per assembly
  (`AvaloniaTestIsolation(PerAssembly)`). Every test is an `[AvaloniaTest]`, so it runs on the UI
  thread.
- **Data:** `TestGame` generates one small real game per run with the engine's own
  `Gameinitializer` (two human players built from `DefaultRaces/Humanoid.race`: a Packet Physics
  race "Packeteers" and a Space Demolition race "Demolishers"), then gives every test its own
  copy of that game folder loaded through `GameSession.Load` - the same path the app uses. Tests
  can mutate their `ClientData` freely.
- **Isolation from the machine:** the project copies `components.xml`, `DefaultRaces`,
  `HelpContent` and `Graphics` next to the test assembly and points
  `PlatformHooks.NovaRootOverride` there, so `nova.conf` is written into the test output folder,
  never into a real installation. Generated games live in a temp folder deleted after the run.
  The star map's view options are static; `TestGame.Load` resets them to the shipped defaults.
- **Views:** `Headless.Show(control)` hosts a view in a headless window and pumps layout and one
  render tick; `Headless.All<T>` walks the visual tree; `Headless.DistinctColours(window)` captures
  the rendered frame and counts sampled colours (a blank render is one colour).

## What is covered

View models (star map, inspector orders/cargo/merge/minefield/packets, production, research,
messages filter, battle plans, race designer, new game, technology browser, relations, score,
help) and views (the docked main screen and its menu bar/accelerators, the star map, inspector,
messages, battle plans, production, research, technology browser, race designer, new game).
The rows each test targets are named in its doc comment (behavior-specs-10 coverage tables
client-interface, client-ui-dialog-catalog and race-designer-ui-and-availability).

## Rules for new tests

- Do not pin anything listed in `docs/behavior-specs-10-questions.md`. When a value is a stand-in,
  read it through its seam (`MapViewOptions.ModeOverlays`, `NewGameSetup.SimplifiedPlayerCountRanges`,
  ...) or assert only what holds under every reading (for example "a neighbouring waypoint stays
  selected", not which one).
- Prefer view-model assertions; use a rendered view when the thing under test is the binding or the
  control itself.
- Never write outside `TestGame.ScratchRoot` or the test output folder.
