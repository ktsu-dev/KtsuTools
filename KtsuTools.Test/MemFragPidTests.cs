// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Diagnostics;
using KtsuTools.Commands;
using KtsuTools.MemFrag;
using Spectre.Console;

/// <summary>
/// Pins how <c>memfrag scan</c> and <c>memfrag monitor</c> handle a PID that cannot name a process.
/// On Linux, <see cref="Process.GetProcessById(int)"/> hands back the current process for a PID of
/// zero or below, so without a check ktools reported its own memory as PID 0's. A target that has
/// exited must end in "Process has exited." and exit 1, not an unhandled exception and exit 255.
/// </summary>
[TestClass]
public class MemFragPidTests
{
	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public void ScanRejectsAPidThatIsNotPositive(int pid)
	{
		AssertRejectsPid(new MemFragScanCommand.Settings { ProcessId = pid }.Validate());
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public void MonitorRejectsAPidThatIsNotPositive(int pid)
	{
		AssertRejectsPid(new MemFragMonitorCommand.Settings { ProcessId = pid, RefreshInterval = 1000 }.Validate());
	}

	[TestMethod]
	public void ScanAndMonitorAcceptAPositivePid()
	{
		Assert.IsTrue(new MemFragScanCommand.Settings { ProcessId = 1 }.Validate().Successful);
		Assert.IsTrue(new MemFragMonitorCommand.Settings { ProcessId = 1, RefreshInterval = 1000 }.Validate().Successful);
	}

	[TestMethod]
	[DoNotParallelize]
	[DataRow(0)]
	[DataRow(-1)]
	public async Task ScanServiceRefusesAPidThatIsNotPositiveInsteadOfScanningItself(int pid)
	{
		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await new MemFragService().ScanAsync(pid).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode, output);
		Assert.DoesNotContain("Working Set", output, "A non-positive PID must not produce a memory report.");
	}

	[TestMethod]
	[DoNotParallelize]
	[DataRow(0)]
	[DataRow(-1)]
	public async Task MonitorServiceRefusesAPidThatIsNotPositive(int pid)
	{
		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await new MemFragService().MonitorAsync(pid, 1000).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode, output);
		Assert.DoesNotContain("Memory Fragmentation Monitor", output);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ScanOfAnExitedPidReportsItAndReturnsOne()
	{
		int pid = await StartAndWaitForExitAsync().ConfigureAwait(false);

		int exitCode = 0;
		await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await new MemFragService().ScanAsync(pid).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ScanOfAProcessThatExitedAfterBeingOpenedReportsItAndReturnsOne()
	{
		using Process exited = await StartExitedProcessAsync().ConfigureAwait(false);

		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await MemFragService.ScanProcessAsync(exited).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode, output);
		Assert.Contains("Process has exited.", output);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task MonitorOfAProcessThatExitedAfterBeingOpenedReportsItAndReturnsOne()
	{
		using Process exited = await StartExitedProcessAsync().ConfigureAwait(false);

		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await MemFragService.MonitorProcessAsync(exited, 1000).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode, output);
		Assert.Contains("Process has exited.", output);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ScanOfALiveProcessReportsItsMemoryAndReturnsZero()
	{
		int exitCode = -1;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await new MemFragService().ScanAsync(Environment.ProcessId).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, exitCode, output);
		Assert.Contains($"PID {Environment.ProcessId}", output);
		Assert.Contains("Working Set", output);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task MonitorOfALiveProcessRunsUntilCancelledAndReturnsZero()
	{
		// Spectre's live display needs an ANSI console with a known height to redraw in place, which the
		// plain capture console in ConsoleCapture doesn't provide.
		using StringWriter writer = new();
		IAnsiConsole originalConsole = AnsiConsole.Console;
		IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Ansi = AnsiSupport.Yes,
			ColorSystem = ColorSystemSupport.NoColors,
			Interactive = InteractionSupport.No,
			Out = new AnsiConsoleOutput(writer),
		});
		console.Profile.Width = 200;
		console.Profile.Height = 200;

		int exitCode = -1;
		try
		{
			AnsiConsole.Console = console;
			using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(300));
			exitCode = await new MemFragService().MonitorAsync(Environment.ProcessId, 50, cts.Token).ConfigureAwait(false);
		}
		finally
		{
			AnsiConsole.Console = originalConsole;
		}

		string output = writer.ToString();
		Assert.AreEqual(0, exitCode, output);
		Assert.Contains("Memory Fragmentation Monitor", output);
		Assert.Contains($"PID {Environment.ProcessId}", output);
	}

	private static void AssertRejectsPid(ValidationResult result)
	{
		Assert.IsFalse(result.Successful, "A PID of zero or below cannot name another process.");
		StringAssert.Contains(result.Message ?? string.Empty, "--pid");
	}

	private static async Task<int> StartAndWaitForExitAsync()
	{
		using Process exited = await StartExitedProcessAsync().ConfigureAwait(false);
		return exited.Id;
	}

	/// <summary>
	/// Starts a short-lived child and waits for it to exit, keeping the <see cref="Process"/> object,
	/// which is what <c>memfrag</c> holds when its target exits partway through a command.
	/// </summary>
	private static async Task<Process> StartExitedProcessAsync()
	{
		ProcessStartInfo startInfo = new("dotnet", "--version")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start a child process.");
		await process.WaitForExitAsync().ConfigureAwait(false);
		return process;
	}
}
