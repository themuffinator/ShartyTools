# Versioning and manual releases

## One version source

`version.txt` contains the product's SemVer, initially `0.1.0`. Both apps read it through `Directory.Build.props`; do not hand-edit assembly versions. Ordinary builds append `-dev`. The .NET SDK appends the source revision to informational versions when Git is available. These versions identify the base commit, not a hash of uncommitted changes; use a clean checkout for reproducible artifacts.

The release tag is `v<version>`. Release archives, binaries and changelog entry use the same value. Prereleases such as `0.2.0-rc.1` are supported and are marked as GitHub prereleases rather than “latest”. Build metadata in `version.txt` is deliberately disallowed. Jam `schemaVersion` is independent: schema 1 does not change whenever the application version changes.

Before 1.0, use a minor release for user-visible features or breaking changes and a patch release for compatible fixes. Describe any project-format change explicitly. After 1.0, use normal SemVer major/minor/patch compatibility rules.

## Prepare a release

1. Run `./scripts/Set-Version.ps1 -Version 0.2.0` (or the desired prerelease value).
2. Add a nonempty `## [0.2.0]` section in `CHANGELOG.md`; include user-visible changes, migration notes and limitations. Add the publication date when known.
3. Run `./scripts/Build.ps1`. For a packaging dry run use `./scripts/Pack.ps1 -Runtime win-x64 -Release` on Windows, or `-Runtime linux-x64` on Linux. Nothing is published by these scripts.
4. Review the archives and commit/merge the version, changelog, code and dependency lock files. CI must pass on both Windows and Linux.

The supplied `global.json` accepts local preview SDKs because the initial development host had one installed; CI/release setup explicitly requests the stable `10.0.x` channel. Dependencies are pinned centrally and locked. Do not disable auditing or locked restores to get a release through; resolve the graph or vulnerability first.

## Dispatch on GitHub

The workflow must exist on the default branch to appear as a runnable manual workflow, per [GitHub's workflow-dispatch rules](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow). After this implementation is merged:

1. Open **Actions → Manual release → Run workflow**.
2. Select `main` and enter the exact value from `version.txt` in `expected_version`.
3. Leave **draft** selected to inspect the result before publication. Clear it only when ready to publish directly.
4. Start the run. Inspect its result, binaries, checksums and notes; if it is a draft, use GitHub's **Publish release** action when ready.

Only `workflow_dispatch` can invoke a release. Pushing commits or tags cannot publish one. The workflow only accepts the default branch. Validation checks the requested version, nonempty changelog, clean tree and absence of the tag. Jobs build and test the exact dispatch commit. Only the final release job has repository-write permission.

The workflow builds portable self-contained **Windows x64 ZIP** and **Linux x64 tar.gz** archives with both GUI and CLI, complete notices and documentation. It verifies downloaded artifact checksums and attaches a corresponding-source ZIP and SHA-256 file. The tag targets the exact workflow commit. Scripts pass input through environment variables/structured argument arrays, not interpolated shell fragments.

No signing certificate, Apple notarization, package-manager feed, installer or automatic update service is configured. Binaries are unsigned. Linux tar archives are made on Linux to retain executable permissions.

## Failure and recovery

No existing version/tag is replaced. If a build or test fails before release creation, fix it and rerun. If GitHub release creation partially succeeds, inspect the existing draft/release and assets before retrying. A published version should be corrected with a new patch version; do not move its tag or replace its assets. An unpublished failed draft may be removed deliberately by a maintainer before retrying the same version.

Local packaging refuses to overwrite archives. Move a prior local archive aside or remove verified disposable output before rebuilding it. Build staging directories are cleaned automatically within `.agents/tmp/pack/`; completed archives remain in `.artifacts/releases/`.

The runtime dependency inventory and notices are collected from published `.deps.json` files and restored package metadata. A new dependency requires license review and any missing upstream notice before it can ship. Test-only packages are not included in the application archives.
