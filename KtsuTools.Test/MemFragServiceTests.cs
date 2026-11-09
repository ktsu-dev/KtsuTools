// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Diagnostics;
using KtsuTools.MemFrag;

[TestClass]
public class MemFragServiceTests
{
	/// <summary>Starts a child that idles until it is killed, on every platform the tests run on.</summary>
	private static Process StartIdleChild()
	{
		ProcessStartInfo startInfo = OperatingSystem.IsWindows()
			? new("ping", "-n 60 127.0.0.1")
			: new("sleep", "60");
		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardOutput = true;
		startInfo.CreateNoWindow = true;

		return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the child process.");
	}

	/// <summary>
	/// Captures the console with a height, since the live display crops its table to one and the
	/// captured console has no terminal to measure.
	/// </summary>
	private static Task<string> CaptureLiveAsync(Func<Task> action) =>
		ConsoleCapture.CaptureAsync(() =>
		{
			Spectre.Console.AnsiConsole.Console.Profile.Height = 100;
			return action();
		});

	[TestMethod]
	[DoNotParallelize]
	public async Task MonitorReportsAndFailsWhenTheTargetExits()
	{
		using Process child = StartIdleChild();
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

		int exit = 0;
		string output = await CaptureLiveAsync(async () =>
		{
			Task<int> monitor = new MemFragService().MonitorAsync(child.Id, refreshIntervalMs: 100, timeout.Token);
			await Task.Delay(500).ConfigureAwait(false);
			child.Kill();
			exit = await monitor.ConfigureAwait(false);
		}).ConfigureAwait(false);

		Assert.IsFalse(timeout.IsCancellationRequested, "The monitor should stop on its own when the target exits.");
		Assert.AreEqual(1, exit, output);
		StringAssert.Contains(output, "Process has exited.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task MonitorSucceedsWhenCancelledWhileTheTargetRuns()
	{
		using Process child = StartIdleChild();
		using CancellationTokenSource cancel = new();

		int exit = -1;
		string output;
		try
		{
			output = await CaptureLiveAsync(async () =>
			{
				Task<int> monitor = new MemFragService().MonitorAsync(child.Id, refreshIntervalMs: 100, cancel.Token);
				await Task.Delay(500).ConfigureAwait(false);
				await cancel.CancelAsync().ConfigureAwait(false);
				exit = await monitor.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}
		finally
		{
			child.Kill();
		}

		Assert.AreEqual(0, exit, output);
		Assert.IsFalse(output.Contains("Process has exited.", StringComparison.Ordinal), output);
	}
}
