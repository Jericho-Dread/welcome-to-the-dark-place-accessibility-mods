# Welcome To The Dark Place Accessibility Mods

This repository contains releases and source code for two mods for `Welcome To The Dark Place`:

- `Dark-Place_Access`
- `Dark-Place_Journal`

These mods are meant to help blind and low-vision players access the game with NVDA speech and spoken menu feedback.

## Downloads

You can grab the built files from the GitHub release page or from the [`releases`](./releases) folder in this repo.

Included downloads:

- `Dark-Place_Access.Release.zip`
- `Dark-Place_Journal.Release.zip`
- `Dark-Place_Source.zip`

## What The Mods Do

### Accessibility mod

`Dark-Place_Access` adds spoken access to the main game flow, including:

- story text
- choices
- menu focus
- pockets and inventory review
- map open and close announcements
- tape recorder open and close announcements
- controls help
- number-key choice activation

### Journal mod

`Dark-Place_Journal` adds a spoken in-game journal that lets players:

- create pages
- read pages
- edit pages
- rename pages
- delete pages
- paste clipboard text
- paste the current scene text into an entry

## What You Need

- `Welcome To The Dark Place`
- `BepInEx 5.4.23.5`
- `NVDA` running if you want speech output

The accessibility package includes:

- `Dark-Place_Access.dll`
- `nvdaControllerClient32.dll`
- `nvdaControllerClient64.dll`

The journal package includes:

- `Dark-Place_Journal.dll`

## Installation

### Accessibility mod

1. Install `BepInEx 5.4.23.5`.
2. Copy the `BepInEx` folder from `Dark-Place_Access.Release.zip` into the game folder.
3. Copy `nvdaControllerClient32.dll` and `nvdaControllerClient64.dll` into the game folder beside the game EXE.
4. Launch the game with NVDA running.

### Journal mod

1. Make sure BepInEx is already installed.
2. Copy the `BepInEx` folder from `Dark-Place_Journal.Release.zip` into the game folder.
3. Allow the `plugins` folder to merge.

## Accessibility Mod Hotkeys

- `F5`: read the controls help
- `F6`: reread the current room text and choices
- `F8`: toggle automatic choice readback
- `1` through `4`: activate visible choices from top to bottom

## Journal Mod Hotkeys

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

The source code for these mods is in the [`source`](./source) folder:

- [`source/Dark-Place_Access`](./source/Dark-Place_Access)
- [`source/Dark-Place_Journal`](./source/Dark-Place_Journal)

## Notes

- This repository does not include `UndergoingAccess`.
- This repository does not include the full private/local BepInEx-bundled package.
