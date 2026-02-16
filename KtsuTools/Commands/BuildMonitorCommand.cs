// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.BuildMonitor;
using Spectre.Console.Cli;

public sealed class BuildMonitorCommand(BuildMonitorService buildMonitorService) : AsyncCommand<BuildMonitorCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--owner <OWNER>")]
		[Description("GitHub owner or organization to monitor")]
		public required string Owner { get; init; }

		[CommandOption("--refresh <SECONDS>")]
		[Description("Refresh interval in seconds")]
		[DefaultValue(30)]
		public int RefreshInterval { get; init; }
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

		await buildMonitorService.RunDashboardAsync(settings.Owner, settings.RefreshInterval, cts.Token).ConfigureAwait(false);
		return 0;
	}
}
