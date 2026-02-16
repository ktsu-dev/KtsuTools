// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Machine;

using KtsuTools.Core.Services.Settings;

public class MachineMonitorService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;

	public async Task<int> RunDashboardAsync(int refreshIntervalMs = 1000, CancellationToken ct = default)
	{
		_ = settingsService;
		_ = refreshIntervalMs;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
