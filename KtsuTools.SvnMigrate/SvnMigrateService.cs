// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.SvnMigrate;

using KtsuTools.Core.Services.Process;

public class SvnMigrateService(IProcessService processService)
{
	private readonly IProcessService processService = processService;

	public async Task<int> MigrateAsync(Uri svnUrl, string targetPath, string? authorsFile = null, bool preserveEmptyDirs = true, CancellationToken ct = default)
	{
		_ = processService;
		_ = svnUrl;
		_ = targetPath;
		_ = authorsFile;
		_ = preserveEmptyDirs;
		_ = ct;
		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}
}
