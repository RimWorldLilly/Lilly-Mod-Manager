# Lilly Integrated Mod Manager

RimWorld 1.7 mod replacing the vanilla Mods menu page with a category- and tag-based manager.

## Current features
- Four categories: Pre/Core/Normal/After-load.
- Category activation state and per-mod activation state.
- Ctrl-click, Shift-click, and Ctrl+A multi-selection.
- Move selected mods between categories.
- Category-specific folders with create, rename, collapse, move, and delete operations.
- Drag-and-drop category/folder moves, folder ordering, and multi-mod reordering.
- User-defined tags that can be assigned to multiple mods, with tag-specific folders.
- Toggle activation for all eligible mods in a tag, and add or remove tags from selected mods.
- Aggregate active, inactive, and mixed state controls for tags and folders.
- Save, load, and delete named presets containing categories, folders, tags, and activation choices.
- Search by mod name, display name, PackageId, author, description, version, tags, and category.
- Three-column quick folder mover with paging, per-column scrolling, search, favorites, and recent targets.
- Steam Workshop unsubscribe requests, separate management-list removal, and confirmed local mod-folder deletion.
- Protected Core and official DLC activation.
- Apply changes to the vanilla mod configuration; cancel unapplied changes.
- Persistent category/tag/folder assignments, collapsed state, and activation choices.
- Tag definitions, tag folders, and tag assignments are saved immediately and remain when unapplied manager changes are canceled.
- Favorite and recent quick-move folders persist in ModSettings.

Newly discovered mods are added to the Normal category. Core and official DLC are protected from moving or disabling. Changes apply on the next game restart.

Loading a preset replaces the current edit draft; it does not change the active mod configuration until Apply is selected. Installed mods missing from an older preset are retained in a default category.

Automatic sorting uses available mod dependency metadata while preserving the existing order where dependencies do not constrain it. Apply checks the resulting RimWorld load order and asks before applying when it finds ordering problems. Core and official DLC remain protected. Removing a mod from the manager is separate from deleting its files; Workshop unsubscribe requests do not erase its saved management data.

## Standalone Windows app

Running `build.bat` on Windows with the .NET 10 SDK now builds both the RimWorld mod and a self-contained x64 desktop application. The executable is written beside `build.bat` as `Lilly.ModManager.exe`; its Windows icon is embedded during publishing.

The desktop app uses a game-style three-pane layout: category/tag navigation on the left, a folder-aware mod list in the center, and the selected mod's information and local preview image on the right. The visible list is not a spreadsheet/table. It supports category and tag views, active/mixed activation controls, collapsible folders, Ctrl/Shift multi-selection, drag-and-drop assignment to categories/tags/folders, and drag-and-drop load-order changes. It can scan local, official, and Steam Workshop mod folders; filter by source and activation state; search installed-mod metadata; enable or disable one or multiple mods; change load order manually or sort by declared dependencies; inspect missing requirements and load-order problems before applying; assign categories, tags, and folders; edit category, tag, and mod colors; customize displayed mod names; save, load, and delete manager presets; discard unapplied edits by reloading saved settings; remove inactive mods from the management list without deleting files; delete confirmed local mod folders; open mod folders and Workshop pages; and launch RimWorld. Missing active mod entries are retained rather than silently dropped. It reads and writes the same RimWorld `ModsConfig.xml`, the in-game manager's `ModSettings` data, and the manager preset XML files. Existing configuration files are backed up before writing. Close RimWorld before applying a desktop configuration. Steam subscription/unsubscription and Workshop publishing are intentionally not included in the standalone app.

The existing in-game manager remains in place. On startup, it creates a desktop shortcut when `Lilly.ModManager.exe` is present beside the mod's `build.bat`. A **Create desktop shortcut** button is also available in the in-game manager options window.

## Build

Run `build.bat` on Windows with the .NET SDK 10 installed. Since `LoadFolders.xml` loads the mod root for RimWorld 1.7, the output is written to `Assemblies\Lilly.IntegratedModManager.dll`.
