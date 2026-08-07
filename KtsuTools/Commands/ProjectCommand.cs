// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.UI;
using KtsuTools.Project;
using Spectre.Console.Cli;

public sealed class ProjectCommand(ProjectService projectService) : AsyncCommand<ProjectCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--owner <OWNER>")]
		[Description("GitHub owner or organization")]
		[DefaultValue("ktsu-dev")]
		public string Owner { get; init; } = "ktsu-dev";
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);

		using CtrlCScope scope = new();
		return await projectService.RunAsync(settings.Owner, scope.Token).ConfigureAwait(false);
	}
}
