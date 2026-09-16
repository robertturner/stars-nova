# Stars! Nova (Avalonia)

A modernized, cross-platform continuation of **[Stars! Nova](https://github.com/ekolis/stars-nova)**,
an open-source clone of the 1995-2000 4X space strategy game *Stars!* — now with a native
[Avalonia](https://avaloniaui.net/) UI running on both Windows desktop and Android, alongside a
growing set of independently-authored, clean-room behavior specifications documenting the
original game's mechanics.

## 1. Derived from Stars! Nova

This repository is a fork of Stars! Nova (GPLv2), forked in whole with its original git history
intact — `Common/`, `Nova/`, `ServerState/`, `ControlLibrary/`, `Tests/`, and most of the repo
root are that project's own source tree, not rewritten from scratch. `Common/` is the shared
domain model (game objects, production, research, race definitions); `Nova/` is the original
WinForms client, AI, and server-hosting executable; `ServerState/` is turn-processing and
persistence logic. The goal here is to fix and complete Stars! Nova against this project's own
behavior specs (see below) and, where it makes sense, contribute improvements back upstream.

## 2. Clean-room behavior specifications

`docs/behavior-specs*/` contains multiple rounds of documentation describing the *original*
closed-source *Stars!* client's confirmed behavior — habitability and population growth,
production queues, research, combat resolution, fleet movement/fuel/scanning, race traits, ship
design, and UI-level behavior (map overlays, dialogs, indicators) among others.

**Ground rule:** this documentation, and any implementation written from it, is produced **only**
from externally observable behavior — the original manual, community FAQs and wikis, and direct
play-testing/inspection of the running original game (including its exported/decompiled binary,
used strictly to confirm *observable* behavior such as dialog layouts, exact option labels, and
UI-visible mechanics — never to copy code, structure, or implementation). It is never written by
reading, copying, or adapting Stars! Nova's own source for the same subsystem it documents. This
separation is what keeps the specs (and the implementation work done from them) an independent,
defensible reference rather than a derivative of either the original game or of Nova's own code.

## 3. Native Avalonia ports (PC & Android)

The original WinForms client is being replaced with a shared Avalonia UI (`Nova.Avalonia/`) hosted
by two thin platform heads:

- **`Nova.Avalonia.Desktop/`** (.NET 9, Windows) — a docked, multi-panel layout (via AvaloniaDock)
  mirroring the original's Navigator/Inspector/Production/Research/Ship Design arrangement, built
  for mouse-and-keyboard use on a full-size screen.
- **`Nova.Avalonia.Android/`** (.NET 10, Android) — a single-view, touch-first layout: one
  screen at a time (Map, Navigator, Research, Ship Design, ...) reached from a burger menu, with
  tabs instead of docked panels, on-screen gestures for map panning/zooming/waypoint plotting, and
  its own asset-bundling/back-button/lifecycle handling for the Android app model.

Both heads share the same `Nova.Client`/`Common`/`ServerState` game logic underneath — only the
presentation layer differs per platform.

## License

Stars! Nova (and this fork) is dual-licensed: code under **GPLv2**, content (images, docs, and
other media) under **CC BY-SA 3.0**. See [`LICENSE.txt`](LICENSE.txt) for full terms.

## More detail

`docs/PROJECT-STATUS.md` is a running, dated log of what's been fixed, built, and verified in this
fork so far — the best place to check current status or history for any specific feature.
