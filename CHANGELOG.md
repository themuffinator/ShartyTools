# Changelog

All notable user-facing changes are recorded here. Versions follow [Semantic Versioning](https://semver.org/).
Release dates are recorded when a maintainer publishes a release; a heading here is not a claim that it has shipped.

## [Unreleased]

## [0.1.0] - 2026-10-04

### Added

- Quake II desktop workshop with jam management and an MD2 skin-table editor.
- Portable jam projects with content folders/PAKs/ZIPs, read-only game references, map discovery, ordering, metadata and mapper credits.
- Submission file inventory, per-source content roots and inclusion controls; mapdb import directly from sources, bounded ZIP reads and build provenance.
- Schema-2 projects with exact schema-1 backups on upgrade and rebased source settings on Save As.
- Rerelease mapdb import, JSON editing and export with preservation of additional fields and episode commands.
- IBSP/QBSP entity extraction and checks for starts, worldspawn, dangling targets, map transitions and explicit asset references.
- Deterministic ZIP and PAK assembly with conflict detection, build manifests and SHA-256 hashes.
- MD2 skin-table rewriting that preserves geometry, UVs, frames, padding and trailing data; originals are retained.
- Command-line access to the same operations for repeatable QA and builds.
- Windows/Linux CI and an exclusively manual release workflow with version checks, source archives, license notices and checksums.

### Downloads

- Windows x64: extract `ShartyTools-0.1.0-win-x64.zip` and run `ShartyTools.exe`.
- Linux x64: extract `ShartyTools-0.1.0-linux-x64.tar.gz` and run `./ShartyTools`.
- Both portable downloads include the `sharty` CLI and .NET runtime. Keep the archive's files together. Corresponding source and SHA-256 checksums are provided alongside the binaries.

### Scope

- Quake II classic and rerelease profiles. Quake 1 support is planned next.
- The visual entity editor is deferred pending a host-editor integration design.
- No game renderer, texture conversion, automatic asset-license clearance or automated playtesting.
- Automated format, workflow and headless UI checks pass on Windows and Linux; organizers should still inspect packages and playtest in their target engine.
