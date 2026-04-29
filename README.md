# Welcome To The Dark Place Accessibility Mods

This repository contains releases and source code for two mods for `Welcome To The Dark Place`:

- `WTTDP.AccessibilityMod`
- `WTTDP.JournalMod`

These mods are meant to help blind and low-vision players access the game with NVDA speech and spoken menu feedback.

## Downloads

You can grab the built files from the GitHub release page or from the [`releases`](./releases) folder in this repo.

Included downloads:

- `WTTDP.AccessibilityMod.Release.zip`
- `WTTDP.JournalMod.Release.zip`
- `WTTDP.Public.Source.zip`

## What The Mods Do

### Accessibility mod

`WTTDP.AccessibilityMod` adds spoken access to the main game flow, including:

- story text
- choices
- menu focus
- pockets and inventory review
- map open and close announcements
- tape recorder open and close announcements
- controls help
- number-key choice activation

### Journal mod

`WTTDP.JournalMod` adds a spoken in-game journal that lets players:

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

- `WTTDP.AccessibilityMod.dll`
- `nvdaControllerClient32.dll`
- `nvdaControllerClient64.dll`

The journal package includes:

- `WTTDP.JournalMod.dll`

## Installation

### Accessibility mod

1. Install `BepInEx 5.4.23.5`.
2. Copy the `BepInEx` folder from `WTTDP.AccessibilityMod.Release.zip` into the game folder.
3. Copy `nvdaControllerClient32.dll` and `nvdaControllerClient64.dll` into the game folder beside the game EXE.
4. Launch the game with NVDA running.

### Journal mod

1. Make sure BepInEx is already installed.
2. Copy the `BepInEx` folder from `WTTDP.JournalMod.Release.zip` into the game folder.
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

The source code for these mods is in the [`source`](./source) folder.

## Notes

- This repository does not include `UndergoingAccess`.
- This repository does not include the full private/local BepInEx-bundled package.
