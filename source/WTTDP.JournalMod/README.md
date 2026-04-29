# WTTDP Journal Mod

This is a separate BepInEx plugin for `Welcome To The Dark Place`.

It adds a spoken in-game journal that is intentionally separate from the accessibility mod.

## Current controls

- `F9`: open or close the journal
- `Up` / `Down` or `W` / `S`: move between journal items
- `Enter`: open the selected page, create a page, or activate `Edit` / `Back`
- `Escape`: close the journal, go back, or save while editing

## Journal flow

- When opened, the journal reads the current selection.
- Existing pages can be opened and read.
- `Create new page` starts a title entry step, then an edit step for the page body.
- While editing, typed text is echoed through NVDA.

Pages are saved to:

`BepInEx\config\com.wttdp.journal.pages.json`
