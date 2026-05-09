// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.MemFrag;

using System;
using System.Diagnostics;
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

		Process process;
		try
		{
			process = Process.GetProcessById(processId);
		}
		catch (ArgumentException)
		{
			AnsiConsole.MarkupLine($"[red]Error: Process with PID {processId} not found.[/]");
			return 1;
		}

		using (process)
		{
			AnsiConsole.MarkupLine($"[bold blue]Memory Fragmentation Scan[/] - PID {processId} ({process.ProcessName.EscapeMarkup()})");
			AnsiConsole.WriteLine();

			try
			{
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
		}

		await Task.CompletedTask.ConfigureAwait(false);
		return 0;
	}

#pragma warning disable CA1822 // Mark members as static - instance method required for DI injection
	public async Task<int> MonitorAsync(int processId, int refreshIntervalMs = 1000, CancellationToken ct = default)
#pragma warning restore CA1822
	{
		Process process;
		try
		{
			process = Process.GetProcessById(processId);
		}
		catch (ArgumentException)
		{
			AnsiConsole.MarkupLine($"[red]Error: Process with PID {processId} not found.[/]");
			return 1;
		}

		using (process)
		{
			AnsiConsole.MarkupLine($"[bold blue]Memory Fragmentation Monitor[/] - PID {processId} ({process.ProcessName.EscapeMarkup()})");
			AnsiConsole.MarkupLine("[dim]Press Ctrl+C to exit[/]");
			AnsiConsole.WriteLine();

			IRenderable initial = new Text("Loading...");
			Process processCapture = process;
			await AnsiConsole.Live(initial)
				.AutoClear(true)
				.StartAsync(async ctx =>
				{
					using CancellationTokenSource exitedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
					TimeSpan interval = TimeSpan.FromMilliseconds(refreshIntervalMs);

					void Tick()
					{
						try
						{
							processCapture.Refresh();
							ctx.UpdateTarget(BuildMemoryTable(processCapture));
						}
						catch (InvalidOperationException)
						{
							ctx.UpdateTarget(new Markup("[red]Process has exited.[/]"));
							exitedCts.Cancel();
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
					}

					ticker.Stop();
				}).ConfigureAwait(false);
		}

		return 0;
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
