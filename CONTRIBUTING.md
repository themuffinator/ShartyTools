# Contributing

Use .NET 10 and PowerShell 7. Run `./scripts/Build.ps1` from a clean checkout before submitting changes. Runtime code is in `src/`, synthetic tests in `tests/`, documentation in `docs/`, and build/release scripts in `scripts/`.

Keep format code in Core so the GUI and CLI agree. Add meaningful regression cases for binary layout changes, malformed input, data preservation, destructive-write prevention and workflow behavior. Do not add commercial game assets to the repository or test outputs. Real reference corpora may be read locally without being bundled.

Build outputs and restore packages use `.artifacts/`. Temporary tests, helpers, logs and task files use `.agents/tmp/<task>/`. Before recursive cleanup, verify the exact resolved target remains in the intended project and has no links. Do not hard-link a live game installation into test data. This project does not launch games or control OS input; any future engine validation must follow the owner's windowed-mode and engine-screenshot rules.

Update `Directory.Packages.props` and regenerate lock files with `dotnet restore ShartyTools.slnx --force-evaluate` when changing packages. Verify licenses first; credit external code in README/notices. Keep first-party action pins current and review release permission changes carefully. Never add push/tag publication triggers without an explicit change to the manual release policy.

Product version changes go through `scripts/Set-Version.ps1`; document user-facing changes in CHANGELOG.md. Project schema changes need migration design, a preserved original file and clear release notes. Report unrelated issues found during work instead of silently expanding the change.
