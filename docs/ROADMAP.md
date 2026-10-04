# Roadmap

## Milestone 0.1 — initial workshop

Implemented: shared format library, jam projects, folder/PAK/ZIP sources, per-source roots and file inclusion, source mapdb import, map discovery and metadata, rerelease mapdb preservation, entity extraction and structural QA, deterministic packages with provenance, MD2 skin editing, desktop GUI, CLI, tests, documentation, versioning and manual releases.

Before calling a release community-tested, an organizer should run a real jam through the tool, inspect the output, and playtest it in the target engine. The current automated fixtures establish format and workflow behavior, not gameplay correctness. See [verification](VERIFICATION.md) for actual local/CI evidence.

## Milestone 0.2 — feedback from organizers and modelers

- Validate against organizer-provided jam projects and document mod-specific conventions.
- Gather feedback on ZIP submission limits, per-file inclusion and source-root selection; consider batch selection, nested PAK handling and multiple-submission metadata merging if needed.
- Expand dependency resolution for WAL animation chains, skyboxes, explicit script includes, custom entities and mod inheritance.
- Capture skin-editor feedback: image browsing/dimension checks, previews, duplicate-slot workflows and extended-engine limits.
- Improve large-project progress/cancellation and per-rule configuration after measuring real jams.

## Next game — Quake 1

Add explicit Quake 1 profiles, BSP 29/BSP2 parsing and game-appropriate entity checks. Investigate embedded/external texture behavior, WAD-related workflows and Quake rerelease mapdb differences. Reuse packaging, conflict checks and reports without treating Quake II assumptions as universal.

## Deferred research — visual entity editor

This third request is valuable but is not a small utility. Choose a host after interviewing the mappers: likely candidates are an editor in their existing TrenchBroom/NetRadiant workflow, or another app they already use. No host has been selected and no upstream integration is claimed.

Investigate:

1. Host extension APIs, licenses, release policy and maintainer willingness to accept integration.
2. Entity-definition sources (FGD/DEF and mod-specific metadata), target/targetname semantics and fields beyond simple edges.
3. Bidirectional selection between map entities and graph nodes, grouped links, filtering and validation.
4. Preservation of unrecognized keys, stable identity, transactional edits, undo/redo and export without losing map formatting.
5. Actual workflows from complex scripted maps, rather than designing a generic graph canvas first.

Acceptance gate: a small prototype can edit a real target chain in a chosen host, round-trip unknown properties, undo the edit and preserve the rest of the map. Only then estimate a user-facing release. The existing entity reader and QA findings are reusable foundations; compiled BSP editing is not the authoring workflow.
