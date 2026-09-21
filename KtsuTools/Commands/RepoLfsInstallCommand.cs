// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using ktsu.Semantics.Paths;
using KtsuTools.Repo;

/// <summary>
/// Command that configures Git LFS across every repository in a workspace.
/// </summary>
/// <param name="repoService">The service that performs the install.</param>
public sealed class RepoLfsInstallCommand(RepoService repoService) : RepoWorkspaceCommand(repoService)
{
	/// <inheritdoc/>
	protected override async Task<int> RunAsync(AbsoluteDirectoryPath path, CancellationToken ct) =>
		await RepoService.InstallLfsAsync(path, ct).ConfigureAwait(false);
}
