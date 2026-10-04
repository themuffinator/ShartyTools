# Product brief and acceptance criteria

The supplied conversation image describes three requests from Shartuterie, dated 29 September 2026. This document paraphrases that brief; the private screenshot is not redistributed.

## Agreed scope

The owner confirmed Quake II classic and rerelease first, with Quake 1 planned next. “Jams” means [Map-Center mapping jams](https://map-center.com/forums/news.2/), not an online event-management or submission service. Initial work is a local desktop application, with matching command-line operations.

The public event examples inform requirements without being bundled as test assets. The [PSX Jam 2 release](https://map-center.com/threads/quake-2-remaster-psx-jam-2-release.63/) describes an intro, a hub, several contributor maps and a secret map built on an existing mod. The news index also describes multi-map Warehouse and N64 jams. These imply explicit entry-map selection, mod references, ordered listings and preservation of specialized metadata. They do not establish a universal mapdb schema or permission to redistribute jam assets.

## 1. Jam integration — implemented initial scope

- A central GUI saves and reopens a portable project manifest.
- Organizers collect maps, textures and other assets from explicit folders and existing PAKs/ZIPs, choosing a content root and preserving game-relative layout.
- Source file inventories remain usable during conflicts; per-source exclusions resolve them without changing original submissions. Included source mapdb can be imported and excluded in one operation.
- Different bytes at one asset path and case-only collisions block builds. Identical duplicates are reported and deduplicated.
- Stock/prerequisite assets can resolve checks through separate read-only references and are never packaged.
- Maps are discovered from IBSP 38 or QBSP 38; organizers set display metadata, authors, modes, order and start map.
- Existing mapdb JSON can be imported, edited and exported, preserving unknown root/map fields, episodes and commands.
- Entity lumps can be inspected and exported. Checks cover structure, worldspawn, starts, selected entity links, transitions and explicit file references.
- Builds are deterministic for identical inputs on the same runtime, emit asset hashes, source provenance and credits, and fail on validation errors. Strict CLI mode also rejects warnings.
- Source submissions and game installations are not modified.

## 2. MD2 skin table — implemented initial scope

- Open an IDP2 version 8 model and inspect the skin names and model counts.
- Add, rename, remove and reorder references within classic Quake II limits.
- Reject malformed layouts, invalid paths, unsupported counts, duplicate names and overlong strings.
- Recompute every downstream offset when the skin table changes length.
- Preserve all original non-skin payload, including gaps and trailing bytes.
- Save to a new file; do not overwrite the source or existing output.

This is a skin-reference editor. Painting, image import/conversion, resampling, 3D rendering, animation editing and geometry modification require separate requirements from modelers.

## 3. Visual entity authoring — deferred

Entity scripting is more useful inside a map editor with map selection, geometry context, entity definitions and undo. Do not build a detached visual graph with unreliable write-back. The [roadmap](ROADMAP.md) records the host-integration investigation and a proposed acceptance gate.

## Release engineering

- A single product-version source, SemVer tags and revision-bearing development builds.
- A changelog, pinned dependency graph, reproducible build scripts and Windows/Linux CI.
- Manual GitHub workflow only; no release-on-push or release-on-tag trigger.
- Version/changelog validation, immutable existing tags, portable binaries, dependency notices, checksums and corresponding source.
- Draft by default; maintainer can explicitly choose publication when dispatching.
