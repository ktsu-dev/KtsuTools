// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Machine;
using Spectre.Console.Cli;

public sealed class MachineMonitorCommand(MachineMonitorService machineMonitorService) : AsyncCommand<MachineMonitorCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--refresh <MILLISECONDS>")]
		[Description("Refresh interval in milliseconds")]
		[DefaultValue(1000)]
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

		return await machineMonitorService.RunDashboardAsync(settings.RefreshInterval, cts.Token).ConfigureAwait(false);
	}
}
