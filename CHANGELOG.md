## v1.10.4 (patch)

Changes since v1.10.3:

- Bump the ktsu group with 8 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.10.3 (patch)

Changes since v1.10.2:

- Bump Moq from 4.20.72 to 4.21.0 ([@dependabot[bot]](https://github.com/dependabot[bot]))
- Bump the ktsu group with 9 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.10.2 (patch)

Changes since v1.10.1:

- Bump the ktsu group with 16 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.10.1 (patch)

Changes since v1.10.0:

- Bump the ktsu group with 11 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))
- Bump ktsu.AppDataStorage and 15 others ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.10.0 (minor)

Changes since v1.9.0:

- refactor: filter the sync candidates in the sequence, not the body [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: answer for a malformed path instead of throwing out of it [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: push the branch by name so a first push is not refused [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- refactor: project the sync candidates instead of remapping them [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: give sync's commits a committer identity of their own [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- feat: delegate local git operations to ktsu.GitIntegration [minor] ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.9.0 (minor)

Changes since v1.8.0:

- Merge remote-tracking branch 'origin/main' into claude/sync-explicit-repos ([@Claude](https://github.com/Claude))
- Merge main into the explicit-repos branch ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: deduplicate the scan with Distinct instead of a running set [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: filter the scan candidates with Where [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: split the scan into enumerate, filter and dedupe [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): assert the join against a concrete path, not against Combine ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: join a directory to a file name instead of combining [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: guard the combined filename and cover the new scan paths [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: accept explicit repo paths and directory exclusions [minor] ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.8.0 (minor)

Changes since v1.7.0:

- test(sync): join the temp paths instead of combining them [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): drive the sync-config verbs and sync --config [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): cover saved configurations and the flag override [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: save named configurations of the inputs flags repeat [minor] ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.7.0 (minor)

Changes since v1.6.0:

- test(sync): cover the pull request paths for the quality gate [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: filter the branch switches with Where instead of an inner if [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: open a pull request for each synced branch with --pr [minor] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(packages): cover the find-unused report rendering and its verb [patch] ([@Claude](https://github.com/Claude))
- packages: filter the element scans with Where instead of in the loop body [patch] ([@Claude](https://github.com/Claude))
- test(packages): cover the find-unused classification rules [patch] ([@Claude](https://github.com/Claude))
- packages: add a find-unused verb for package references no source uses [minor] ([@Claude](https://github.com/Claude))

## v1.6.1 (patch)

Changes since v1.6.0:

- Bump Polyfill from 11.3.0 to 11.4.0 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.6.0 (minor)

Changes since v1.5.0:

- repo: pick the lfs summary colour with a switch instead of a nested ternary [patch] ([@Claude](https://github.com/Claude))
- test(repo): cover the workspace command shell and the pointer report [patch] ([@Claude](https://github.com/Claude))
- repo: share the workspace-walk and command shell the lfs verb duplicated [patch] ([@Claude](https://github.com/Claude))
- repo: add an lfs install verb to configure Git LFS across a workspace [minor] ([@Claude](https://github.com/Claude))

## v1.5.0 (minor)

Changes since v1.4.0:

- test(sync): derive the path root instead of assuming a leading separator [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): map and filter with LINQ instead of accumulating loops [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): canonicalise the temp repo paths for macOS [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): cover the branch-mode paths for the quality gate [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): use Path.Join for the temp repo paths [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: commit onto a dedicated branch with --branch [minor] ([@matt-edmondson](https://github.com/matt-edmondson))
- Assert parallel build overlap by concurrency, not wall clock ([@matt-edmondson](https://github.com/matt-edmondson))
- Cover the output paths of repo list ([@matt-edmondson](https://github.com/matt-edmondson))
- Add a repo list verb for cached repositories and solutions ([@matt-edmondson](https://github.com/matt-edmondson))
- Align the ktsu.Semantics.Strings pin with ktsu.Semantics.Paths ([@matt-edmondson](https://github.com/matt-edmondson))
- Gate Dependabot auto-merge on CI actually being green ([@Claude](https://github.com/Claude))
- ci: adopt the consolidated .NET workflow [patch] ([@Claude](https://github.com/Claude))

## v1.4.2 (patch)

Changes since v1.4.1:

- Bump MSTest.Sdk from 4.4.0 to 4.4.1 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.4.1 (patch)

Changes since v1.4.0:

- Assert parallel build overlap by concurrency, not wall clock ([@matt-edmondson](https://github.com/matt-edmondson))
- Cover the output paths of repo list ([@matt-edmondson](https://github.com/matt-edmondson))
- Add a repo list verb for cached repositories and solutions ([@matt-edmondson](https://github.com/matt-edmondson))
- Align the ktsu.Semantics.Strings pin with ktsu.Semantics.Paths ([@matt-edmondson](https://github.com/matt-edmondson))
- Gate Dependabot auto-merge on CI actually being green ([@Claude](https://github.com/Claude))
- ci: adopt the consolidated .NET workflow [patch] ([@Claude](https://github.com/Claude))

## v1.4.0 (minor)

Changes since v1.3.0:

- Address code quality Path.Combine comments in repo cache tests ([@copilot-swe-agent[bot]](https://github.com/copilot-swe-agent[bot]))
- Add repo validate command to prune stale cache entries ([@copilot-swe-agent[bot]](https://github.com/copilot-swe-agent[bot]))
- Initial plan ([@copilot-swe-agent[bot]](https://github.com/copilot-swe-agent[bot]))

## v1.3.0 (minor)

Changes since v1.2.0:

- test: compare the parsed divergence as a value rather than dereferencing it [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- feat: add ktsu repo fetch for a read-only workspace survey [minor] ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: raise ktsu.Semantics.Strings to the version Semantics.Paths requires [patch] ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.0 (minor)

Changes since v1.1.0:

- Answer S2325 where CA1822 was already answered ([@Claude](https://github.com/Claude))
- Let codegen use the code generator rather than a copy of one ([@Claude](https://github.com/Claude))
- Raise Semantics.Strings to the version Semantics.Paths asks for ([@Claude](https://github.com/Claude))

## v1.1.1 (patch)

Changes since v1.1.0:

- Bump the ktsu group with 4 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.1.0 (minor)

Changes since v1.0.0:

- test: cover the git command line assembly and the git output passthrough [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- refactor: use Path.Join in the git command tests [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- Merge origin/main into fix/align-semantics-versions ([@matt-edmondson](https://github.com/matt-edmondson))
- feat: add ktsu git to run a git command across every repository under a directory [minor] ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: align ktsu.Semantics with the version AppDataStorage requires [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- ci: make the SonarQube quality gate opt in [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- ci: adopt the unified dotnet workflow [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- chore: store icon.png in LFS as .gitattributes declares ([@matt-edmondson](https://github.com/matt-edmondson))
- docs: scope build badge to the default branch ([@matt-edmondson](https://github.com/matt-edmondson))
- docs: broaden TAGS.md for better topic coverage ([@matt-edmondson](https://github.com/matt-edmondson))
- docs: correct README, DESCRIPTION and TAGS metadata ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix NU1605 package downgrade on ktsu.Semantics ([@matt-edmondson](https://github.com/matt-edmondson))
- Stop Update SDKs failing when there is nothing to update ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix NU1605 package downgrade: bump ktsu.Semantics.Paths/Strings to 2.9.3 for AppDataStorage compatibility [patch] ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.0.2 (patch)

Changes since v1.0.1:

- Fix NU1605 package downgrade on ktsu.Semantics ([@matt-edmondson](https://github.com/matt-edmondson))
- Stop Update SDKs failing when there is nothing to update ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.0.1 (patch)

Changes since v1.0.0:

- Fix NU1605 package downgrade: bump ktsu.Semantics.Paths/Strings to 2.9.3 for AppDataStorage compatibility [patch] ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.0.0 (major)

- Update copyright headers to reflect 2023-2026 ktsu-dev contributors across all relevant files ([@matt-edmondson](https://github.com/matt-edmondson))
- Add COPYRIGHT.md file with copyright notice ([@matt-edmondson](https://github.com/matt-edmondson))
- Update line endings and add InternalsVisibleTo attribute for test projects ([@matt-edmondson](https://github.com/matt-edmondson))
- Sync global.json ([@KtsuTools](https://github.com/KtsuTools))
- Sync global.json ([@KtsuTools](https://github.com/KtsuTools))
- [patch] Fix build: restore failure, SDK analyzer errors, Spectre.Console API break ([@matt-edmondson](https://github.com/matt-edmondson))
- Update NuGet package versions in Directory.Packages.props ([@matt-edmondson](https://github.com/matt-edmondson))
- refactor(core): remove plaintext CredentialService; add ktsu.CredentialCache ([@Claude](https://github.com/Claude))
- feat(merge): record run history with `ktsu merge-history` + --clear ([@Claude](https://github.com/Claude))
- feat(dedup): port FileDeduplicator as a ktsu dedup branch ([@Claude](https://github.com/Claude))
- feat(merge): add --diff-style flag (side-by-side default, git unified) ([@Claude](https://github.com/Claude))
- feat(repo): wire up --parallel build flag with exit-code aggregation ([@Claude](https://github.com/Claude))
- feat(merge): add saved batch CRUD and --batch invocation (#36) ([@Claude](https://github.com/Claude))
- docs: point users to KtsuBuild for per-repo release automation ([@Claude](https://github.com/Claude))
- Expand ktsu.Semantics.Paths usage across services (#14) ([@Claude](https://github.com/Claude))
- Regenerate TAGS.md with NuGet package tags ([@matt-edmondson](https://github.com/matt-edmondson))
- fix(sync): suppress CA1819 on Filename setting ([@Claude](https://github.com/Claude))
- fix: add missing CA1062 null-checks to strong-path service entry points ([@Claude](https://github.com/Claude))
- fix(monitors): drop using on IntervalAction (not IDisposable) ([@Claude](https://github.com/Claude))
- refactor(monitors): replace polling loops with ktsu.IntervalAction ([@Claude](https://github.com/Claude))
- Copy build pipeline from Semantics ([@matt-edmondson](https://github.com/matt-edmondson))
- feat(sync): multi-filename, opt-in --auto-push, abort push on failed pull (#16) ([@Claude](https://github.com/Claude))
- test: add behavioral test coverage for six core services ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: tighten service contracts, wire Ctrl+C, route Sync via IProcessService ([@matt-edmondson](https://github.com/matt-edmondson))
- chore: remove unused PackageReferences ([@matt-edmondson](https://github.com/matt-edmondson))
- refactor: Convert PushDirectory to asynchronous method and improve error handling ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: Change Path and Filename properties to optional with default values ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: Update Humanizer.Core package reference to exclude analyzers ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix sonar issues ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: Update application name from 'ktools' to 'ktsu' ([@matt-edmondson](https://github.com/matt-edmondson))
- feat: Add SonarLint configuration for connected mode project ([@matt-edmondson](https://github.com/matt-edmondson))
- feat: Enhance RepoService with repository discovery, building, testing, and package management ([@matt-edmondson](https://github.com/matt-edmondson))
- feat: Consolidate ktsu-dev tools into a unified CLI application (ktools) ([@matt-edmondson](https://github.com/matt-edmondson))
- Initial commit ([@matt-edmondson](https://github.com/matt-edmondson))

