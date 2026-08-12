// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services;

using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.GitHub;
using KtsuTools.Core.Services.Process;
using KtsuTools.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
	public static IServiceCollection AddCoreServices(this IServiceCollection services)
	{
		services.AddSingleton<IGitService, GitService>();
		services.AddSingleton<IGitHubService, GitHubService>();
		services.AddSingleton<IProcessService, ProcessService>();
		services.AddSingleton<ISettingsService, SettingsService>();

		return services;
	}
}
