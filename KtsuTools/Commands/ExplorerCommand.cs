// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.FileExplorer;
using Spectre.Console.Cli;

public sealed class ExplorerCommand(FileExplorerService fileExplorerService) : AsyncCommand<ExplorerCommand.Settings>
{
	private readonly FileExplorerService fileExplorerService = fileExplorerService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Starting directory path")]
		[DefaultValue(".")]
		public string StartPath { get; init; } = ".";

		[CommandOption("--show-hidden")]
		[Description("Show hidden files and directories")]
		[DefaultValue(false)]
		public bool ShowHidden { get; init; }

		[CommandOption("--show-sizes")]
		[Description("Show file sizes")]
		[DefaultValue(true)]
		public bool ShowSizes { get; init; } = true;
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath startPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.StartPath));
		return await fileExplorerService.RunAsync(
			startPath,
			settings.ShowHidden,
			settings.ShowSizes,
			scope.Token).ConfigureAwait(false);
	}
}
