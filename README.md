# TileSplitter

A Windows 11 x64 WPF tool for assigning rectangular regions of a texture atlas and exporting individual texture images. Requires the .NET 10 SDK for development; the self-contained distribution includes its runtime.

The main window remembers its size, position, and maximized state between sessions. App preferences are stored per user in `%LOCALAPPDATA%\TileSplitter\settings.json`. If a display is disconnected, the window is moved into an available display's working area. Missing or damaged settings fall back to defaults.

## Build and run

```powershell
dotnet build TileSplitter.slnx
dotnet test TileSplitter.slnx
dotnet run --project src/TileSplitter.App
```

The SDK is pinned in `global.json` with patch roll-forward. Core has no WPF dependency; Windows integration tests use WPF's actual image codecs.

## Workflow

1. Choose **New / Atlas** and open PNG, JPG/JPEG, or BMP. Enter tile width and height (64,64 by default). Dimensions must divide the decoded image exactly. `pack1.jpg` is 2048×2048, producing a 32×32 grid.
2. Create a texture in the sidebar. Give it a name, select it, then click or drag across the atlas. All assigned regions must be solid rectangles and cannot overlap. Green/texture-colored previews are valid; red previews explain a rejected change in the status bar.
3. Use **Add tiles** to grow a rectangle, **Replace region** to move/resize it, or **Erase tiles** to remove an edge. Erasures leaving a hole or disconnected region are rejected. Clear region keeps the texture as an empty draft; Delete releases the region and removes the texture. Each edit can be undone.
4. Scroll to zoom around the cursor; hold the right mouse button to pan. Fit, 100%, grid, and labels control the view. Escape cancels a drag. Source coordinates always refer to raw decoded pixels, without EXIF rotation or DPI resizing.
5. Save a `.tilesplitter.json` project. **Keep the atlas with it:** projects reference the source rather than embedding it. Relative paths support moving the project and atlas together. Missing sources can be located; same-size changed content requires acknowledgment. Other dimension changes are rejected.
6. Export: choose a directory, batch format, JPEG quality, and background color. Review the filenames, dimensions, unassigned tile count, and empty drafts. Fix errors beside the affected entries. Existing files require an explicit overwrite/skip/cancel choice. Names are never silently changed.

PNG retains decoded transparency. JPEG is lossy; JPEG and opaque 24-bit BMP flatten transparency onto the selected background. Exporting JPG input to PNG cannot restore lost detail. Editor overlays never enter exports.

Export runs in the background and disables document edits. Cancellation occurs between textures. Each file is finalized independently: completed files remain after cancellation or a later failure. The report lists completed, skipped, failed, and remaining paths. Fix the reported issue and export again to retry.

## Keyboard shortcuts

| Action | Shortcut |
|---|---|
| New atlas / open project | Ctrl+N / Ctrl+O |
| Save / Save As | Ctrl+S / Ctrl+Shift+S |
| Undo / redo | Ctrl+Z / Ctrl+Y |
| Export | Ctrl+E |
| New texture / search | Ctrl+T or F1 / Ctrl+F |
| Rename / apply name | F2 / Enter |
| Change color | Ctrl+Shift+C |
| Clear region / delete texture | Ctrl+Backspace / Ctrl+Delete |
| Add / replace / erase mode | Ctrl+1 / Ctrl+2 / Ctrl+3 |
| Fit / 100% | Ctrl+, / Ctrl+. |
| Toggle grid / labels | Ctrl+G / Ctrl+L |
| Reset tile dimensions (clears regions, undoable) | Ctrl+Shift+G |
| Cancel gesture / cancel export between textures | Escape |

Dialog buttons support Tab, Enter, Escape, and labeled Alt access keys where shown. Tooltips expose the editing shortcuts and full texture names.

## Self-contained Windows x64 distribution

```powershell
dotnet publish src/TileSplitter.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/win-x64-single-file
```

Distribute only `artifacts/win-x64-single-file/TileSplitter.App.exe`. Launch it on supported Windows 11 x64; a separate .NET install is unnecessary. Native runtime libraries are bundled into the executable and extracted automatically at launch. Debug symbols are omitted to keep the distribution to one file. Do not enable trimming for this WPF application. Atlas files are user inputs and are not bundled automatically.

## Verification and limitations

See `IMPLEMENTATION.md` for the implementation and verification record and `PLAN.md` for acceptance criteria. Automated checks include domain edits/history, hit testing, WPF construction/rendering, project validation and atomic writes, real PNG/JPEG/BMP output, lossless pixels, alpha flattening, collision races, cancellation, partial failure, and a supplied-atlas save/reload/export workflow.

Interactive mouse/keyboard behavior at multiple Windows display scaling settings and launch on a machine without .NET require a manual acceptance pass. The first release handles one atlas, no gutters, nonoverlapping rectangles, and one batch format. Conservative export path limits keep resulting paths below 260 characters. There is no automatic detection, repacking, or cross-platform UI.
