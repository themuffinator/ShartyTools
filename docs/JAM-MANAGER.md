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

Add `submissions/mapper-a`, `submissions/mapper-b` and `shared` as content sources. A source is a **game root**, not its `maps` subfolder. PAK files can also be sources. Multiple identical files are deduplicated; differing contents at one path must be resolved before building. Case-only collisions fail even when bytes match.

Source folders are included in their entirety, so keep private notes, old builds, source-control directories and unused versions elsewhere. ZIP inputs are not unpacked by the app; extract a trusted submission first. Source links/junctions are rejected. Empty projects can be saved but cannot be built.

## References

Add installed/extracted `baseq2`, its PAK files, and prerequisite mod roots as **Game references**. Reference directories index loose files and PAKs recursively; explicit PAK paths are also supported. References only answer whether an asset exists, so the resolver does not model every engine's load-order precedence. They are never copied or modified.

Do not place a game installation in Content sources: those files would be treated as submission content. Save builds outside all source/reference roots. Missing dependencies are warnings because stock assets, implicit class resources and custom mod logic cannot all be inferred statically.

## Map database

Discovery reads `maps/*.bsp`, including subdirectories, and seeds the title/author from worldspawn and modes from spawn entities. Existing curated map entries keep their values. The list order is the exported mapdb order. Select the entry/intro/hub map using its ID without `maps/` or `.bsp`.

The editor supports ordinary fields in the map detail panel and complete JSON in the Map database tab. Import replaces the listing, preserves existing author credits for matching BSP IDs, and keeps episode commands, `activity`, `unit`, `start_items`, custom properties and other unknown data. Existing `bot`/`bots` spelling is retained; newly generated entries use the stock rerelease's `bot` spelling.

Stock rerelease launch expressions such as `intro.cin+*base1$start` are retained in mapdb. Checks and entity inspection resolve the final BSP, and cinematic dependencies are reported separately. A leading `*` starts a new unit, `+` chains launch stages and `$` selects a spawn point. Enter these expressions in JSON when needed; discovery creates plain BSP IDs.

Multiple menu listings may point to the same BSP (the stock Quake II 64 metadata does this). They retain separate titles, mode flags and additional fields. Asset packaging still includes the physical BSP only once.

Choose **Apply JSON** after editing raw JSON. Save/build/check operations reject unapplied changes instead of silently ignoring them. **Refresh** intentionally replaces the text with the current structured listing. Export writes structured project metadata to a new file.

The jam's generated episode has an `id`, display `name`, `command` pointing at the start map, and skill-selection setting. Imported episodes and commands stay intact; if an imported episode has the jam's ID, edit its title/launch command in JSON explicitly. A map referring to an unknown episode is an error.

Generated files reserve these root paths: `mapdb.json`, `sharty-manifest.json`, `sharty-credits.txt`. If a source contains them, the build reports a conflict. Import the existing mapdb and remove it from your **working submission copy**, or unpack a PAK into a clean staging source. The app never edits the original PAK. Per-file inclusion/exclusion controls are a later improvement.

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

Both formats include generated credits and a JSON build manifest with asset lengths/hashes and the validation findings. Rerelease builds include mapdb. Nothing is inferred about the redistribution rights of assets; retain the submission's licenses/readmes in its content root and review credits before publication.

Builds do not overwrite an existing archive. Remove or move your previous export yourself before rebuilding to the same name. Determinism covers unchanged content and project metadata on the same runtime; validation findings are part of the build manifest.
