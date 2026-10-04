# Third-party notices and source references

ShartyTools code is distributed under [GPL v2](LICENSE). The original repository license has been retained. No game data or copied engine implementation is included. Newly written format readers were checked against these sources:

- [id Software Quake II `qfiles.h`](https://github.com/id-Software/Quake-2/blob/master/qcommon/qfiles.h) — GPL v2; authoritative MD2, BSP and PAK layouts.
- [ericw-tools](https://github.com/ericwa/ericw-tools) — GPL-family source reference for extended Quake II BSP layout; no implementation copied.
- [Q2REPRO mapdb reader](https://github.com/Paril/q2repro/blob/rerelease-game/src/common/mapdb.c) — GPL v2 source reference for supported metadata; no implementation copied. Its [MD2 definitions](https://github.com/Paril/q2repro/blob/rerelease-game/inc/format/md2.h) also document the 1024-frame extended limit.
- Stock Quake II rerelease mapdb was inspected locally for compatibility, including singular `bot`. It is not redistributed.

## Runtime components

| Component | License / notice source |
| --- | --- |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) and its platform/theme packages | MIT; [upstream notice](third_party/Avalonia-MIT.txt) |
| [.NET runtime](https://github.com/dotnet/runtime) | MIT plus bundled third-party notices; [upstream license](third_party/Dotnet-MIT.txt); runtime-pack notices are collected at packaging |
| [MicroCom](https://github.com/kekekeks/MicroCom) | MIT; [upstream notice](third_party/MicroCom-MIT.txt) |
| [Tmds.DBus](https://github.com/tmds/Tmds.DBus) | MIT; [upstream notice](third_party/Tmds-DBus-MIT.txt) |
| [SkiaSharp](https://github.com/mono/SkiaSharp) / Skia | MIT and native third-party licenses; full LICENSE and THIRD-PARTY-NOTICES files are copied from the resolved NuGet packages |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) / HarfBuzz | MIT and native third-party licenses; full LICENSE and THIRD-PARTY-NOTICES files are copied from the resolved NuGet packages |
| [Inter](https://github.com/rsms/inter) | SIL Open Font License 1.1; [upstream notice](third_party/Inter-OFL.txt), included conservatively for the transitive font package |

These permissive/runtime/font licenses were checked before integration for compatibility with distributing the toolkit under GPL v2. The full exact dependency graph is recorded in `packages.lock.json`. `Collect-Notices.ps1` collects notices and `.nuspec` metadata from the actual published runtime graph and writes `licenses/dependencies.json` in each binary archive. Native components can have additional permissive notices; these are retained rather than reduced to the wrapper's MIT label.

## Development tools

[xUnit](https://github.com/xunit/xunit), [Microsoft Test Platform](https://github.com/microsoft/vstest), and [Avalonia Headless](https://github.com/AvaloniaUI/Avalonia) are test dependencies, not application binaries. GitHub's first-party checkout, .NET setup and artifact actions are pinned to reviewed commit IDs. Dependency updates should refresh lock files and this review where applicable.

## Community and assets

The product requirements came from Shartuterie's tool suggestions and [Map-Center's jam community](https://map-center.com/forums/news.2/). Community names are attribution, not an endorsement. Quake and Quake II are trademarks of their respective owners. No permission to redistribute any submitted game content is inferred by the application; organizers retain that responsibility and should include original asset credits/licenses.
