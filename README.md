# Dark-Place Accessibility Mods

This repository contains source code and a ready-to-install release package for:

- `Dark-Place_Access`
- `Dark-Place_Journal`

These mods are for `Welcome To The Dark Place` and are meant to help blind and low-vision players use the game with NVDA speech and spoken menu feedback.

## Download

The main download is:

- `Dark-Place_Access_and_Journal.zip`

You can find it on the GitHub release page and in the [`releases`](./releases) folder in this repo.

## What The Mods Do

### Dark-Place_Access

This mod adds spoken access to the main game flow, including:

- story text
- choices
- menu focus
- pockets and inventory review
- map open and close announcements
- tape recorder open and close announcements
- controls help
- number-key choice activation

### Dark-Place_Journal

This mod adds a spoken in-game journal that lets players:

- create pages
- read pages
- edit pages
- rename pages
- delete pages
- paste clipboard text
- paste the current scene text into an entry

## Included In The Download

The combined package includes:

- `BepInEx 5.4.23.5`
- `Dark-Place_Access.dll`
- `Dark-Place_Journal.dll`
- `nvdaControllerClient32.dll`
- `nvdaControllerClient64.dll`

## Installation

1. Copy everything from `Dark-Place_Access_and_Journal.zip` into your `Welcome To The Dark Place` game folder.
2. Allow files to merge or overwrite if prompted.
3. Launch the game with NVDA running.

If you previously installed older versions manually, remove duplicate old plugin DLLs from `BepInEx\plugins`.

## Main Hotkeys

### Dark-Place_Access

- `F5`: read the controls help
- `F6`: reread the current room text and choices
- `F8`: toggle automatic choice readback
- `1` through `4`: activate visible choices from top to bottom

### Dark-Place_Journal

- `F7`: open or close the journal
- `F9`: paste the current scene text while editing
- `F10`: repeat the current journal state
- `Up` / `Down` or `W` / `S`: move through journal items
- `Enter`: activate the selected item
- `Escape`: back out, close the journal, or save while editing
- `Ctrl+V`: paste clipboard text while editing

## Basic Game Controls

- `Up` / `Down` or `W` / `S`: move through choices
- `Enter`: select a choice
- `C`: open the map
- `X`: open or close pockets
- hold `Escape`: return to the main menu

## Source Code

Source code is in the [`source`](./source) folder:

- [`source/Dark-Place_Access`](./source/Dark-Place_Access)
- [`source/Dark-Place_Journal`](./source/Dark-Place_Journal)

## Notes

- `UndergoingAccess` is not included in this repository.
