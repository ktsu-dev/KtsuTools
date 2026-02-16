// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Repo;
using Spectre.Console.Cli;

public sealed class RepoBuildCommand(RepoService repoService) : AsyncCommand<RepoBuildCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root directory containing repositories to build")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";

		[CommandOption("--parallel")]
		[Description("Build repositories in parallel")]
		[DefaultValue(false)]
		public bool Parallel { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		return await repoService.BuildAndTestAsync(
			settings.Path,
			settings.Parallel,
			CancellationToken.None).ConfigureAwait(false);
	}
}
