// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Project;

using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.GitHub;

public class ProjectService(IGitService gitService, IGitHubService gitHubService)
{
	private readonly IGitService gitService = gitService;
	private readonly IGitHubService gitHubService = gitHubService;

	public async Task<int> RunAsync(string owner, CancellationToken ct = default)
	{
		_ = gitService;
		_ = gitHubService;
		_ = owner;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
