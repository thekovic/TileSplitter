# Implementation progress

The approved design is PLAN.md. Source atlas pack1.jpg must remain unchanged.

1. Foundation — complete: domain geometry, owner map, edits and snapshot undo/save-point history. Seven tests pass.
2. Image viewport — implemented: full-resolution frozen decoder, raw-pixel rendering, inverse-transform hit testing, grid, pan and cursor-anchored zoom. Two integration tests pass, including supplied 2048×2048 atlas and file handle release. Interactive visual acceptance pending.
3. Texture editor — implemented: sidebar, search, names, colors, stable IDs, previews, overlays, coverage, grid reset and undo/redo. Builds successfully; visual/gesture acceptance pending.
4. Project lifecycle — implemented: versioned JSON, validated loading, source resolution/hash checks, atomic saving and unsaved prompts. Three integration tests pass (roundtrip/source checks, malformed/future/invalid geometry, failed atomic write preservation).
5. Export — implemented: settings/preview, Windows name validation, preflight/collisions, exact crops, alpha flattening, background encoding/cancellation and atomic per-file output. Tests pass for actual codecs, lossless crop pixels, JPEG tolerance, alpha, collisions/races, cancellation and partial failure cleanup.
6. Polish and distribution — implementation complete: keyboard shortcuts, command enablement, tooltips/accessibility labels, loading/error states, README workflow and publish instructions. STA window/viewport construction/rendering, bound sidebar selection, and automated supplied-atlas end-to-end workflow pass. Release build and self-contained win-x64 publish succeed. Manual acceptance below remains pending.

## Final verification

- `dotnet test -c Release --blame-hang-timeout 30s`: **49 passed** (29 Core, 20 App), zero failures or skips.
- `dotnet build -c Release --no-restore -warnaserror`: success, zero warnings/errors.
- `dotnet publish src/TileSplitter.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64`: success. Output includes executable, coreclr.dll, and WPF assemblies.
- `dotnet format whitespace TileSplitter.slnx --no-restore`: applied standard SDK formatting.
- Supplied atlas is decoded read-only as 2048×2048; automated end-to-end assignments include 1×1, 1×2, 2×2 and larger rectangles, project roundtrip, and all export formats. `pack1.jpg` was never written or replaced.
- Additional failure coverage includes all erasure edges, stable IDs/history branching, allocation failure preserving the current document, changed-source dirty state, locked/read-only export preflight, and collision races.

## Decisions and remaining acceptance

No architecture or scope deviations from PLAN.md. Undo uses document snapshots; immutable texture records and preallocated derived ownership maps keep failed edits atomic. Source relinking or acknowledging changed content starts dirty without adding a fabricated undo action. Name edits still focused during shortcuts/save/close are committed before acting. Export paths use a conservative 260-character limit.

The file-handle release test uses a private copy to avoid competing with parallel read-only fixture tests. Scaffolding/restore required approved SDK initialization access.

Pending manual checks from PLAN.md: real pointer gestures, keyboard/focus workflow, label appearance at multiple zoom/display scaling settings, interactive missing/changed-source and overwrite prompts, and published open/assign/save/export on Windows 11 x64 without a separately installed .NET runtime. Automated STA rendering and file/codec checks cover components but do not replace these interactive/clean-machine checks. No clean-machine launch has been claimed.
