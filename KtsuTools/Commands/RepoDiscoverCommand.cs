// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Repo;
using Spectre.Console.Cli;

public sealed class RepoDiscoverCommand(RepoService repoService) : AsyncCommand<RepoDiscoverCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root directory to scan for repositories")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		await repoService.DiscoverRepositoriesAsync(settings.Path, CancellationToken.None).ConfigureAwait(false);
		return 0;
	}
}
