// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Repo;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class RepoListCommand(RepoService repoService) : AsyncCommand<RepoListCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root directory to scan, used only when the cache is empty or --refresh is passed")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";

		[CommandOption("--refresh")]
		[Description("Re-walk the filesystem and rewrite the cache instead of reading it")]
		[DefaultValue(false)]
		public bool Refresh { get; init; }

		[CommandOption("--format <FORMAT>")]
		[Description("Output format: table (default) or json")]
		[DefaultValue("table")]
		public string Format { get; init; } = "table";

		public override ValidationResult Validate() =>
			Enum.TryParse<RepoListFormat>(Format, ignoreCase: true, out _)
				? ValidationResult.Success()
				: ValidationResult.Error($"Unknown format '{Format}'. Use 'table' or 'json'.");
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		RepoListFormat format = Enum.Parse<RepoListFormat>(settings.Format, ignoreCase: true);
		return await repoService.ListAsync(path, settings.Refresh, format, scope.Token).ConfigureAwait(false);
	}
}
