# Verification evidence

Local validation date: **3 October 2026**. This records completed checks and their limits; a release entry does not imply that it has been published.

## Automated checks

`scripts/Build.ps1` restores the locked graph, builds the solution with warnings treated as errors, runs xUnit/Avalonia Headless tests and validates the version/changelog. Tests cover MD2 offset relocation and byte preservation, malformed inputs, classic/extended frame counts, BSP/entity parsing, source collisions, PAK/ZIP round-trips, deterministic packaging, dependency checks, mapdb preservation, workspace persistence and rendering the desktop tools.

The full local suite passed **49 cases**, with zero build warnings or errors. Reference-data findings led to regression tests for repeated entity keys, launch expressions, extended stock models and multiple menu listings per BSP. The detailed results are in `.artifacts/test-results/tests.trx`; hosted CI reports its own results.

Headless UI renderings were inspected from `.artifacts/verification/jam-manager.png` and `md2-editor.png`. They come from the application's render target. No OS screen capture, mouse/keyboard injection or game launch was used.

The Windows self-contained archive was built with `Pack.ps1 -Runtime win-x64 -Release`, then extracted and exercised by `Test-Package.ps1`. Its checksum, required files, dependency notices, CLI version/help, project creation and failure exit codes passed. Both workflows passed `actionlint`; PowerShell scripts were parsed and the version/changelog validation was exercised locally. No GitHub release was published by this verification.

The initial hosted CI run also passed on **Windows and Linux**, including the 49-case suite, portable packaging and execution of each platform's packaged CLI. The PR's latest checks remain the authority for subsequent changes.

## Read-only compatibility corpus

The owner's existing extracted assets were inspected locally, without copying game content into this repository:

| Corpus | BSP files parsed | MD2 files parsed and edited in memory | Mapdb entries round-tripped |
| --- | ---: | ---: | ---: |
| Classic Quake II | 48 | 182 | — |
| Quake II rerelease | 222 | 441 | 235 |
| Total | 270 | 623 | 235 |

All passed the final compatibility pass. Every MD2 edit also compared the entire non-skin payload before/after. The aggregate report is retained locally at `.artifacts/verification/reference-checks.json`; it contains counts and paths, not game data.

This pass found and resolved three real-world conventions: repeated entity keys use their final value; stock mapdb accepts cinematic/unit/spawn-point launch expressions and more than one menu listing for a BSP; rerelease gunner/soldier MD2s exceed the original 512-frame limit. Models up to 1024 frames are now supported, with classic-profile checks retained.

## Remaining verification

Actual gameplay, renderer behavior, native file dialogs and a full organizer-provided jam have not been manually exercised. Headless rendering and binary checks are useful evidence, not an in-engine playtest. Real asset references were checked locally; they are neither committed nor used in hosted CI. See CI/release job results for platform-specific packaging evidence.

No unrelated pre-existing repository defects were found; the repository initially contained only its license and Git configuration files.
