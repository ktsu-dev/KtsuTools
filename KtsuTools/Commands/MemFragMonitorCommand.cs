// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading.Tasks;
using KtsuTools.Core.UI;
using KtsuTools.MemFrag;
using Spectre.Console.Cli;

public sealed class MemFragMonitorCommand(MemFragService memFragService) : AsyncCommand<MemFragMonitorCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--pid <PID>")]
		[Description("Process ID to monitor for memory fragmentation")]
		public required int ProcessId { get; init; }

		[CommandOption("--refresh <MILLISECONDS>")]
		[Description("Refresh interval in milliseconds")]
		[DefaultValue(1000)]
		public int RefreshInterval { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		Ensure.NotNull(settings);

		using CtrlCScope scope = new();
		return await memFragService.MonitorAsync(settings.ProcessId, settings.RefreshInterval, scope.Token).ConfigureAwait(false);
	}
}
