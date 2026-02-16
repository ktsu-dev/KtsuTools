// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Packages;

using KtsuTools.Core.Services.Process;

public class PackagesService(IProcessService processService)
{
	private readonly IProcessService processService = processService;

	public async Task<int> UpdateAsync(string path, bool whatIf = false, bool includePrerelease = false, string source = "nuget", CancellationToken ct = default)
	{
		_ = processService;
		_ = path;
		_ = whatIf;
		_ = includePrerelease;
		_ = source;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}

	public async Task<int> MigrateToCpmAsync(string path, CancellationToken ct = default)
	{
		_ = processService;
		_ = path;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
