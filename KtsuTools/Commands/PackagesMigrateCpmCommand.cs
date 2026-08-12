// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Packages;
using Spectre.Console.Cli;

public sealed class PackagesMigrateCpmCommand(PackagesService packagesService) : AsyncCommand<PackagesMigrateCpmCommand.Settings>
{
	private readonly PackagesService packagesService = packagesService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to the project or solution to migrate")]
		public required string Path { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		return await packagesService.MigrateToCpmAsync(
			path,
			scope.Token).ConfigureAwait(false);
	}
}
