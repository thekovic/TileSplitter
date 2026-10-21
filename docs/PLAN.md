# TileSplitter design and implementation plan

Status: design only; implementation has not started.

## Purpose and confirmed requirements

Build a C# desktop tool primarily for Windows that opens a texture atlas, lets the user visually assign grid tiles to named textures, and exports each texture as a separate image.

- The initial use case is a 2048×2048 atlas with 64×64 tiles: 32 columns, 32 rows, 1,024 tiles.
- Every texture must occupy one solid, axis-aligned rectangle of adjacent tiles. Dimensions can be any positive multiple of the tile dimensions, including 64×128 and 128×128.
- The user creates or selects a texture in a sidebar, then clicks or drags on the atlas to assign tiles.
- Each texture has a thin colored perimeter, an ID at its top-left corner, and its editable name at its bottom-right corner.
- Projects can be saved and reopened. Unassigned tiles are permitted and are omitted from export.
- Both input and output support PNG, JPG/JPEG, and BMP.
- Output goes to a user-selected directory, with one file per assigned texture using its name.

The repository currently contains only `pack1.jpg`; preserve it as user-provided input. No existing application architecture needs to be retained.

## Technology decisions

Use WPF with C# on .NET 10 LTS, targeting `net10.0-windows`. WPF supplies desktop dialogs, data binding, zoomable drawing, and native image decoding/encoding. This keeps the first release focused on Windows without introducing a cross-platform UI or imaging dependency. Use an SDK-style solution and a current .NET 10 SDK; pin the selected SDK during implementation with an appropriate patch roll-forward policy.

Use built-in WPF imaging (`BitmapDecoder`, `BitmapSource`, `CroppedBitmap`, and PNG/JPEG/BMP encoders). Decode the source at full resolution, load it completely, close the file stream, and freeze image sources when crossing thread boundaries. Do not resize the source to match the viewport. Use nearest-neighbor display scaling for inspecting pixel textures.

Use a small MVVM structure, with plain domain objects and explicit commands. Keep image codecs and file dialogs outside the domain layer. Avoid a dependency injection framework and a large UI toolkit for this scope.

Initial release target: supported Windows 11 x64 configurations. Publish a self-contained folder distribution so users do not need to install .NET separately. Other architectures and platforms are later work.

References informing these choices:

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 is an LTS release.
- [WPF project](https://github.com/dotnet/wpf): Windows desktop framework with C# and XAML.
- [WPF imaging overview](https://learn.microsoft.com/en-us/dotnet/framework/wpf/graphics-multimedia/imaging-overview): supported codecs and bitmap transformations.

## Atlas and geometry rules

Default tile width and height to 64 pixels; allow each to be configured when creating a project. Accept other atlas dimensions as long as both dimensions are exact multiples of the tile dimensions. Reject partial edge tiles with a clear message; do not silently crop or pad them. Grid origin is the top-left pixel, with no spacing or margins in the first release.

Store committed texture regions as integer tile rectangles `(column, row, width, height)`, using zero-based coordinates and positive sizes. Pixel export bounds are `(column × tileWidth, row × tileHeight, width × tileWidth, height × tileHeight)`. Rectangles may touch but must not overlap. A tile belongs to at most one texture.

Changing grid dimensions after assigning textures requires clearing the assignments through an explicit, undoable operation. Opening another atlas creates a different project; do not reinterpret existing rectangles against an unrelated source.

Treat the decoded pixel matrix as authoritative. Do not automatically apply EXIF rotation or DPI-based resizing.

## Window and interaction design

Main window:

- Toolbar/menu: New/Open Atlas, Open Project, Save/Save As, Undo/Redo, Export, grid and label visibility, Fit, and 100% zoom.
- Center: atlas viewport with a subtle tile grid, texture perimeters, labels, hover tile, and selection preview.
- Sidebar: New Texture, searchable texture list, color swatches, stable IDs, names, dimensions, selected texture properties, and Delete Texture.
- Status bar: zoom, hovered tile coordinates, assigned/total tile count, texture count, and validation feedback.

Use mouse wheel zoom anchored at the cursor, right mouse button to pan, and Fit to show the entire atlas. Convert pointer coordinates through the inverse viewport transform before calculating tile coordinates; avoid DPI-dependent hit testing.

Selecting a texture in the sidebar makes it the assignment target and highlights its border. Each newly created texture receives a monotonically increasing ID, a deterministic distinct color, and an editable default name such as `texture_001`. IDs survive renaming, deletion of other textures, and project reloads; never renumber existing textures.

Assignment behavior:

1. For an empty texture, clicking assigns one tile. Dragging previews and then assigns the inclusive tile rectangle between the press tile and current tile.
2. For an existing texture, a normal click/drag proposes adding the selected tiles. Commit only if the union is exactly one solid rectangle and has no overlap with other textures.
3. Provide an explicit **Replace region** mode to redraw an existing texture's complete rectangle. This makes moving or resizing a texture straightforward without a sequence of additions.
4. Provide an **Erase** mode to remove clicked/dragged tiles from the active texture. Accept only when the remainder is a solid rectangle or empty. Reject holes or disconnected remnants.
5. Show valid previews in the texture color and invalid previews in red with the reason. Reject invalid edits atomically on release; leave existing assignments intact. Never silently fill gaps or steal another texture's tiles.
6. Escape cancels a gesture. Capture the pointer during dragging; clamp drags to the atlas once started inside it. Ignore presses outside the atlas.

Selecting another texture is done through the sidebar, avoiding ambiguity between selection and assignment clicks. Deleting a texture releases its tiles. Clearing its region retains its ID and name as an empty draft. Empty drafts are saved, omitted from export, and reported in the export summary.

Each completed assignment, erase, replacement, rename, color change, creation, deletion, or grid reset is one undoable action. Pan, zoom, hover, and sidebar selection do not enter undo history. Undo/redo updates the unsaved state relative to the last successful save.

Each action that's not tile selection/deselection, such as mode changes, saving, undo/redo, etc., should have a keyboard shortcut to facilitate fast workflow for users.

Render one outline around the complete rectangle, with a roughly 1–2 device-independent-pixel screen-space stroke. Use a stronger outline for the active texture. Draw ID and name with contrasting backplates. Keep labels anchored inside their respective corners; truncate long names visually and reveal the full name on hover and in the sidebar. At low zoom, suppress labels that cannot fit without collisions while retaining borders; provide a label toggle. Labels, grid, and borders are editor overlays only and never appear in exports.

## Project format and persistence

Use versioned UTF-8 JSON with extension `.tilesplitter.json`. Persist:

- Schema version and source atlas path relative to the project when possible, with an absolute fallback.
- Source dimensions and SHA-256 file fingerprint to detect changed input.
- Tile width/height, next texture ID, and textures containing ID, name, display color, and nullable tile rectangle.
- Export settings: output format, directory, JPEG quality, and background color for formats without supported alpha.
- Optional viewport and selected texture state for convenience.

Do not embed the atlas or undo history in the initial project format. Projects therefore need the referenced atlas; make this clear when saving. On reopen, resolve the relative path first. If missing, offer Locate Atlas. Verify dimensions and fingerprint before accepting it. Refuse incompatible dimensions; require explicit acknowledgment for same-size changed content, since tile assignments may now describe different artwork.

Validate the complete loaded project before replacing the current document: schema, positive tile sizes, dimensions, unique IDs, rectangle bounds, non-overlap, and export settings. Unknown future schema versions produce a helpful error. Treat persisted paths and names as untrusted input.

Save through a temporary sibling file followed by replacement/rename to avoid leaving truncated project files. Preserve the prior save on failure. Prompt to save unsaved work before closing or replacing the project; cancellation leaves the current document open.

## Export behavior

Provide an export dialog with directory selection, one format for the batch (PNG default), JPEG quality (default 95), and an opaque background color (default white) for alpha flattening. Let the user see the resulting file list, dimensions, unassigned tile count, and empty drafts before starting.

Validate every nonempty texture name as a Windows filename stem. Reject blank names, invalid characters, directory separators, traversal components, reserved device names, trailing periods/spaces, excessive resulting path lengths, and duplicate destination names using case-insensitive comparison. Show errors beside the affected textures. Do not silently sanitize names or append IDs. Derive the file extension from the selected encoder: `.png`, `.jpg`, or `.bmp`.

Preflight all destination paths, collisions, and directory creation/access errors before encoding. Never allow an export destination to overwrite the source atlas or project. If files exist, offer a single batch choice: overwrite the listed files, skip existing files, or cancel. No overwrite occurs without that choice. Directory permission errors still need handling during writes even after preflight.

For each texture, crop directly from the full-resolution decoded source using integer pixel bounds. Do not render the editor viewport or resample pixels. PNG preserves decoded alpha. JPEG flattens alpha onto the chosen background and is lossy. For predictable BMP compatibility, output opaque 24-bit BMP and flatten alpha using the same background. Explain these format effects in the export dialog. Reading JPG is supported, but exporting as PNG cannot recover detail already lost in the input.

Encode sequentially on a background task using an immutable export snapshot, keeping the UI responsive and peak memory bounded. Disable document mutations during the batch. Show progress and support cancellation between textures. Write each image to a temporary file in the destination directory, close it, then move/replace its final destination; clean up temporary files on cancellation or error. Check collision policy again at finalization to account for concurrent filesystem changes.

Export is atomic per file, not for the entire batch. Completed files remain if a later texture fails or the batch is cancelled. Stop on the first unexpected write/encode failure and report completed, skipped, failed, and remaining textures, with paths and actionable errors. A later export can retry using the same collision handling.

## Application structure

Proposed solution layout:

```text
TileSplitter.slnx
src/
  TileSplitter.Core/             # Rectangles, document model, validation, edit commands
  TileSplitter.App/              # WPF views, view models, viewport, imaging, persistence
tests/
  TileSplitter.Core.Tests/       # Geometry, assignment, undo, name validation
  TileSplitter.App.Tests/        # Windows imaging and persistence integration checks
```

Core targets `net10.0` and has no WPF references. App and its integration tests target Windows. Use xUnit for focused automated tests. Introduce interfaces at image loading/export and persistence boundaries where they enable meaningful tests; avoid a generic repository abstraction.

Maintain a derived tile-owner map for fast conflict checks and coverage counts, rebuilding it from committed rectangles after load or edits. Persist rectangles only so there is one authoritative geometry representation. For 1,024 tiles, straightforward scans are sufficient; do not add spatial indexing.

Draw the atlas and overlays through a custom WPF drawing element rather than creating a control for every tile. Cache the decoded atlas; redraw overlays as needed. Keep geometry calculations and validation outside rendering code. Handle capture loss and cancelled gestures without changing the document.

## Implementation sequence

1. **Foundation:** Create solution/projects, domain models, rectangle validation, ownership map, and edit commands. Cover valid growth, rejected holes/overlaps, replacement, erasure, and undo behavior with focused tests.
2. **Image viewport:** Implement atlas loading, tile-size validation, drawing, grid, pan/zoom, coordinate conversion, and accurate hit testing. Verify the supplied atlas manually without modifying it.
3. **Texture editor:** Add sidebar, names/colors/IDs, rectangle gestures, preview feedback, overlays, coverage status, and undo/redo. Deliver a complete in-memory assignment workflow.
4. **Project lifecycle:** Implement schema, save/load, source resolution and fingerprint checks, dirty-state handling, and input validation. Test round trips and failed saves/loads.
5. **Export:** Implement format settings, filename validation, preflight, collision choices, exact crops, alpha handling, background execution, cancellation, and per-file finalization. Test actual image output with synthetic fixtures.
6. **Polish and distribution:** Add keyboard shortcuts, loading/error states, tooltips, accessibility labels, README workflow instructions, and self-contained Windows x64 publish instructions. Run the end-to-end acceptance pass.

Each milestone should leave the application usable for its completed workflow. Do not implement later features in advance of the core assignment/export path.

## Validation and acceptance criteria

Automated checks should focus on behavior with meaningful failure modes:

- Pixel-to-tile mapping at edges and transformed coordinates; inclusive drags in every direction; out-of-bounds handling.
- Single-tile and large rectangles, valid unions, rejected L-shapes, overlaps, hole-producing erasures, and replacement conflicts; rejected edits leave state unchanged.
- Stable IDs, coverage counts, undo/redo of destructive edits, and save-point dirty tracking.
- Project round trips, malformed/future schema rejection, duplicate IDs, invalid rectangles, missing/changed atlas handling, and preservation of existing data after failed saves.
- Filename edge cases, case-insensitive duplicate paths, reserved names, source/project collisions, and overwrite/skip decisions.
- Synthetic PNG/JPEG/BMP input and output cases. Verify decoded dimensions for all formats; verify exact pixel crops for lossless fixtures and appropriate tolerance for JPEG. Include alpha flattening and cancellation/failure cleanup.

Manual Windows acceptance pass:

1. Open `pack1.jpg` with a 64×64 grid; verify source dimensions and calculated rows/columns, with 32×32 expected for a 2048×2048 source.
2. Assign and name 1×1, 1×2, 2×2, and larger regions through the sidebar workflow. Check perimeter, ID, name placement, and previews at multiple zoom levels and Windows display scaling settings.
3. Exercise overlapping assignments, irregular unions, erasure, replacement, undo/redo, long names, empty drafts, and unassigned areas.
4. Save, close, reopen, and confirm geometry, IDs, names, source resolution, and export settings survive.
5. Export to each supported format with unassigned tiles remaining. Verify filenames, dimensions, crop contents, and absence of editor overlays.
6. Exercise destination collisions, missing sources, changed sources, unwritable directories, cancellation, and partial export failure. Verify messages explain the outcome and existing files remain intact where replacement did not complete.
7. Launch the published build on a Windows machine without a separately installed .NET runtime and complete the open/assign/save/export workflow.

## Scope boundaries and future options

First release handles one atlas per project, a regular grid without gutters, rectangular nonoverlapping textures, and one output format per batch. Automatic texture detection, arbitrary polygons/disconnected groups, rotated textures, stitching/repacking, multi-atlas projects, per-texture formats, and cross-platform UI are deferred. No artificial 2048×2048 size limit is imposed, but image allocation failures and unsupported images must produce clear errors.

The workflow questions are resolved. The remaining defaults above are concrete implementation choices that can be adjusted during plan review without starting implementation.
