// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Repo;
using Spectre.Console.Cli;

public sealed class RepoUpdatePackagesCommand(RepoService repoService) : AsyncCommand<RepoUpdatePackagesCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root directory containing repositories")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";

		[CommandOption("--include-prerelease")]
		[Description("Include prerelease package versions")]
		[DefaultValue(false)]
		public bool IncludePrerelease { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		return await repoService.UpdatePackagesAsync(
			settings.Path,
			settings.IncludePrerelease,
			CancellationToken.None).ConfigureAwait(false);
	}
}
