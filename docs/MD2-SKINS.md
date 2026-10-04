# MD2 skin-table guide

MD2 stores a fixed-width table of external image filenames. Adding a skin means adding a reference to that table and moving subsequent section offsets. The texture pixels remain in separate PCX/TGA/etc. assets; the exact supported image types depend on the engine.

1. Open an MD2 in the skin editor.
2. Keep the existing lines in order. Append the new game-relative filename, such as `models/monsters/soldier/jam.pcx`.
3. Validate the table, then save to a **new** filename. Place the edited copy under its intended final model filename in your mod's staging directory when ready.
4. Include the referenced image at that exact path, using the model's existing skin dimensions and UV layout. Test the model and any numbered skin references in the target game.

You can rename, remove or reorder entries by editing the lines. Reordering/removal changes skin indices and can affect entities or code that selects a numeric skin. A blank middle line is rejected rather than silently removing a slot. Models containing empty slots can be inspected; assign valid paths before exporting.

The first version supports IDP2 version 8, 1–32 output skins, 63 ASCII bytes plus a null terminator per path, at most 2048 vertices, 4096 triangles and 1024 frames. Models over the classic 512-frame limit are labeled as requiring a rerelease-compatible engine; classic jam builds reject them. This accommodates the stock rerelease gunner and soldier models. Unsafe/absolute/parent paths, duplicates ignoring case and malformed or overlapping section layouts fail. Models beyond these limits remain outside this version's scope.

The implementation preserves every byte after the old skin table, including padding and trailers, and updates the skin count, texture-coordinate/triangle/frame/GL-command offsets and declared end offset. It validates the result before writing. The original and existing output files cannot be overwritten. The reader validates container structure/counts, not the semantic correctness of every triangle, GL-command or normal index.

CLI equivalent:

```powershell
sharty md2 inspect tris.md2
sharty md2 skins tris.md2 tris-edited.md2 models/example/old.pcx models/example/new.pcx
```

The second command specifies the **whole desired table**. Supply existing skin names to retain them. Image import, image-format conversion, palette editing, dimension checking, skin painting and 3D preview are planned only after feedback from the modelers.
