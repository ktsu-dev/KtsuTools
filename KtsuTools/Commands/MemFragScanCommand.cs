// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.MemFrag;
using Spectre.Console.Cli;

public sealed class MemFragScanCommand(MemFragService memFragService) : AsyncCommand<MemFragScanCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--pid <PID>")]
		[Description("Process ID to scan for memory fragmentation")]
		public required int ProcessId { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		return await memFragService.ScanAsync(settings.ProcessId, cancellationToken).ConfigureAwait(false);
	}
}
