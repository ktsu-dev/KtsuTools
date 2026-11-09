// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Machine;

[TestClass]
[DoNotParallelize]
public class MachineMonitorServiceTests
{
	[TestMethod]
	public async Task RunDashboardAsyncReportsWindowsOnlyAndReturnsOneOffWindows()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Inconclusive("The platform guard only applies off Windows.");
		}

		// Off Windows LibreHardwareMonitorLib.dll is absent from the output, so touching Computer throws
		// FileNotFoundException. The guard has to answer before anything loads it.
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
		MachineMonitorService service = new();
		int exit = -1;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await service.RunDashboardAsync(1000, cts.Token).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		StringAssert.Contains(output, "machine-monitor requires Windows");
	}
}
