// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Commands;
using Spectre.Console;

[TestClass]
public class MonitorCommandSettingsTests
{
	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public void BuildMonitorRejectsARefreshThatIsNotPositive(int refresh)
	{
		BuildMonitorCommand.Settings settings = new() { Owner = "ktsu-dev", RefreshInterval = refresh };

		AssertRejectsRefresh(settings.Validate(), "seconds");
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public void MachineMonitorRejectsARefreshThatIsNotPositive(int refresh)
	{
		MachineMonitorCommand.Settings settings = new() { RefreshInterval = refresh };

		AssertRejectsRefresh(settings.Validate(), "milliseconds");
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public void MemFragMonitorRejectsARefreshThatIsNotPositive(int refresh)
	{
		MemFragMonitorCommand.Settings settings = new() { ProcessId = 1, RefreshInterval = refresh };

		AssertRejectsRefresh(settings.Validate(), "milliseconds");
	}

	[TestMethod]
	public void MonitorsAcceptAPositiveRefresh()
	{
		Assert.IsTrue(new BuildMonitorCommand.Settings { Owner = "ktsu-dev", RefreshInterval = 30 }.Validate().Successful);
		Assert.IsTrue(new MachineMonitorCommand.Settings { RefreshInterval = 1 }.Validate().Successful);
		Assert.IsTrue(new MemFragMonitorCommand.Settings { ProcessId = 1, RefreshInterval = 1000 }.Validate().Successful);
	}

	private static void AssertRejectsRefresh(ValidationResult result, string unit)
	{
		Assert.IsFalse(result.Successful, "A zero or negative --refresh would only fail inside IntervalAction after the first frame.");
		StringAssert.Contains(result.Message ?? string.Empty, "--refresh");
		StringAssert.Contains(result.Message ?? string.Empty, unit);
	}
}
