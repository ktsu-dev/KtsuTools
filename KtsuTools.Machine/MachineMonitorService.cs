// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Machine;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Core.Services.Settings;
using LibreHardwareMonitor.Hardware;
using Spectre.Console;
using Spectre.Console.Rendering;

public class MachineMonitorService(ISettingsService settingsService)
{
	public async Task<int> RunDashboardAsync(int refreshIntervalMs = 1000, CancellationToken ct = default)
	{
		_ = settingsService;

		Computer computer = new()
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true,
			IsMotherboardEnabled = true,
			IsStorageEnabled = true,
			IsNetworkEnabled = true,
		};

		try
		{
			computer.Open();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			AnsiConsole.MarkupLine($"[red]Failed to initialize hardware monitoring: {ex.Message.EscapeMarkup()}[/]");
			AnsiConsole.MarkupLine("[yellow]Try running as administrator for full hardware access.[/]");
			return 1;
		}

		try
		{
			AnsiConsole.MarkupLine("[bold blue]Machine Monitor[/]");
			AnsiConsole.MarkupLine("[dim]Press Ctrl+C to exit[/]");
			AnsiConsole.WriteLine();

			IRenderable initial = new Text("Loading hardware sensors...");
			await AnsiConsole.Live(initial)
				.AutoClear(true)
				.StartAsync(async ctx =>
				{
					while (!ct.IsCancellationRequested)
					{
						try
						{
							Table dashboard = BuildDashboard(computer);
							ctx.UpdateTarget(dashboard);
						}
						catch (OperationCanceledException)
						{
							break;
						}

						try
						{
							await Task.Delay(refreshIntervalMs, ct).ConfigureAwait(false);
						}
						catch (OperationCanceledException)
						{
							break;
						}
					}
				}).ConfigureAwait(false);
		}
		finally
		{
			computer.Close();
		}

		return 0;
	}

	private static Table BuildDashboard(Computer computer)
	{
		Table table = new()
		{
			Border = TableBorder.Rounded,
			Title = new TableTitle("[bold blue]Machine Monitor[/]"),
			Caption = new TableTitle($"[dim]Updated: {DateTimeOffset.Now:T}[/]"),
		};

		table.AddColumn("Hardware");
		table.AddColumn("Sensor");
		table.AddColumn("Type");
		table.AddColumn(new TableColumn("Value").RightAligned());
		table.AddColumn(new TableColumn("Min").RightAligned());
		table.AddColumn(new TableColumn("Max").RightAligned());

		if (computer.Hardware.Count == 0)
		{
			table.Caption = new TableTitle("[yellow]No hardware detected. Try running as administrator.[/]");
			return table;
		}

		foreach (IHardware hardware in computer.Hardware)
		{
			hardware.Update();
			AddHardwareSensors(table, hardware, hardware.Name);

			foreach (IHardware subHardware in hardware.SubHardware)
			{
				subHardware.Update();
				AddHardwareSensors(table, subHardware, $"  {subHardware.Name}");
			}
		}

		return table;
	}

	private static void AddHardwareSensors(Table table, IHardware hardware, string label)
	{
		IEnumerable<ISensor> sensors = hardware.Sensors
			.Where(s => s.Value.HasValue)
			.OrderBy(s => s.SensorType)
			.ThenBy(s => s.Name, StringComparer.Ordinal);

		bool firstSensor = true;

		foreach (ISensor sensor in sensors)
		{
			string hardwareLabel = firstSensor
				? $"[bold]{label.EscapeMarkup()}[/]"
				: string.Empty;

			string sensorType = FormatSensorType(sensor.SensorType);
			string value = FormatSensorValue(sensor);
			string min = sensor.Min.HasValue
				? FormatValue(sensor.Min.Value, sensor.SensorType)
				: "[dim]-[/]";
			string max = sensor.Max.HasValue
				? FormatValue(sensor.Max.Value, sensor.SensorType)
				: "[dim]-[/]";

			table.AddRow(hardwareLabel, sensor.Name.EscapeMarkup(), sensorType, value, min, max);
			firstSensor = false;
		}
	}

	private static string FormatSensorType(SensorType type) =>
		type switch
		{
			SensorType.Temperature => "Temp",
			SensorType.Load => "Load",
			SensorType.Clock => "Clock",
			SensorType.Fan => "Fan",
			SensorType.Power => "Power",
			SensorType.Voltage => "Voltage",
			SensorType.Current => "Current",
			SensorType.Data => "Data",
			SensorType.SmallData => "Data",
			SensorType.Throughput => "Throughput",
			SensorType.Control => "Control",
			SensorType.Level => "Level",
			SensorType.Frequency => "Frequency",
			_ => type.ToString(),
		};

	private static string FormatSensorValue(ISensor sensor)
	{
		if (!sensor.Value.HasValue)
		{
			return "[dim]-[/]";
		}

		float val = sensor.Value.Value;
		string formatted = FormatValue(val, sensor.SensorType);

		return sensor.SensorType switch
		{
			SensorType.Temperature => val switch
			{
				> 90 => $"[red]{formatted}[/]",
				> 70 => $"[yellow]{formatted}[/]",
				_ => $"[green]{formatted}[/]",
			},
			SensorType.Load => val switch
			{
				> 90 => $"[red]{formatted}[/]",
				> 70 => $"[yellow]{formatted}[/]",
				_ => $"[green]{formatted}[/]",
			},
			_ => formatted,
		};
	}

	private static string FormatValue(float value, SensorType type) =>
		type switch
		{
			SensorType.Temperature => string.Create(CultureInfo.InvariantCulture, $"{value:N1} C"),
			SensorType.Load => string.Create(CultureInfo.InvariantCulture, $"{value:N1}%"),
			SensorType.Clock => string.Create(CultureInfo.InvariantCulture, $"{value:N0} MHz"),
			SensorType.Fan => string.Create(CultureInfo.InvariantCulture, $"{value:N0} RPM"),
			SensorType.Power => string.Create(CultureInfo.InvariantCulture, $"{value:N1} W"),
			SensorType.Voltage => string.Create(CultureInfo.InvariantCulture, $"{value:N3} V"),
			SensorType.Current => string.Create(CultureInfo.InvariantCulture, $"{value:N2} A"),
			SensorType.Data => string.Create(CultureInfo.InvariantCulture, $"{value:N2} GB"),
			SensorType.SmallData => string.Create(CultureInfo.InvariantCulture, $"{value:N2} MB"),
			SensorType.Throughput => string.Create(CultureInfo.InvariantCulture, $"{value:N1} B/s"),
			SensorType.Control => string.Create(CultureInfo.InvariantCulture, $"{value:N1}%"),
			SensorType.Level => string.Create(CultureInfo.InvariantCulture, $"{value:N1}%"),
			SensorType.Frequency => string.Create(CultureInfo.InvariantCulture, $"{value:N0} Hz"),
			_ => string.Create(CultureInfo.InvariantCulture, $"{value:N2}"),
		};
}
