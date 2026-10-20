## v1.10.14 (patch)

Changes since v1.10.13:

- Run the dedup verbs through a CommandApp in tests (Claude)
- Share one settings type across the dedup verbs (Claude)
- Have the parser enforce every required option so a missing one is named [patch] (Claude)
- Reject negative --padding and a --size below 1 before processing images [patch] (Claude)
- Reject a zero or negative --refresh on the three monitors [patch] (Claude)

## v1.10.13 (patch)

Changes since v1.10.12:

- Use Path.Join for the .git probe in IsRepoNested (Claude)
- Stop the nested-repo check at the scan root in sync [patch] (Claude)

## v1.10.13-pre.1 (prerelease)

Changes since v1.10.12:

- Bump Polyfill from 11.4.3 to 11.4.5 (dependabot[bot])
- Bump the microsoft group with 4 updates (dependabot[bot])

## v1.10.12 (patch)

Changes since v1.10.11:

- Build the empty-file test's paths with Path.Join (Claude)
- Leave empty files out of dedupe [patch] (Claude)

## v1.10.11 (patch)

Changes since v1.10.10:

- Pass svn-migrate's URL, target and authors file to git intact [patch] (matt-edmondson)
- Test a skipped block through InteractiveMerge (Claude)
- Build the new image tests' paths with Path.Join (Claude)
- Keep version 1 when a merge block is skipped [patch] (Claude)
- Write image process output as .png and never overwrite one input with another [patch] (Claude)

## v1.10.11-pre.1 (prerelease)

Changes since v1.10.10:

- Bump MSTest.Sdk from 4.4.1 to 4.5.1 (dependabot[bot])

## v1.10.10 (patch)

Changes since v1.10.9:

- Suppress the unpatched ImageSharp 3.1.12 advisories so restore passes (matt-edmondson)
- Stop a one-segment root namespace vouching for every package beneath it [patch] (Claude)
- Make repo update-packages actually update packages [patch] (Claude)

## v1.10.9 (patch)

Changes since v1.10.8:

- Read a commit author with no recorded name as empty (Claude)
- Read a commit author with no recorded name as empty (Claude)

## v1.10.8 (patch)

Changes since v1.10.7:

- Cover the link-loop path in CanonicalDirectoryOf (Claude)
- Find and commit synced files reached through a symlinked path [patch] (Claude)

## v1.10.8-pre.1 (prerelease)

Changes since v1.10.7:

- Bump Polyfill from 11.4.1 to 11.4.3 (dependabot[bot])

## v1.10.7 (patch)

Changes since v1.10.6:

- Write LF files back with LF on Windows too (Claude)
- Cover skipped non-concrete versions and the migrate-cpm guard in tests (Claude)
- Use Path.Join in the dedup symlink tests (Claude)
- Skip symlinks in dedup so a link never gets its target deleted [patch] (Claude)
- Skip a sync commit when the user has other changes staged [patch] (Claude)
- Leave property, range and floating versions alone in packages update [patch] (Claude)
- Find .slnx solutions and stop dropping sibling repos that share a prefix [patch] (Claude)

## v1.10.6 (patch)

Changes since v1.10.5:

- Move CI onto the shared ci-shared.yml pipeline (Claude)

## v1.10.5 (patch)

Changes since v1.10.4:

- Compare migrate-cpm test layouts independently of line endings (matt-edmondson)
- Fold the fenced-line branch into the non-blank branch in MD012 (matt-edmondson)
- fix: merge into an existing Directory.Packages.props instead of overwriting it [patch] (matt-edmondson)
- fix: crop images to their full content and skip ones with nothing to crop [patch] (matt-edmondson)
- fix: leave fenced code and thematic breaks alone in markdown lint [patch] (matt-edmondson)

## v1.10.5-pre.1 (prerelease)

Changes since v1.10.4:

- Bump Polyfill from 11.4.0 to 11.4.1 (dependabot[bot])

## v1.10.4 (patch)

Changes since v1.10.3:

- Bump the ktsu group with 8 updates (dependabot[bot])

## v1.10.3 (patch)

Changes since v1.10.2:

- Bump Moq from 4.20.72 to 4.21.0 (dependabot[bot])
- Bump the ktsu group with 9 updates (dependabot[bot])

## v1.10.2 (patch)

Changes since v1.10.1:

- Bump the ktsu group with 16 updates (dependabot[bot])

## v1.10.1 (patch)

Changes since v1.10.0:

- Bump the ktsu group with 11 updates (dependabot[bot])
- Bump ktsu.AppDataStorage and 15 others (dependabot[bot])

## v1.10.0 (minor)

Changes since v1.9.0:

- refactor: filter the sync candidates in the sequence, not the body [patch] (matt-edmondson)
- fix: answer for a malformed path instead of throwing out of it [patch] (matt-edmondson)
- fix: push the branch by name so a first push is not refused [patch] (matt-edmondson)
- refactor: project the sync candidates instead of remapping them [patch] (matt-edmondson)
- fix: give sync's commits a committer identity of their own [patch] (matt-edmondson)
- feat: delegate local git operations to ktsu.GitIntegration [minor] (matt-edmondson)

## v1.9.0 (minor)

Changes since v1.8.0:

- Merge remote-tracking branch 'origin/main' into claude/sync-explicit-repos (Claude)
- Merge main into the explicit-repos branch (matt-edmondson)
- sync: deduplicate the scan with Distinct instead of a running set [patch] (matt-edmondson)
- sync: filter the scan candidates with Where [patch] (matt-edmondson)
- sync: split the scan into enumerate, filter and dedupe [patch] (matt-edmondson)
- test(sync): assert the join against a concrete path, not against Combine (matt-edmondson)
- sync: join a directory to a file name instead of combining [patch] (matt-edmondson)
- sync: guard the combined filename and cover the new scan paths [patch] (matt-edmondson)
- sync: accept explicit repo paths and directory exclusions [minor] (matt-edmondson)

## v1.8.0 (minor)

Changes since v1.7.0:

- test(sync): join the temp paths instead of combining them [patch] (matt-edmondson)
- test(sync): drive the sync-config verbs and sync --config [patch] (matt-edmondson)
- test(sync): cover saved configurations and the flag override [patch] (matt-edmondson)
- sync: save named configurations of the inputs flags repeat [minor] (matt-edmondson)

## v1.7.0 (minor)

Changes since v1.6.0:

- test(sync): cover the pull request paths for the quality gate [patch] (matt-edmondson)
- sync: filter the branch switches with Where instead of an inner if [patch] (matt-edmondson)
- sync: open a pull request for each synced branch with --pr [minor] (matt-edmondson)
- test(packages): cover the find-unused report rendering and its verb [patch] (Claude)
- packages: filter the element scans with Where instead of in the loop body [patch] (Claude)
- test(packages): cover the find-unused classification rules [patch] (Claude)
- packages: add a find-unused verb for package references no source uses [minor] (Claude)

## v1.6.1 (patch)

Changes since v1.6.0:

- Bump Polyfill from 11.3.0 to 11.4.0 (dependabot[bot])

## v1.6.0 (minor)

Changes since v1.5.0:

- repo: pick the lfs summary colour with a switch instead of a nested ternary [patch] (Claude)
- test(repo): cover the workspace command shell and the pointer report [patch] (Claude)
- repo: share the workspace-walk and command shell the lfs verb duplicated [patch] (Claude)
- repo: add an lfs install verb to configure Git LFS across a workspace [minor] (Claude)

## v1.5.0 (minor)

Changes since v1.4.0:

- test(sync): derive the path root instead of assuming a leading separator [patch] (matt-edmondson)
- test(sync): map and filter with LINQ instead of accumulating loops [patch] (matt-edmondson)
- test(sync): canonicalise the temp repo paths for macOS [patch] (matt-edmondson)
- test(sync): cover the branch-mode paths for the quality gate [patch] (matt-edmondson)
- test(sync): use Path.Join for the temp repo paths [patch] (matt-edmondson)
- sync: commit onto a dedicated branch with --branch [minor] (matt-edmondson)
- Assert parallel build overlap by concurrency, not wall clock (matt-edmondson)
- Cover the output paths of repo list (matt-edmondson)
- Add a repo list verb for cached repositories and solutions (matt-edmondson)
- Align the ktsu.Semantics.Strings pin with ktsu.Semantics.Paths (matt-edmondson)
- Gate Dependabot auto-merge on CI actually being green (Claude)
- ci: adopt the consolidated .NET workflow [patch] (Claude)

## v1.4.2 (patch)

Changes since v1.4.1:

- Bump MSTest.Sdk from 4.4.0 to 4.4.1 (dependabot[bot])

## v1.4.1 (patch)

Changes since v1.4.0:

- Assert parallel build overlap by concurrency, not wall clock (matt-edmondson)
- Cover the output paths of repo list (matt-edmondson)
- Add a repo list verb for cached repositories and solutions (matt-edmondson)
- Align the ktsu.Semantics.Strings pin with ktsu.Semantics.Paths (matt-edmondson)
- Gate Dependabot auto-merge on CI actually being green (Claude)
- ci: adopt the consolidated .NET workflow [patch] (Claude)

## v1.4.0 (minor)

Changes since v1.3.0:

- Address code quality Path.Combine comments in repo cache tests ([@Copilot](https://github.com/Copilot))
- Add repo validate command to prune stale cache entries ([@Copilot](https://github.com/Copilot))
- Initial plan ([@Copilot](https://github.com/Copilot))

## v1.3.0 (minor)

Changes since v1.2.0:

- test: compare the parsed divergence as a value rather than dereferencing it [patch] (matt-edmondson)
- feat: add ktsu repo fetch for a read-only workspace survey [minor] (matt-edmondson)
- fix: raise ktsu.Semantics.Strings to the version Semantics.Paths requires [patch] (matt-edmondson)

## v1.2.0 (minor)

Changes since v1.1.0:

- Answer S2325 where CA1822 was already answered (Claude)
- Let codegen use the code generator rather than a copy of one (Claude)
- Raise Semantics.Strings to the version Semantics.Paths asks for (Claude)

## v1.1.1 (patch)

Changes since v1.1.0:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.1.0 (minor)

Changes since v1.0.0:

- test: cover the git command line assembly and the git output passthrough [patch] (matt-edmondson)
- refactor: use Path.Join in the git command tests [patch] (matt-edmondson)
- Merge origin/main into fix/align-semantics-versions (matt-edmondson)
- feat: add ktsu git to run a git command across every repository under a directory [minor] (matt-edmondson)
- fix: align ktsu.Semantics with the version AppDataStorage requires [patch] (matt-edmondson)
- ci: make the SonarQube quality gate opt in [patch] (matt-edmondson)
- ci: adopt the unified dotnet workflow [patch] (matt-edmondson)
- chore: store icon.png in LFS as .gitattributes declares (matt-edmondson)
- docs: scope build badge to the default branch (matt-edmondson)
- docs: broaden TAGS.md for better topic coverage (matt-edmondson)
- docs: correct README, DESCRIPTION and TAGS metadata (matt-edmondson)
- Fix NU1605 package downgrade on ktsu.Semantics (matt-edmondson)
- Stop Update SDKs failing when there is nothing to update (matt-edmondson)
- Fix NU1605 package downgrade: bump ktsu.Semantics.Paths/Strings to 2.9.3 for AppDataStorage compatibility [patch] (matt-edmondson)

## v1.0.2 (patch)

Changes since v1.0.1:

- Fix NU1605 package downgrade on ktsu.Semantics (matt-edmondson)
- Stop Update SDKs failing when there is nothing to update (matt-edmondson)

## v1.0.1 (patch)

Changes since v1.0.0:

- Fix NU1605 package downgrade: bump ktsu.Semantics.Paths/Strings to 2.9.3 for AppDataStorage compatibility [patch] (matt-edmondson)

## v1.0.0 (major)

- Update copyright headers to reflect 2023-2026 ktsu-dev contributors across all relevant files (matt-edmondson)
- Add COPYRIGHT.md file with copyright notice (matt-edmondson)
- Sync .github\workflows\dotnet.yml (KtsuTools)
- Update line endings and add InternalsVisibleTo attribute for test projects (matt-edmondson)
- Sync global.json (KtsuTools)
- Sync .github\workflows\dotnet.yml (KtsuTools)
- Sync global.json (KtsuTools)
- [patch] Fix build: restore failure, SDK analyzer errors, Spectre.Console API break (matt-edmondson)
- Update NuGet package versions in Directory.Packages.props (matt-edmondson)
- refactor(core): remove plaintext CredentialService; add ktsu.CredentialCache (Claude)
- feat(merge): record run history with `ktsu merge-history` + --clear (Claude)
- feat(dedup): port FileDeduplicator as a ktsu dedup branch (Claude)
- feat(merge): add --diff-style flag (side-by-side default, git unified) (Claude)
- feat(repo): wire up --parallel build flag with exit-code aggregation (Claude)
- feat(merge): add saved batch CRUD and --batch invocation (#36) (Claude)
- docs: point users to KtsuBuild for per-repo release automation (Claude)
- Expand ktsu.Semantics.Paths usage across services (#14) (Claude)
- Regenerate TAGS.md with NuGet package tags (matt-edmondson)
- fix(sync): suppress CA1819 on Filename setting (Claude)
- fix: add missing CA1062 null-checks to strong-path service entry points (Claude)
- fix(monitors): drop using on IntervalAction (not IDisposable) (Claude)
- refactor(monitors): replace polling loops with ktsu.IntervalAction (Claude)
- Copy build pipeline from Semantics (matt-edmondson)
- feat(sync): multi-filename, opt-in --auto-push, abort push on failed pull (#16) (Claude)
- test: add behavioral test coverage for six core services (matt-edmondson)
- fix: tighten service contracts, wire Ctrl+C, route Sync via IProcessService (matt-edmondson)
- chore: remove unused PackageReferences (matt-edmondson)
- refactor: Convert PushDirectory to asynchronous method and improve error handling (matt-edmondson)
- fix: Change Path and Filename properties to optional with default values (matt-edmondson)
- fix: Update Humanizer.Core package reference to exclude analyzers (matt-edmondson)
- Fix sonar issues (matt-edmondson)
- fix: Update application name from 'ktools' to 'ktsu' (matt-edmondson)
- feat: Add SonarLint configuration for connected mode project (matt-edmondson)
- feat: Enhance RepoService with repository discovery, building, testing, and package management (matt-edmondson)
- feat: Consolidate ktsu-dev tools into a unified CLI application (ktools) (matt-edmondson)
- Initial commit (matt-edmondson)

