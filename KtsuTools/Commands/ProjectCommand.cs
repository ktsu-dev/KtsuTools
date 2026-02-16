// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
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

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);

		using CancellationTokenSource cts = new();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			cts.Cancel();
		};

		return await projectService.RunAsync(settings.Owner, cts.Token).ConfigureAwait(false);
	}
}
