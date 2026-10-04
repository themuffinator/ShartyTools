# Jam manager guide

## Project layout

Keep the project file beside the submission roots, with exports elsewhere:

```text
foundry-jam/
  foundry.sharty.json
  submissions/
    mapper-a/maps/fj_01.bsp
    mapper-a/textures/foundry/wall.wal
    mapper-b/maps/fj_02.bsp
  shared/models/...
  exports/
```

Add `submissions/mapper-a`, `submissions/mapper-b` and `shared` as content sources. A source is a **game root**, not its `maps` subfolder. PAK and ZIP files can also be sources. Multiple identical included files are deduplicated; differing contents at one path must be resolved before building. Case-only collisions fail even when bytes match.

New sources include all files by default. Review **Files** before building and exclude private notes, old builds and unused versions. Exclusions affect discovery, checks, entity inspection and packaging together; excluding a listed BSP produces a missing-map error until its listing is removed. Empty projects can be saved but cannot be built.

## Review submissions

Use **Files → Refresh files**, then select a content source. The list remains available even when two sources provide conflicting bytes. Selecting a file shows its original path, destination path, size, inclusion state and any other sources that provide the same destination. Exclude the unwanted copy and run checks to verify the result. Inclusion is per source and exact original path, so one submission's `textures/shared.wal` can be kept while another is omitted. The UI currently selects one file at a time.

For a ZIP containing `pack/maps/example.bsp`, choose **Set root** and enter `pack`. The packaged path becomes `maps/example.bsp`. Leave the root empty if the archive already contains `maps/` directly. Roots also work with folders and PAKs; they use exact case and no leading/trailing slash. All files outside the chosen root are omitted and remain visible as **Outside root**. Review readmes/licenses outside a wrapper folder before using this option; supply any required notices through a content folder. Root changes keep exclusions keyed to original paths.

ZIPs are indexed and streamed without extraction. Supported archives are single-volume, non-ZIP64 ZIPs using stored or Deflate compression, up to 2 GiB compressed, 16,384 entries, 512 MiB per expanded file and 8 GiB total expanded content. The central directory is limited to 32 MiB before parsing. Encrypted entries, links/special files, duplicate exact paths and traversal paths are rejected. Reads check expanded lengths and CRCs; builds also verify SHA-256 digests while copying. Embedded ZIPs/PAKs are not recursively unpacked: add a PAK separately when its maps need to be discovered.

Folder links/junctions are rejected. Included game paths must use portable ASCII letters, digits, `_`, `-`, `/` and `.`. ZIP/folder inventories can display ordinary readmes with spaces or Unicode so they can be excluded; unsafe traversal or link entries block source indexing even if they would be outside the root. Rename or copy required notices into a working content folder with a portable name when necessary.

`jam files PROJECT --json` exposes the same inventory. `jam add PROJECT PATH [--root PREFIX]` adds a source; this command resolves `PATH` against the working directory, then stores a project-relative path. `jam include|exclude PROJECT SOURCE_NUMBER ENTRY_PATH` changes a file choice. `jam root PROJECT SOURCE_NUMBER PREFIX` changes the root; `-` clears it. Source numbers are 1-based, and entry paths are the original paths shown by `jam files`, before root stripping. CLI mutations save the project; desktop changes need **Save**.

Schema 2 stores these choices in `sourceSettings`, keyed by the exact string in `sources`:

```json
"sourceSettings": {
  "submissions/mapper-a.zip": {
    "root": "pack",
    "excludedFiles": ["pack/mapdb.json", "pack/old-draft.txt"]
  }
}
```

Schema-1 projects still open. Saving upgrades them to schema 2 and first keeps an exact `<project>.schema1.bak` copy. An existing different backup blocks the save; use Save As or move that backup. Save As rebases source paths and settings keys, and leaves the original project intact. Older ShartyTools builds will reject schema 2. Keep source path spelling consistent when editing JSON; the desktop drops a removed source's settings when its source line is removed.

## References

Add installed/extracted `baseq2`, its PAK files, and prerequisite mod roots as **Game references**. Reference directories index loose files and PAKs recursively; explicit PAK paths and game-root ZIP paths are also supported. Reference ZIP paths can be entered in the source textbox; content root/exclusion controls apply only to Content sources. References only answer whether an asset exists, so the resolver does not model every engine's load-order precedence. They are never copied or modified.

Do not place a game installation in Content sources: those files would be treated as submission content. Save builds outside all source/reference roots. Missing dependencies are warnings because stock assets, implicit class resources and custom mod logic cannot all be inferred statically.

## Map database

Discovery reads `maps/*.bsp`, including subdirectories, and seeds the title/author from worldspawn and modes from spawn entities. Existing curated map entries keep their values. The list order is the exported mapdb order. Select the entry/intro/hub map using its ID without `maps/` or `.bsp`.

The editor supports ordinary fields in the map detail panel and complete JSON in the Map database tab. Import replaces the listing, preserves existing author credits for matching BSP IDs, and keeps episode commands, `activity`, `unit`, `start_items`, custom properties and other unknown data. Existing `bot`/`bots` spelling is retained; newly generated entries use the stock rerelease's `bot` spelling.

Stock rerelease launch expressions such as `intro.cin+*base1$start` are retained in mapdb. Checks and entity inspection resolve the final BSP, and cinematic dependencies are reported separately. A leading `*` starts a new unit, `+` chains launch stages and `$` selects a spawn point. Enter these expressions in JSON when needed; discovery creates plain BSP IDs.

Multiple menu listings may point to the same BSP (the stock Quake II 64 metadata does this). They retain separate titles, mode flags and additional fields. Asset packaging still includes the physical BSP only once.

Choose **Apply JSON** after editing raw JSON. Save/build/check operations reject unapplied changes instead of silently ignoring them. **Refresh** intentionally replaces the text with the current structured listing. Export writes structured project metadata to a new file.

The jam's generated episode has an `id`, display `name`, `command` pointing at the start map, and skill-selection setting. Imported episodes and commands stay intact; if an imported episode has the jam's ID, edit its title/launch command in JSON explicitly. A map referring to an unknown episode is an error.

Generated files reserve these root paths: `mapdb.json`, `sharty-manifest.json`, `sharty-credits.txt`. If an included source file occupies one of them, the build reports a conflict. Select the source's root `mapdb.json` in Files and choose **Import this mapdb**. A successful import replaces the listing and excludes that original mapdb; a failed import leaves metadata and selection unchanged. Unknown fields and existing matching mapper credits are retained. Imported mapdb is limited to 4 MiB of UTF-8 JSON. CLI: `mapdb import-source PROJECT SOURCE_NUMBER ENTRY_PATH`.

Exclude other generated files explicitly. Importing one source's mapdb does not merge every submission's listing or exclude other copies. You can subsequently discover missing maps and curate their metadata. The standalone **Map database → Import** operation remains available; it does not change source selections. Original folders, PAKs and ZIPs are never edited.

Classic Quake II builds do not contain a mapdb; it remains editable in the project for a rerelease variant. An installed mod's custom game code, configuration, localization, image assets and required licenses must be provided in content roots as appropriate. The tool does not download these or assume a mod license.

## What checks mean

| Finding | Behavior |
| --- | --- |
| Bad project/schema/source paths, conflicting files or reserved outputs | Error; build blocked |
| Missing listed BSP, unsupported/truncated BSP, invalid lump ranges | Error |
| QBSP under stock classic profile | Error; choose rerelease/extended target or compile IBSP |
| Missing/extra/non-first worldspawn or empty classname | Error |
| SP/co-op without `info_player_start`; DM without `info_player_deathmatch` | Error |
| Co-op without dedicated co-op starts | Warning; engines may provide fallback behavior |
| Missing title/credit, unset start map, no selected mode, unlisted BSP | Warning |
| Unmatched `target`, `killtarget`, `pathtarget`, `deathtarget` | Warning; custom scripts may handle links |
| Unresolved changelevel map (including `*unit$spawn` syntax) | Warning |
| Explicit entity model/noise references, texinfo WAL/TGA/PNG references, MD2 skin paths | Missing assets are warnings |
| Invalid packaged MD2 structure | Error |
| MD2 with more than 512 frames in a classic project | Error; extended models can be edited and packaged for rerelease |
| Repeated entity keys | Warning (editor-only `_` keys: info); checks use the final value, matching game parsing |
| Uppercase game paths | Warning about case-sensitive engines |
| Bots requested without the expected navigation reference | Warning; manually verify bot navigation and target-engine conventions |

The parser supports Quake II **IBSP 38** and **QBSP 38**, including their shared entity and texinfo layout. It does not validate every geometry lump, render a map, detect leaks, assess gameplay, check spatial spawn safety, verify texture dimensions, traverse WAL animation chains, or understand all custom entities/scripts. Entity extraction reads the compiled BSP, not a separate overriding `.ent` file; review overrides manually. Text is decoded as Latin-1 to preserve classic entity bytes and exported as UTF-8.

Bots, single-player skill filtering, engine-specific limits beyond the implemented format checks, localization and mod dependencies still need target-engine testing. Missing references alone are not proof that the package is broken. Use CLI `--strict` only after configuring references and resolving intentional warnings.

## Output

ZIP contains `<jam-id>/...` for extraction beside `baseq2`. PAK contains root-relative assets and belongs inside that mod folder, usually as `pak0.pak`. PAK entry names max out at 55 ASCII bytes; total data is bounded by signed 32-bit offsets, and classic PAK builds allow at most 4096 entries. ZIP is preferable for larger packages.

Both formats include generated credits and a schema-2 JSON build manifest with asset lengths/hashes and validation findings. `sources` records 1-based source numbers, basenames and chosen roots; each original file's `origin` records its source number and original entry path. Identical duplicates use the first included source; generated files have a null origin. Source provenance avoids recording local absolute source paths. Rerelease builds include mapdb. Nothing is inferred about the redistribution rights of assets; retain the submission's licenses/readmes in its content root and review credits before publication.

Builds do not overwrite an existing archive. Remove or move your previous export yourself before rebuilding to the same name. Determinism covers unchanged content and project metadata on the same runtime; validation findings are part of the build manifest.
