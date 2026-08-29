# KtsuTools

> A unified developer tools suite consolidating multiple ktsu-dev utilities into a single CLI application.

[![License](https://img.shields.io/github/license/ktsu-dev/KtsuTools.svg?label=License&logo=nuget)](LICENSE.md)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/KtsuTools?label=Commits&logo=github)](https://github.com/ktsu-dev/KtsuTools/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/KtsuTools?label=Contributors&logo=github)](https://github.com/ktsu-dev/KtsuTools/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/KtsuTools/dotnet.yml?branch=main&label=Build&logo=github)](https://github.com/ktsu-dev/KtsuTools/actions)

## Introduction

KtsuTools collects a number of standalone ktsu-dev utilities behind one `ktools` command with a
consistent user experience powered by [Spectre.Console](https://spectreconsole.net). Instead of
installing and remembering a separate tool per task, each utility becomes a command group under a
single CLI.

The suite is organised as one module per capability, each shipped as its own
`ktsu.KtsuTools.*` package, with `ktsu.KtsuTools.Core` providing the shared command
infrastructure.

## Installation

```bash
git clone https://github.com/ktsu-dev/KtsuTools.git
cd KtsuTools
dotnet build
```

## Usage

```bash
ktools <command> [options]
```

Run `ktools --help` for the full command list, or `ktools <command> --help` for a specific
command's options.

`ktools git` runs one git command in every repository beneath a directory, so the directory
itself doesn't have to be a repository. Repositories are never descended into, so pointing it at
a repository runs the command only there.

```bash
ktools git status                      # every repo under the current directory
ktools git --path c:/dev -- fetch --prune
```

Flags belong after a `--` separator, otherwise `ktools` claims them as its own options.

## Command Groups

| Group | Module | What it does |
| --- | --- | --- |
| `git` | `KtsuTools.Repo` | Run one git command in every repository under a directory |
| `repo` | `KtsuTools.Repo` | Cross-repository git operations — `discover`, `pull`, `list`, `update` |
| `packages` | `KtsuTools.Packages` | NuGet package maintenance — `update-packages`, `migrate-cpm` |
| `dedup` | `KtsuTools.FileDedupe` | Duplicate file detection and removal — `scan`, `dry-run`, `dedupe`, `stats` |
| `merge-batch` | `KtsuTools.Merge` | Iterative multi-version file merging — `merge`, `merge-history` |
| `sync` | `KtsuTools.Sync` | Synchronize shared file contents across directories |
| `markdown` | `KtsuTools.Markdown` | Markdown processing and linting — `lint` |
| `memfrag` | `KtsuTools.MemFrag` | Memory fragmentation analysis |
| `project` | `KtsuTools.Project` | Project and solution operations — `build`, `clean` |
| `codegen` | `KtsuTools.CodeGen` | Code generation utilities |
| `image` | `KtsuTools.Image` | Batch image processing and icon normalization |
| `explorer` | `KtsuTools.FileExplorer` | Interactive file browsing |
| `build-monitor` | `KtsuTools.BuildMonitor` | CI/CD build status monitoring |
| `machine-monitor` | `KtsuTools.Machine` | Local machine resource monitoring |
| `svn-migrate` | `KtsuTools.SvnMigrate` | Guided Subversion to Git migration |

## Related Tools

For per-repo release automation — semver bumps, changelog generation, publishing, and package
manifest emission — see [KtsuBuild](https://github.com/ktsu-dev/KtsuBuild). KtsuTools focuses on
cross-repo orchestration; KtsuBuild handles the inside-one-repo release workflow. The two are
complementary and intentionally not merged.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.
