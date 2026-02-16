// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.CodeGen;

using KtsuTools.Core.Services.Settings;

public class CodeGenService(ISettingsService settingsService)
{
	private readonly ISettingsService settingsService = settingsService;

	public async Task<int> GenerateAsync(string inputFile, string language, string? outputFile = null, CancellationToken ct = default)
	{
		_ = settingsService;
		_ = inputFile;
		_ = language;
		_ = outputFile;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
