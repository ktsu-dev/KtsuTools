// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using ktsu.Semantics.Paths;
using KtsuTools.Repo;

public sealed class RepoDiscoverCommand(RepoService repoService) : RepoWorkspaceCommand(repoService)
{
	protected override Task<int> RunAsync(AbsoluteDirectoryPath path, CancellationToken ct) =>
		RepoService.DiscoverAsync(path, ct);
}
