// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using Spectre.Console;

/// <summary>
/// Redirects stdout and Spectre's console into one buffer so a test can read what a verb
/// actually printed. The console is global, so tests using this do not run in parallel.
/// </summary>
internal static class ConsoleCapture
{
	internal static async Task<string> CaptureAsync(Func<Task> action)
	{
		using StringWriter writer = new();
		IAnsiConsole originalConsole = AnsiConsole.Console;
		TextWriter originalOut = Console.Out;

		try
		{
			Console.SetOut(writer);

			IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
			{
				Ansi = AnsiSupport.No,
				ColorSystem = ColorSystemSupport.NoColors,
				Out = new AnsiConsoleOutput(writer),
			});

			// Without a width the table collapses to an ellipsis, since there is no terminal to measure.
			console.Profile.Width = 200;
			AnsiConsole.Console = console;

			await action().ConfigureAwait(false);
		}
		finally
		{
			AnsiConsole.Console = originalConsole;
			Console.SetOut(originalOut);
		}

		return writer.ToString();
	}
}
