// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.FileExplorer;

using KtsuTools.Core.Services.Settings;

public class FileExplorerService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;

	public async Task<int> RunAsync(string startPath = ".", bool showHidden = false, bool showSizes = true, CancellationToken ct = default)
	{
		_ = settingsService;
		_ = startPath;
		_ = showHidden;
		_ = showSizes;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
