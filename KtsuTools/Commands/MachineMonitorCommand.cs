// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.UI;
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

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);

		using CtrlCScope scope = new();
		return await machineMonitorService.RunDashboardAsync(settings.RefreshInterval, scope.Token).ConfigureAwait(false);
	}
}
