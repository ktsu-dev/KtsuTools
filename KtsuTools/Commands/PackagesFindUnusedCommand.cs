// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Packages;
using Spectre.Console.Cli;

public sealed class PackagesFindUnusedCommand(PackagesService packagesService) : AsyncCommand<PackagesFindUnusedCommand.Settings>
{
	private readonly PackagesService packagesService = packagesService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to the project, solution or workspace to scan")]
		public required string Path { get; init; }

		[CommandOption("--show-build-time")]
		[Description("Also list the references held back as analyzers or build-time-only packages")]
		public bool ShowBuildTime { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		string fullPath = Path.GetFullPath(settings.Path);

		return File.Exists(fullPath)
			? await packagesService.FindUnusedAsync(
				AbsoluteFilePath.Create<AbsoluteFilePath>(fullPath),
				settings.ShowBuildTime,
				scope.Token).ConfigureAwait(false)
			: await packagesService.FindUnusedAsync(
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(fullPath),
				settings.ShowBuildTime,
				scope.Token).ConfigureAwait(false);
	}
}
