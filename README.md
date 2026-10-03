# ShartyTools

**A desktop workshop for Quake II jam organizers and model makers.**

Collect a Map-Center jam's submissions, review its map database, check compiled entities, and build a package. Add skins to an MD2 model without hex editing. Both tools share a small, independently testable core with a command-line interface.

The first milestone targets **classic Quake II and the 2023 rerelease**. Quake 1 comes next. A visual entity scripting editor is a separate, deferred integration project. See the [scope and roadmap](docs/ROADMAP.md).

## Tools

| Tool | Available in the initial implementation |
| --- | --- |
| Jam manager | Saved projects; folder and PAK sources; read-only base-game references; BSP discovery; titles, authors, modes and ordering; intro/hub selection; mapdb import/edit/export; entity inspection; validation; ZIP/PAK builds |
| MD2 skin editor | Inspect the existing table; add, rename, remove or reorder skin references; validate classic limits; save an edited copy while preserving the remaining model bytes |
| CLI | Project creation, discovery, QA, packaging, mapdb import/export, entity extraction and MD2 editing |

The app never launches a game. Checks identify structural problems and review items; they do not replace playtesting. MD2 skin entries reference image files; image conversion and 3D previews are outside this milestone.

## Run

For a published release, extract the complete portable archive from [GitHub Releases](https://github.com/themuffinator/ShartyTools/releases). On Windows run `ShartyTools.exe`; on Linux run `./ShartyTools`. Keep the supplied runtime libraries beside the executables. `sharty` is the CLI. No separate .NET installation is needed for portable builds. Releases are unsigned.

From source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run from this repository:

```powershell
dotnet restore ShartyTools.slnx --locked-mode
dotnet run --project src/ShartyTools.Desktop
```

Build and test with PowerShell 7:

```powershell
./scripts/Build.ps1
# Optional: app-rendered headless UI captures, without OS input or screen capture.
./scripts/Build.ps1 -CaptureUi
```

Build outputs live under `.artifacts/`; disposable task data lives under `.agents/tmp/`. Linux desktop use requires the normal X11/fontconfig/native graphics dependencies described by [Avalonia](https://docs.avaloniaui.net/docs/get-started/supported-platforms).

## First jam

1. Choose **New jam**, then set the title, mod-folder ID, engine profile and intro/hub map ID.
2. Put submissions in the project's `content/` folder, or add folders/PAKs under **Content sources**. Each folder must be a game root containing `maps/`, `textures/`, `models/`, etc. Every file in a content root is included.
3. Add the base game's `baseq2` folder/PAKs, and any prerequisite mod, under **Game references**. These are used only to resolve dependencies.
4. Choose **Discover maps**, review titles, mapper credits and game modes, and arrange the map list. Import an existing mapdb if needed. **Apply JSON** commits raw mapdb edits to the project.
5. Run checks, review warnings, save the project and build a ZIP or PAK outside the content roots. Output filenames must be new.

A ZIP contains a top-level mod folder. A PAK contains game-relative files; install it as `pak0.pak` (or the appropriate next PAK) in your jam's mod folder. Classic builds omit `mapdb.json`, which stock Quake II does not use. Read the [jam guide](docs/JAM-MANAGER.md) for the limits of the checks and handling existing submissions.

## Command line

```powershell
dotnet run --project src/ShartyTools.Cli -- --help
dotnet run --project src/ShartyTools.Cli -- jam new ./myjam.sharty.json myjam "My Jam"
dotnet run --project src/ShartyTools.Cli -- jam scan ./myjam.sharty.json
dotnet run --project src/ShartyTools.Cli -- jam check ./myjam.sharty.json --json
dotnet run --project src/ShartyTools.Cli -- jam build ./myjam.sharty.json ./myjam.zip
dotnet run --project src/ShartyTools.Cli -- md2 inspect ./tris.md2
dotnet run --project src/ShartyTools.Cli -- md2 skins ./tris.md2 ./tris-edited.md2 models/example/original.pcx models/example/new.pcx
```

`md2 skins` replaces the complete table in the given order. Include existing names when appending a skin. Exit codes: **0** success, **1** failed QA (also warnings with `--strict`), **2** invalid input or I/O failure. See [MD2 editing](docs/MD2-SKINS.md).

## Stack and structure

C# on .NET 10, Avalonia 12 for the desktop UI, `System.Text.Json` and built-in binary/ZIP APIs for the core, and xUnit v3 plus Avalonia Headless for tests. The core has no third-party runtime packages. [The architecture decision](docs/ARCHITECTURE.md) explains the tradeoffs.

```text
src/ShartyTools.Core/      Formats, project model, validation, packaging
src/ShartyTools.Desktop/   Avalonia interface and workspace state
src/ShartyTools.Cli/       Scriptable access to the shared core
tests/ShartyTools.Tests/   Synthetic format fixtures, workflows and headless UI
scripts/                  Build, version checks and release packaging
docs/                     Requirements, user guides, roadmap and release process
```

## Versions and releases

[`version.txt`](version.txt) is the sole product-version source. Ordinary builds carry a `-dev` suffix; assembly informational versions include the Git revision when available. Release builds use the exact SemVer value. The release tag is `v<version>`. Project schema versions are independent of product versions.

Update the version with `./scripts/Set-Version.ps1 -Version 0.2.0`, update the changelog, then merge the reviewed changes. Run **Actions → Manual release → Run workflow** on `main` and enter the exact version. Draft is selected by default. Pushes and tags do not publish releases. See [the complete release guide](docs/RELEASING.md).

## Credits and license

Licensed under the repository's [GNU GPL v2](LICENSE). There is no warranty. No commercial game data is distributed.

- Product brief: Shartuterie's request for practical jam-integration, MD2 and entity-authoring tools.
- Community context and example events: [Map-Center news and jams](https://map-center.com/forums/news.2/).
- File-format reference: [id Software's Quake II source](https://github.com/id-Software/Quake-2/blob/master/qcommon/qfiles.h), GPL v2. The C# parsers here are newly written; no engine source was copied.
- Extended BSP reference: [ericw-tools](https://github.com/ericwa/ericw-tools). Mapdb behavior was cross-checked against locally available stock rerelease metadata and [Q2REPRO's reader](https://github.com/Paril/q2repro/blob/rerelease-game/src/common/mapdb.c).
- [Avalonia](https://github.com/AvaloniaUI/Avalonia), [.NET](https://github.com/dotnet/runtime), and their dependencies are credited in [third-party notices](THIRD-PARTY-NOTICES.md).

ShartyTools is an independent community project, unaffiliated with id Software, Bethesda or an official Map-Center endorsement.
