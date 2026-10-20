// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Commands;
using KtsuTools.FileDedupe;
using KtsuTools.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console.Cli;

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

		int exit = 0;
		string output = await ConsoleCapture.CaptureAsync(async () =>
			exit = await app.RunAsync(args).ConfigureAwait(false)).ConfigureAwait(false);

		return (exit, output);
	}
}
