// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Repo;

using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;

public class RepoService(IGitService gitService, IProcessService processService)
{
	private readonly IGitService gitService = gitService;
	private readonly IProcessService processService = processService;

	public async Task<IReadOnlyList<string>> DiscoverRepositoriesAsync(string path, CancellationToken ct = default)
	{
		_ = gitService;
		_ = path;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return [];
	}

	public async Task<int> BuildAndTestAsync(string path, bool parallel = false, CancellationToken ct = default)
	{
		_ = processService;
		_ = path;
		_ = parallel;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}

	public async Task<int> PullAllAsync(string path, CancellationToken ct = default)
	{
		_ = gitService;
		_ = path;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}

	public async Task<int> UpdatePackagesAsync(string path, bool includePrerelease = false, CancellationToken ct = default)
	{
		_ = processService;
		_ = path;
		_ = includePrerelease;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
