// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Commands;
using KtsuTools.FileDedupe;
using KtsuTools.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

/// <summary>
/// Runs the <c>dedup</c> verbs through a real <see cref="CommandApp"/>, the way <c>Program</c>
/// wires them, so the shared <see cref="DedupSettings"/> is bound by the parser rather than
/// constructed by the test.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DedupCommandTests : IDisposable
{
	private readonly string root = Path.Join(Path.GetTempPath(), "ktsutools-dedup-" + Guid.NewGuid().ToString("N"));

	public DedupCommandTests()
	{
		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Join(root, "a.txt"), "same");
		File.WriteAllText(Path.Join(root, "longer.txt"), "same");
	}

	public void Dispose()
	{
		if (Directory.Exists(root))
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task ScanListsTheDuplicateGroup()
	{
		(int exit, string output) = await RunAsync("dedup", "scan", "--path", root).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "1 duplicate group(s), 4 wasted byte(s).", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task DryRunNamesTheFileItWouldDropAndKeepsEverything()
	{
		(int exit, string output) = await RunAsync("dedup", "dry-run", "-p", root).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "1 file(s), 4 byte(s) would be reclaimed.", StringComparison.Ordinal);
		Assert.IsTrue(File.Exists(Path.Join(root, "longer.txt")), "A dry run must not delete anything.");
	}

	[TestMethod]
	public async Task StatsSummarizesTheDirectory()
	{
		(int exit, string output) = await RunAsync("dedup", "stats", "--path", root).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "Redundant files", StringComparison.Ordinal);
		StringAssert.Contains(output, ".txt: 2", StringComparison.Ordinal);
	}

	[TestMethod]
	public async Task DedupeBindsItsOwnOptionAlongsideTheSharedPath()
	{
		(int exit, _) = await RunAsync("dedup", "dedupe", "--path", root, "--yes").ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.IsTrue(File.Exists(Path.Join(root, "a.txt")), "The shortest file name is the one kept.");
		Assert.IsFalse(File.Exists(Path.Join(root, "longer.txt")));
	}

	[TestMethod]
	public async Task DedupeExitsNonZeroAndCountsWhatItCouldNotDelete()
	{
		string removal = Path.Join(root, "longer.txt");

		// The plan is made before the prompt, so turning the planned removal into a non-empty
		// directory while the prompt waits makes its deletion fail on every platform.
		(int exit, string output) = await RunAnsweringAsync("y", () =>
		{
			File.Delete(removal);
			Directory.CreateDirectory(removal);
			File.WriteAllText(Path.Join(removal, "inner.txt"), "keep");
		}, "dedup", "dedupe", "--path", root).ConfigureAwait(false);

		Assert.AreEqual(1, exit);
		StringAssert.Contains(output, "Deleted 0 of 1 file(s); 1 could not be deleted.", StringComparison.Ordinal);
		Assert.IsTrue(File.Exists(Path.Join(root, "a.txt")), "The keeper must survive.");
	}

	[TestMethod]
	public async Task DedupeReportsEveryPlannedDeletionWhenAllSucceed()
	{
		(int exit, string output) = await RunAsync("dedup", "dedupe", "--path", root, "--yes").ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.Contains(output, "Deleted 1 file(s).", StringComparison.Ordinal);
	}

	[TestMethod]
	[DataRow("scan")]
	[DataRow("dry-run")]
	[DataRow("stats")]
	[DataRow("dedupe")]
	public async Task EachVerbNamesAMissingPath(string verb)
	{
		CommandRuntimeException exception = await Assert.ThrowsExactlyAsync<CommandRuntimeException>(
			() => RunAsync("dedup", verb)).ConfigureAwait(false);

		Assert.AreEqual($"Command '{verb}' is missing required option 'path'.", exception.Message);
	}

	private static async Task<(int Exit, string Output)> RunAsync(params string[] args)
	{
		CommandApp app = BuildApp();

		int exit = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await app.RunAsync(args).ConfigureAwait(false)).ConfigureAwait(false);

		return (exit, output);
	}

	/// <summary>
	/// Runs a verb against an interactive console that runs <paramref name="beforeAnswer"/> when the
	/// first prompt asks for input, then types <paramref name="answer"/> and Enter.
	/// </summary>
	private static async Task<(int Exit, string Output)> RunAnsweringAsync(string answer, Action beforeAnswer, params string[] args)
	{
		using StringWriter writer = new();
		IAnsiConsole originalConsole = AnsiConsole.Console;

		try
		{
			IAnsiConsole inner = AnsiConsole.Create(new AnsiConsoleSettings
			{
				Ansi = AnsiSupport.No,
				ColorSystem = ColorSystemSupport.NoColors,
				Interactive = InteractionSupport.Yes,
				Out = new AnsiConsoleOutput(writer),
			});
			inner.Profile.Width = 200;
			AnsiConsole.Console = new ScriptedConsole(inner, new ScriptedInput(answer, beforeAnswer));

			int exit = await BuildApp().RunAsync(args).ConfigureAwait(false);
			return (exit, writer.ToString());
		}
		finally
		{
			AnsiConsole.Console = originalConsole;
		}
	}

	private static CommandApp BuildApp()
	{
		ServiceCollection services = new();
		services.AddSingleton<FileDedupeService>();

		CommandApp app = new(new TypeRegistrar(services));
		app.Configure(config =>
		{
			config.PropagateExceptions();
			config.AddBranch("dedup", dedup =>
			{
				dedup.AddCommand<DedupScanCommand>("scan");
				dedup.AddCommand<DedupDryRunCommand>("dry-run");
				dedup.AddCommand<DedupDeleteCommand>("dedupe");
				dedup.AddCommand<DedupStatsCommand>("stats");
			});
		});

		return app;
	}

	private sealed class ScriptedConsole(IAnsiConsole inner, IAnsiConsoleInput input) : IAnsiConsole
	{
		public Profile Profile => inner.Profile;

		public IAnsiConsoleCursor Cursor => inner.Cursor;

		public IAnsiConsoleInput Input => input;

		public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

		public RenderPipeline Pipeline => inner.Pipeline;

		public void Clear(bool home) => inner.Clear(home);

		public void Write(IRenderable renderable) => inner.Write(renderable);

		public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
	}

	private sealed class ScriptedInput(string answer, Action beforeAnswer) : IAnsiConsoleInput
	{
		private readonly Queue<ConsoleKeyInfo> keys = new(
			answer.Select(c => new ConsoleKeyInfo(c, ConsoleKey.NoName, shift: false, alt: false, control: false))
				.Append(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false)));

		private bool started;

		public bool IsKeyAvailable() => keys.Count > 0;

		public ConsoleKeyInfo? ReadKey(bool intercept)
		{
			if (!started)
			{
				started = true;
				beforeAnswer();
			}

			return keys.Count > 0 ? keys.Dequeue() : null;
		}

		public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) =>
			Task.FromResult(ReadKey(intercept));
	}
}
