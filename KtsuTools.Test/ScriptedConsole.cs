// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

/// <summary>
/// An interactive console that answers prompts from a fixed script of key presses, so a test can
/// drive a verb through its selection and confirmation prompts. Running out of keys throws rather
/// than blocking, so a prompt the script did not expect fails the test instead of hanging it.
/// </summary>
internal sealed partial class ScriptedConsole : IAnsiConsole
{
	private readonly IAnsiConsole inner;

	private ScriptedConsole(IAnsiConsole inner, ScriptedInput input)
	{
		this.inner = inner;
		Input = input;
	}

	public Profile Profile => inner.Profile;

	public IAnsiConsoleCursor Cursor => inner.Cursor;

	public IAnsiConsoleInput Input { get; }

	public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

	public RenderPipeline Pipeline => inner.Pipeline;

	public void Clear(bool home) => inner.Clear(home);

	public void Write(IRenderable renderable) => inner.Write(renderable);

	public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);

	/// <summary>
	/// Installs a scripted console for the duration of <paramref name="action"/>.
	/// </summary>
	/// <param name="keys">The key presses to answer prompts with, in order.</param>
	/// <param name="action">The code to run against the scripted console.</param>
	/// <returns>Everything the console printed.</returns>
	internal static async Task<string> RunAsync(IEnumerable<ConsoleKeyInfo> keys, Func<Task> action)
	{
		using StringWriter writer = new();
		IAnsiConsole originalConsole = AnsiConsole.Console;

		try
		{
			IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
			{
				// A selection prompt redraws itself in place, so it refuses to run without ANSI.
				Ansi = AnsiSupport.Yes,
				ColorSystem = ColorSystemSupport.NoColors,
				Interactive = InteractionSupport.Yes,
				Out = new AnsiConsoleOutput(writer),

				// Spectre's default enrichers recognise CI (GitHub Actions and others) and switch
				// interactivity off, which would make every scripted prompt refuse to run there.
				Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
			});

			console.Profile.Capabilities.Interactive = true;
			console.Profile.Capabilities.Ansi = true;

			// With no terminal to measure, the prompt's live display has no height to fit itself into.
			console.Profile.Width = 200;
			console.Profile.Height = 50;
			AnsiConsole.Console = new ScriptedConsole(console, new ScriptedInput(keys));

			await action().ConfigureAwait(false);
		}
		finally
		{
			AnsiConsole.Console = originalConsole;
		}

		// The escape sequences the prompts redraw with are dropped, leaving the text a test reads.
		return AnsiEscape().Replace(writer.ToString(), string.Empty);
	}

	[GeneratedRegex(@"\x1b\[[0-9;?]*[A-Za-z]")]
	private static partial Regex AnsiEscape();

	internal static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, shift: false, alt: false, control: false);

	internal static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.NoName, shift: false, alt: false, control: false);

	private sealed class ScriptedInput(IEnumerable<ConsoleKeyInfo> keys) : IAnsiConsoleInput
	{
		private readonly Queue<ConsoleKeyInfo> keys = new(keys);

		public bool IsKeyAvailable() => keys.Count > 0;

		public ConsoleKeyInfo? ReadKey(bool intercept) =>
			keys.TryDequeue(out ConsoleKeyInfo key)
				? key
				: throw new InvalidOperationException("The console was asked for more input than the test scripted.");

		public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) =>
			Task.FromResult(ReadKey(intercept));
	}
}
