// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Repo;
using Spectre.Console.Cli;

public sealed class RepoFetchCommand(RepoService repoService) : AsyncCommand<RepoFetchCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root directory containing repositories to fetch")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";

		[CommandOption("--sequential")]
		[Description("Fetch repositories one at a time instead of concurrently")]
		[DefaultValue(false)]
		public bool Sequential { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		return await repoService.FetchAllAsync(
			path,
			!settings.Sequential,
			scope.Token).ConfigureAwait(false);
	}
}
