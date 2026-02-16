// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.BuildMonitor;

using KtsuTools.Core.Services.GitHub;
using Spectre.Console;

public class BuildMonitorService(IGitHubService gitHubService)
{
	private readonly IGitHubService gitHubService = gitHubService;

	public async Task RunDashboardAsync(string owner, int refreshIntervalSeconds, CancellationToken ct = default)
	{
		_ = gitHubService;
		_ = refreshIntervalSeconds;
		_ = ct;
		AnsiConsole.MarkupLine("[bold blue]Build Monitor Dashboard[/]");
		AnsiConsole.MarkupLine($"Monitoring builds for [green]{owner.EscapeMarkup()}[/]...");
		AnsiConsole.MarkupLine("[yellow]Full implementation pending - will show CI/CD status from GitHub Actions and Azure DevOps[/]");
		await Task.CompletedTask.ConfigureAwait(false);
	}
}
