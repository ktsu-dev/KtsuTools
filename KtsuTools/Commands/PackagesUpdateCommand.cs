// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Packages;
using Spectre.Console.Cli;

public sealed class PackagesUpdateCommand(PackagesService packagesService) : AsyncCommand<PackagesUpdateCommand.Settings>
{
	private readonly PackagesService packagesService = packagesService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Path to the project or solution")]
		public required string Path { get; init; }

		[CommandOption("--what-if")]
		[Description("Preview changes without applying them")]
		[DefaultValue(false)]
		public bool WhatIf { get; init; }

		[CommandOption("--include-prerelease")]
		[Description("Include prerelease package versions")]
		[DefaultValue(false)]
		public bool IncludePrerelease { get; init; }

		[CommandOption("--source <SOURCE>")]
		[Description("Package source to use")]
		[DefaultValue("nuget")]
		public string Source { get; init; } = "nuget";
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		string fullPath = Path.GetFullPath(settings.Path);
		if (File.Exists(fullPath))
		{
			AbsoluteFilePath filePath = AbsoluteFilePath.Create<AbsoluteFilePath>(fullPath);
			return await packagesService.UpdateAsync(
				filePath,
				settings.WhatIf,
				settings.IncludePrerelease,
				settings.Source,
				scope.Token).ConfigureAwait(false);
		}

		AbsoluteDirectoryPath dirPath = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(fullPath);
		return await packagesService.UpdateAsync(
			dirPath,
			settings.WhatIf,
			settings.IncludePrerelease,
			settings.Source,
			scope.Token).ConfigureAwait(false);
	}
}
