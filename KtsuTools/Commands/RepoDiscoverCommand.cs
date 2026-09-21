// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using ktsu.Semantics.Paths;
using KtsuTools.Repo;

public sealed class RepoDiscoverCommand(RepoService repoService) : RepoWorkspaceCommand(repoService)
{
	protected override async Task<int> RunAsync(AbsoluteDirectoryPath path, CancellationToken ct)
	{
		await RepoService.DiscoverRepositoriesAsync(path, ct).ConfigureAwait(false);
		return 0;
	}
}
