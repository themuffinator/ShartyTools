# Architecture decision: a small native desktop toolkit

Status: accepted for the first milestone, 3 October 2026.

## Stack

Use **C# / .NET 10** and **Avalonia 12.1.3**. .NET 10 is the recommended target in [Avalonia's version 12 guidance](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes). Binary spans, explicit little-endian reads, ZIP streaming and JSON nodes are all available in the standard library. Avalonia provides desktop controls, native file dialogs and a testable cross-platform visual tree. Its core is MIT licensed, compatible with the project's GPL v2 distribution.

This is primarily a filesystem and binary-format tool. An Electron/web stack would add a browser and a separate native bridge. Rust with a webview would need a second UI language; C++/Qt would add build/deployment complexity and a larger memory-safety burden for parser work. Python/Qt would make prototypes quick, but distributing a stable desktop runtime and enforcing typed format boundaries is less direct. None of those alternatives is ruled out for future editor plugins; the independent file formats are the integration boundary.

Windows x64 is the primary local platform. Linux x64 has the same build/test/release pipeline. macOS and ARM packaging are not claimed in this milestone. Portable releases are self-contained and untrimmed; the first version avoids AOT/trimming surprises and does not require an installer.

## Boundaries

- **Core** owns path validation, file readers/writers, project persistence, mapdb preservation, asset catalogs, checks and packaging. It has no UI or third-party runtime dependency.
- **Desktop** owns the workspace, bindings, file dialogs, unsaved-change prompts and background operations. The UI takes a snapshot before scans/builds and disables conflicting controls while work runs.
- **CLI** calls the same core and exposes exit codes, JSON QA output and strict mode.
- **Tests** construct small synthetic binary fixtures, check adverse inputs, exercise workflows and render the actual UI through Avalonia's headless render target. No OS input control or game launch is required.

There is deliberately no general plugin framework, service container, database, telemetry or background updater yet. Small explicit modules are sufficient for two tools. The raw mapdb view is an escape hatch for mod-specific fields without imposing an invented schema.

## Persistence and writes

`*.sharty.json` is UTF-8 JSON with `schemaVersion: 2`. Product SemVer and document schema are independent. Schema 1 still loads; saving explicitly upgrades to schema 2 after retaining an exact `.schema1.bak` copy. Save As retains the original instead. Unknown project properties and future schema versions fail rather than being silently dropped. A conflicting backup blocks migration rather than overwriting recovery data.

Content and reference paths are relative to the project file unless supplied as absolute paths. Desktop Save As rebases relative paths. Game paths are portable forward-slash paths and cannot escape a virtual game root. Symbolic links/junctions are rejected in sources and output ancestors. PAK offsets, sizes and overlapping sections are checked before use.

`JamContent` creates a source inventory before conflict resolution. `sourceSettings` stores an optional root and exact original-path exclusions per content source. This preserves access to conflicting/reserved files for review and mapdb import. Only included files enter the catalog used by discovery, checks, entity inspection and builds. Build manifest schema 2 carries source numbers and entry paths; private absolute source paths are not part of provenance.

ZIP sources use the standard library's `ZipArchive` for indexing and `DeflateStream` for decompression. A bounded tail/header reader validates the central directory before entries are materialized, cross-checks local headers and records compressed byte ranges once. File reads seek directly to those bounded ranges instead of reparsing the directory for every asset. Both declared and actual expanded lengths are checked. CRC checks detect corrupted source entries; SHA-256 verifies content during packaging. No archive extraction, nested-archive expansion or third-party ZIP dependency is involved. See the [jam guide](JAM-MANAGER.md) for explicit limits.

Generated files are written beside their destination as temporary files, flushed, and renamed atomically. New exports refuse existing destinations; explicit project Save can replace the current project. Packaging streams source data and rechecks each source digest while writing. A failed build removes its incomplete temporary package.

ZIP builds use stable ordering, timestamps and generated text. Byte-for-byte ZIP identity is promised for identical inputs on the same runtime, not across future compression-library versions. PAK uses signed 32-bit offsets and 55-byte names; stock classic PAKs are limited to 4096 entries. Use ZIP when PAK limits are exceeded.

## Extension points

Quake 1 should get a separate format/profile implementation, not conditionals that reinterpret Quake II metadata. The current validator is a first set of explicit checks; structured findings (`severity`, `code`, `map`, `message`) let future checks and host integrations share results. A future visual editor may consume these records, but editable graph/undo state belongs to its host adapter.

## Dependency policy

Versions are centralized in `Directory.Packages.props`; committed NuGet lock files fix the complete graph. CI restores in locked mode. Runtime notices are gathered from the actual published dependency graph. Source references were read for format definitions, not copied. Existing GPL v2 licensing is retained; new dependencies must be checked for compatibility before incorporation.
