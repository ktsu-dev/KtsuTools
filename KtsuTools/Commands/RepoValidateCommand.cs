// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using KtsuTools.Core.UI;
using KtsuTools.Repo;
using Spectre.Console.Cli;

public sealed class RepoValidateCommand(RepoService repoService) : AsyncCommand<RepoValidateCommand.Settings>
{
	private readonly RepoService repoService = repoService;

	public sealed class Settings : CommandSettings
	{
		[CommandOption("--dry-run")]
		[Description("Report stale cached entries without pruning them")]
		[DefaultValue(false)]
		public bool DryRun { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		return await repoService.ValidateCacheAsync(settings.DryRun, scope.Token).ConfigureAwait(false);
	}
}
