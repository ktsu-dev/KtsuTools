// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.MemFrag;
using Spectre.Console;
using Spectre.Console.Cli;

public sealed class MemFragScanCommand(MemFragService memFragService) : AsyncCommand<MemFragScanCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--pid <PID>", isRequired: true)]
		[Description("Process ID to scan for memory fragmentation")]
		public required int ProcessId { get; init; }

		public override ValidationResult Validate() =>
			ProcessId > 0
				? ValidationResult.Success()
				: ValidationResult.Error($"--pid must be a process ID greater than zero (got {ProcessId}).");
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		return await memFragService.ScanAsync(settings.ProcessId, cancellationToken).ConfigureAwait(false);
	}
}
