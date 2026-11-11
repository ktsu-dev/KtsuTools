// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.MemFrag;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ktsu.IntervalAction;
using Spectre.Console;
using Spectre.Console.Rendering;

public class MemFragService
{
#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> ScanAsync(int processId, CancellationToken ct = default)
#pragma warning restore CA1822
	{
		ct.ThrowIfCancellationRequested();

		if (!TryOpenProcess(processId, out Process? process))
		{
			return 1;
		}

		using (process)
		{
			return await ScanProcessAsync(process, ct).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Scans an already-opened process. Every read of the process, the header's name included, sits
	/// inside the exited-process handling, because the target can exit at any point after it was opened.
	/// </summary>
	internal static async Task<int> ScanProcessAsync(Process process, CancellationToken ct = default)
	{
		try
		{
			AnsiConsole.MarkupLine($"[bold blue]Memory Fragmentation Scan[/] - PID {process.Id} ({process.ProcessName.EscapeMarkup()})");
			AnsiConsole.WriteLine();

			Table table = BuildMemoryTable(process);
			ct.ThrowIfCancellationRequested();
			AnsiConsole.Write(table);

			AnsiConsole.WriteLine();
			RenderFragmentationAnalysis(process);
		}
		catch (InvalidOperationException)
		{
			AnsiConsole.MarkupLine("[red]Error: Process has exited.[/]");
			return 1;
		}

		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}

	/// <summary>
	/// Opens the process to inspect. A PID of zero or below is refused here rather than passed to
	/// <see cref="Process.GetProcessById(int)"/>, which on Linux hands back the current process for it.
	/// </summary>
	private static bool TryOpenProcess(int processId, [NotNullWhen(true)] out Process? process)
	{
		process = null;
		if (processId <= 0)
		{
			AnsiConsole.MarkupLine($"[red]Error: {processId} is not a valid process ID; it must be greater than zero.[/]");
			return false;
		}

		try
		{
			process = Process.GetProcessById(processId);
			return true;
		}
		catch (ArgumentException)
		{
			AnsiConsole.MarkupLine($"[red]Error: Process with PID {processId} not found.[/]");
			return false;
		}
	}

#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> MonitorAsync(int processId, int refreshIntervalMs = 1000, CancellationToken ct = default)
#pragma warning restore CA1822
	{
		if (!TryOpenProcess(processId, out Process? process))
		{
			return 1;
		}

		using (process)
		{
			return await MonitorProcessAsync(process, refreshIntervalMs, ct).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Monitors an already-opened process. The header's name read is guarded like every later read,
	/// because the target can exit between being opened and the header being written.
	/// </summary>
	internal static async Task<int> MonitorProcessAsync(Process process, int refreshIntervalMs = 1000, CancellationToken ct = default)
	{
		string processName;
		try
		{
			processName = process.ProcessName;
		}
		catch (InvalidOperationException)
		{
			AnsiConsole.MarkupLine("[red]Error: Process has exited.[/]");
			return 1;
		}

		AnsiConsole.MarkupLine($"[bold blue]Memory Fragmentation Monitor[/] - PID {process.Id} ({processName.EscapeMarkup()})");
		AnsiConsole.MarkupLine("[dim]Press Ctrl+C to exit[/]");
		AnsiConsole.WriteLine();

		IRenderable initial = new Text("Loading...");
		Process processCapture = process;
		bool processExited = false;
		await AnsiConsole.Live(initial)
			.AutoClear(true)
			.StartAsync(async ctx =>
			{
				using CancellationTokenSource exitedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				TimeSpan interval = TimeSpan.FromMilliseconds(refreshIntervalMs);

				void MarkExited()
				{
					Volatile.Write(ref processExited, true);
					exitedCts.Cancel();
				}

				void Tick()
				{
					try
					{
						processCapture.Refresh();

						// Asked explicitly, because reading the memory figures of a process that has
						// exited does not throw on every platform: on macOS it can keep returning
						// figures, so the monitor would never notice the exit.
						if (processCapture.HasExited)
						{
							MarkExited();
							return;
						}

						ctx.UpdateTarget(BuildMemoryTable(processCapture));
					}
					catch (Exception ex) when (IsExitFailure(ex, processCapture))
					{
						MarkExited();
					}
				}

				Tick();

				IntervalAction ticker = IntervalAction.Start(new IntervalActionOptions
				{
					ActionInterval = interval,
					PollingInterval = interval,
					Action = Tick,
				});

				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, exitedCts.Token).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					// Expected: Ctrl+C, or Tick cancelling once the target exits, is how the monitor stops.
				}

				ticker.Stop();
			}).ConfigureAwait(false);

		// The live region clears itself when it ends, so the reason the monitor stopped is
		// written after it, where it stays on screen.
		if (Volatile.Read(ref processExited))
		{
			AnsiConsole.MarkupLine("[red]Error: Process has exited.[/]");
			return 1;
		}

		return 0;
	}

	/// <summary>
	/// Tells whether a failure reading a monitored process means the process has gone. .NET reports
	/// that as <see cref="InvalidOperationException"/>; some platforms raise
	/// <see cref="Win32Exception"/> instead, which only counts when the process has in fact exited.
	/// </summary>
	internal static bool IsExitFailure(Exception exception, Process process) =>
		exception is InvalidOperationException
		|| (exception is Win32Exception && HasExited(process));

	/// <summary>
	/// Reports whether a process has exited, treating a process that can no longer be asked as exited.
	/// </summary>
	internal static bool HasExited(Process process)
	{
		try
		{
			return process.HasExited;
		}
		catch (InvalidOperationException)
		{
			return true;
		}
	}

	private const string DimDash = "[dim]-[/]";

	private static Table BuildMemoryTable(Process process)
	{
		Table table = new()
		{
			Border = TableBorder.Rounded,
			Title = new TableTitle($"[bold]Memory - {process.ProcessName.EscapeMarkup()} (PID {process.Id})[/]"),
			Caption = new TableTitle($"[dim]Updated: {DateTimeOffset.Now:T}[/]"),
		};

		table.AddColumn("Metric");
		table.AddColumn(new TableColumn("Current").RightAligned());
		table.AddColumn(new TableColumn("Peak").RightAligned());

		table.AddRow(
			"Working Set",
			FormatBytes(process.WorkingSet64),
			FormatBytes(process.PeakWorkingSet64));

		table.AddRow(
			"Private Memory",
			FormatBytes(process.PrivateMemorySize64),
			DimDash);

		table.AddRow(
			"Virtual Memory",
			FormatBytes(process.VirtualMemorySize64),
			FormatBytes(process.PeakVirtualMemorySize64));

		table.AddRow(
			"Paged Memory",
			FormatBytes(process.PagedMemorySize64),
			FormatBytes(process.PeakPagedMemorySize64));

		table.AddRow(
			"Paged System Memory",
			FormatBytes(process.PagedSystemMemorySize64),
			DimDash);

		table.AddRow(
			"Non-paged System Memory",
			FormatBytes(process.NonpagedSystemMemorySize64),
			DimDash);

		table.AddEmptyRow();

		table.AddRow(
			"[bold]Threads[/]",
			process.Threads.Count.ToString(CultureInfo.InvariantCulture),
			DimDash);

		table.AddRow(
			"[bold]Handles[/]",
			process.HandleCount.ToString(CultureInfo.InvariantCulture),
			DimDash);

		double fragmentationIndex = CalculateFragmentationIndex(process);
		string fragColor = fragmentationIndex switch
		{
			< 0.3 => "green",
			< 0.6 => "yellow",
			_ => "red",
		};

		table.AddEmptyRow();
		table.AddRow(
			"[bold]Fragmentation Index[/]",
			$"[{fragColor}]{fragmentationIndex:P1}[/]",
			DimDash);

		double efficiency = process.VirtualMemorySize64 > 0
			? (double)process.WorkingSet64 / process.VirtualMemorySize64
			: 0;
		string effColor = efficiency switch
		{
			> 0.5 => "green",
			> 0.2 => "yellow",
			_ => "red",
		};

		table.AddRow(
			"[bold]Memory Efficiency[/]",
			$"[{effColor}]{efficiency:P1}[/]",
			DimDash);

		return table;
	}

	private static void RenderFragmentationAnalysis(Process process)
	{
		double fragmentationIndex = CalculateFragmentationIndex(process);

		Panel panel = new(new Markup(GetFragmentationDescription(fragmentationIndex)))
		{
			Header = new PanelHeader("[bold]Analysis[/]"),
			Border = BoxBorder.Rounded,
		};

		AnsiConsole.Write(panel);
	}

	private static double CalculateFragmentationIndex(Process process)
	{
		if (process.VirtualMemorySize64 == 0)
		{
			return 0;
		}

		double ratio = 1.0 - ((double)process.PrivateMemorySize64 / process.VirtualMemorySize64);
		return Math.Clamp(ratio, 0, 1);
	}

	private static string GetFragmentationDescription(double index) =>
		index switch
		{
			< 0.3 => "[green]Low fragmentation.[/] Memory usage is efficient with minimal wasted address space.",
			< 0.6 => "[yellow]Moderate fragmentation.[/] Some virtual address space is unused. Consider monitoring for growth.",
			< 0.8 => "[red]High fragmentation.[/] Significant virtual address space waste detected. May indicate memory leaks or allocation patterns that fragment the address space.",
			_ => "[red bold]Critical fragmentation.[/] Virtual address space is severely fragmented. This process may experience allocation failures or performance degradation.",
		};

	private static string FormatBytes(long bytes)
	{
		string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
		double value = bytes;
		int suffixIndex = 0;

		while (value >= 1024 && suffixIndex < suffixes.Length - 1)
		{
			value /= 1024;
			suffixIndex++;
		}

		return string.Create(CultureInfo.InvariantCulture, $"{value:N1} {suffixes[suffixIndex]}");
	}
}
