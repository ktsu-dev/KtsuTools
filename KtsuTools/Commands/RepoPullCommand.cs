// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using ktsu.Semantics.Paths;
using KtsuTools.Repo;

public sealed class RepoPullCommand(RepoService repoService) : RepoWorkspaceCommand(repoService)
{
	protected override async Task<int> RunAsync(AbsoluteDirectoryPath path, CancellationToken ct) =>
		await RepoService.PullAllAsync(path, ct).ConfigureAwait(false);
}
