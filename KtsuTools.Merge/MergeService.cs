// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Merge;

using KtsuTools.Core.Services.Settings;

public class MergeService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;

	public async Task<int> RunMergeAsync(string directory, string filename, string? batchName = null, CancellationToken ct = default)
	{
		_ = settingsService;
		_ = directory;
		_ = filename;
		_ = batchName;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
