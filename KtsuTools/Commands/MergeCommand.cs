// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
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

		[CommandOption("--batch <NAME>")]
		[Description("Batch name for grouping merge operations")]
		public string? BatchName { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		return await mergeService.RunMergeAsync(
			settings.Directory,
			settings.Filename,
			settings.BatchName,
			CancellationToken.None).ConfigureAwait(false);
	}
}
