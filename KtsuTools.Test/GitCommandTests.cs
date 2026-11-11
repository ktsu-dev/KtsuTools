// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KtsuTools.Commands;
using KtsuTools.Core.Services.Git;
using KtsuTools.Core.Services.Process;
using KtsuTools.Repo;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Spectre.Console.Cli;

/// <summary>
/// Covers how <see cref="GitCommand"/> reassembles a git command line out of what Spectre gives it.
/// Spectre splits the user's input across three places, and getting that wrong means running a
/// different git command than the one that was typed.
/// </summary>
[TestClass]
public class GitCommandTests
{
	[TestMethod]
	public async Task ExecuteRunsPositionalArgumentsVerbatim()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		int exit = await ExecuteAsync(fake, repo.Root, args: ["status"]).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual(1, fake.Calls.Count);
		StringAssert.EndsWith(fake.Calls[0].Arguments, "status");
	}

	[TestMethod]
	public async Task ExecuteAppendsArgumentsThatFollowedTheSeparator()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		// 'ktsu git -- status --short' puts everything into Raw and leaves the positional list empty.
		int exit = await ExecuteAsync(fake, repo.Root, args: [], raw: ["status", "--short"], parsed: ["--short"]).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.EndsWith(fake.Calls[0].Arguments, "status --short");
	}

	[TestMethod]
	public async Task ExecuteRejectsFlagsThatSpectreClaimedForItself()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		// 'ktsu git status --short' with no separator: Spectre takes --short and Raw stays empty,
		// so the flag would silently vanish from the git command line.
		int exit = await ExecuteAsync(fake, repo.Root, args: ["status"], raw: [], parsed: ["--short"]).ConfigureAwait(false);

		Assert.AreEqual(1, exit, "A flag that cannot be forwarded must fail rather than run a different command.");
		Assert.AreEqual(0, fake.Calls.Count, "Nothing should run when the command line is incomplete.");
	}

	[TestMethod]
	public async Task ExecuteAcceptsFlagsMirroredFromRawIntoParsed()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		// Spectre lists post-separator flags in Parsed as well as Raw. Those are not lost, so
		// treating any non-empty Parsed as an error would reject every valid '--' invocation.
		int exit = await ExecuteAsync(fake, repo.Root, args: [], raw: ["fetch", "--prune"], parsed: ["--prune"]).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.EndsWith(fake.Calls[0].Arguments, "fetch --prune");
	}

	[TestMethod]
	public async Task ExecuteAcceptsValuedFlagWrittenWithEquals()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		// Raw holds '--author=me' while Parsed keys it as '--author', so matching needs to compare
		// against the part before the '='.
		int exit = await ExecuteAsync(fake, repo.Root, args: [], raw: ["log", "--author=me"], parsed: ["--author"]).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		StringAssert.EndsWith(fake.Calls[0].Arguments, "log --author=me");
	}

	[TestMethod]
	public async Task ExecuteWithoutColorNeverPassesTheColourOverride()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		int exit = await ExecuteAsync(fake, repo.Root, args: ["status"], noColor: true).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual("status", fake.Calls[0].Arguments, "--no-color must leave the git command line untouched.");
	}

	[TestMethod]
	public async Task ExecuteRunsInTheRequestedDirectory()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		await ExecuteAsync(fake, repo.Root, args: ["status"]).ConfigureAwait(false);

		Assert.AreEqual(repo.Root, fake.Calls[0].WorkingDirectory);
	}

	[TestMethod]
	[DoNotParallelize]
	[DataRow("log -1", "log", "-1")]
	[DataRow("log -3 -p", "log", "-3", "-p")]
	[DataRow("clean -fdx", "clean", "-fdx")]
	[DataRow("branch -vv", "branch", "-vv")]
	[DataRow("grep -C2 foo", "grep", "-C2", "foo")]
	[DataRow("diff -U5", "diff", "-U5")]
	[DataRow("log \"--format=%an %s\" -1", "log", "--format=%an %s", "-1")]
	public async Task TheRealAppPassesEverythingAfterTheSeparatorToGitVerbatim(string expected, params string[] gitArgs)
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		int exit = await RunAppAsync(fake, ["git", "--path", repo.Root, "--no-color", "--", .. gitArgs]).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual(1, fake.Calls.Count);
		Assert.AreEqual(expected, fake.Calls[0].Arguments);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task TheRealAppKeepsPositionalArgumentsBeforeTheSeparator()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		int exit = await RunAppAsync(fake, ["git", "log", "--path", repo.Root, "--no-color", "--", "-1"]).ConfigureAwait(false);

		Assert.AreEqual(0, exit);
		Assert.AreEqual("log -1", fake.Calls[0].Arguments);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task TheRealAppStillRejectsAFlagWrittenWithoutTheSeparator()
	{
		using TempRepo repo = new();
		RecordingProcessService fake = new();

		int exit = await RunAppAsync(fake, ["git", "status", "--short", "--path", repo.Root]).ConfigureAwait(false);

		Assert.AreEqual(1, exit, "--short without a separator is claimed by Spectre and would be lost.");
		Assert.AreEqual(0, fake.Calls.Count);
	}

	[TestMethod]
	public void SplitHoldsBackEverythingAfterTheFirstSeparator()
	{
		string[] expectedCliArgs = ["git", "--path", "ws"];
		string[] expectedPassThrough = ["log", "--", "file"];

		string[] cliArgs = GitPassThrough.Split(["git", "--path", "ws", "--", "log", "--", "file"], out GitPassThrough passThrough);

		CollectionAssert.AreEqual(expectedCliArgs, cliArgs);
		CollectionAssert.AreEqual(expectedPassThrough, passThrough.Arguments.ToArray(), "A later '--' belongs to git's own pathspec.");
	}

	[TestMethod]
	public void SplitLeavesOtherCommandsAlone()
	{
		string[] args = ["sync", "--path", "ws", "--", "x"];

		string[] cliArgs = GitPassThrough.Split(args, out GitPassThrough passThrough);

		Assert.AreSame(args, cliArgs);
		Assert.AreSame(GitPassThrough.None, passThrough);
	}

	[TestMethod]
	public void SplitLeavesAGitCommandLineWithoutASeparatorAlone()
	{
		string[] args = ["git", "status"];

		string[] cliArgs = GitPassThrough.Split(args, out GitPassThrough passThrough);

		Assert.AreSame(args, cliArgs);
		Assert.AreEqual(0, passThrough.Arguments.Count);
	}

	/// <summary>
	/// Runs a command line through <see cref="Program.Run"/>, the same path the shipped executable
	/// takes, so Spectre's real tokenizing is part of what is tested.
	/// </summary>
	private static async Task<int> RunAppAsync(IProcessService processService, string[] args)
	{
		ServiceCollection services = new();
		services.AddSingleton(new RepoService(new Mock<IGitService>().Object, processService));

		int exit = -1;
		_ = await ConsoleCapture.CaptureAsync(() =>
		{
			exit = Program.Run(args, services);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		return exit;
	}

	private static async Task<int> ExecuteAsync(
		IProcessService processService,
		string path,
		string[] args,
		string[]? raw = null,
		string[]? parsed = null,
		bool noColor = false)
	{
		RepoService repoService = new(new Mock<IGitService>().Object, processService);
		ICommand command = new GitCommand(repoService, GitPassThrough.None);

		GitCommand.Settings settings = new() { Args = args, Path = path, NoColor = noColor };
		CommandContext context = new([], new FakeRemainingArguments(raw ?? [], parsed ?? []), "git", null);

		return await command.ExecuteAsync(context, settings, CancellationToken.None).ConfigureAwait(false);
	}

	private sealed class FakeRemainingArguments(IReadOnlyList<string> raw, IReadOnlyList<string> parsedKeys) : IRemainingArguments
	{
		public IReadOnlyList<string> Raw { get; } = raw;

		public ILookup<string, string?> Parsed { get; } =
			parsedKeys.ToLookup(key => key, _ => (string?)null, StringComparer.Ordinal);
	}

	private sealed class RecordingProcessService : IProcessService
	{
		public List<(string Command, string Arguments, string? WorkingDirectory)> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
		{
			lock (Calls)
			{
				Calls.Add((command, arguments, workingDirectory));
			}

			return Task.FromResult(new ProcessResult(0, [], []));
		}
	}

	private sealed class TempRepo : IDisposable
	{
		public TempRepo()
		{
			Root = Path.Join(Path.GetTempPath(), $"ktsu_cmd_{Guid.NewGuid():N}");
			Directory.CreateDirectory(Path.Join(Root, ".git"));
		}

		public string Root { get; }

		public void Dispose()
		{
			try
			{
				Directory.Delete(Root, recursive: true);
			}
			catch (DirectoryNotFoundException)
			{
				// Already gone.
			}
		}
	}
}
