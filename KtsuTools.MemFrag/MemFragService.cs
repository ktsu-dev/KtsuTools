// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.MemFrag;

using KtsuTools.Core.Services.Settings;

public class MemFragService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;

	public async Task<int> ScanAsync(int processId, CancellationToken ct = default)
	{
		_ = settingsService;
		_ = processId;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}

	public async Task<int> MonitorAsync(int processId, int refreshIntervalMs = 1000, CancellationToken ct = default)
	{
		_ = settingsService;
		_ = processId;
		_ = refreshIntervalMs;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
