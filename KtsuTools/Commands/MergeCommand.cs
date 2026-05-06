// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Merge;
using Spectre.Console.Cli;

public sealed class MergeCommand(MergeService mergeService) : AsyncCommand<MergeCommand.Settings>
{
	private readonly MergeService mergeService = mergeService;

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<directory>")]
		[Description("Directory containing files to merge")]
		public required string Directory { get; init; }

		[CommandArgument(1, "<filename>")]
		[Description("Filename pattern to merge")]
		public required string Filename { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath directory = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Directory));
		return await mergeService.RunMergeAsync(
			directory,
			settings.Filename,
			scope.Token).ConfigureAwait(false);
	}
}
