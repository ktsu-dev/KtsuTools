// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Repo;
using Spectre.Console.Cli;

/// <summary>
/// Command that configures Git LFS across every repository in a workspace.
/// </summary>
public sealed class RepoLfsInstallCommand(RepoService repoService) : AsyncCommand<RepoLfsInstallCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	/// <summary>
	/// Settings for the repo lfs install command.
	/// </summary>
	public sealed class Settings : CommandSettings
	{
		/// <summary>
		/// Gets the root directory containing the repositories to configure.
		/// </summary>
		[CommandOption("--path <PATH>")]
		[Description("Root directory containing repositories to configure")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";
	}

	/// <inheritdoc/>
	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		return await repoService.InstallLfsAsync(path, scope.Token).ConfigureAwait(false);
	}
}
