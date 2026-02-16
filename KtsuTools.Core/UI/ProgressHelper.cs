// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.UI;

using System;
using System.Threading.Tasks;
using Spectre.Console;

public static class ProgressHelper
{
	public static async Task RunWithProgressAsync(Func<ProgressContext, Task> action)
	{
		await AnsiConsole.Progress()
			.AutoRefresh(true)
			.AutoClear(false)
			.HideCompleted(false)
			.Columns(
				new TaskDescriptionColumn(),
				new ProgressBarColumn(),
				new PercentageColumn(),
				new SpinnerColumn())
			.StartAsync(action)
			.ConfigureAwait(false);
	}

	public static async Task<T> RunWithStatusAsync<T>(string status, Func<StatusContext, Task<T>> action)
	{
		return await AnsiConsole.Status()
			.AutoRefresh(true)
			.Spinner(Spinner.Known.Dots)
			.StartAsync(status, action)
			.ConfigureAwait(false);
	}

	public static async Task RunWithStatusAsync(string status, Func<StatusContext, Task> action)
	{
		await AnsiConsole.Status()
			.AutoRefresh(true)
			.Spinner(Spinner.Known.Dots)
			.StartAsync(status, action)
			.ConfigureAwait(false);
	}
}
