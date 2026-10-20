// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using KtsuTools.SvnMigrate;

/// <summary>
/// Pins how <see cref="SvnMigrateService"/> hands its values to git: each one must arrive as a single,
/// intact argument even when it contains a space, and a clone that leaves nothing behind must end in a
/// clean "Migration failed" rather than an exception from a later step.
/// </summary>
[TestClass]
public class SvnMigrateServiceTests
{
	private const string EncodedSvnUrl = "https://svn.example.com/My%20Project";

	[TestMethod]
	[DoNotParallelize]
	public async Task CloneReceivesTheUrlTargetAndAuthorsFileAsIntactArguments()
	{
		using TempDir root = TempDir.Create();
		string target = Path.Join(root.Root, "migrated repo");
		string authors = Path.Join(root.Root, "My Work", "authors.txt");
		_ = Directory.CreateDirectory(Path.GetDirectoryName(authors)!);
		await File.WriteAllTextAsync(authors, "jsmith = John Smith <john@example.com>").ConfigureAwait(false);
		RecordingGit git = new() { CloneCreatesTarget = true };

		int exitCode = await RunMigrationAsync(git, target, authors).ConfigureAwait(false);

		Assert.AreEqual(0, exitCode);
		IReadOnlyList<string> clone = git.Calls.Single(c => c.Argv.Take(2).SequenceEqual(["svn", "clone"])).Argv;
		CollectionAssert.AreEqual(
			new[] { "svn", "clone", EncodedSvnUrl, target, "--stdlayout", $"--authors-file={authors}", "--preserve-empty-dirs" },
			clone.ToArray());
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ACloneThatLeavesNoRepositoryReportsMigrationFailed()
	{
		using TempDir root = TempDir.Create();
		string target = Path.Join(root.Root, "never created");
		RecordingGit git = new() { CloneCreatesTarget = false };
		int exitCode = -1;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await RunMigrationAsync(git, target, authorsFile: null).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode);
		Assert.Contains("Migration failed", output);
		Assert.IsFalse(git.Calls.Any(c => c.WorkingDirectory is not null), "No step should run inside a repository that does not exist.");
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task AFailingCloneReportsMigrationFailed()
	{
		using TempDir root = TempDir.Create();
		RecordingGit git = new() { CloneExitCode = 128 };
		int exitCode = -1;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await RunMigrationAsync(git, Path.Join(root.Root, "target"), authorsFile: null).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode);
		Assert.Contains("Migration failed", output);
	}

	[TestMethod]
	public async Task ProcessServicePassesAListArgumentWithSpacesIntact()
	{
		using TempDir root = TempDir.Create();
		string repository = Path.Join(root.Root, "a repo with spaces");

		ProcessResult result = await new ProcessService().RunAsync("git", ["init", "-q", repository]).ConfigureAwait(false);

		Assert.AreEqual(0, result.ExitCode, string.Join(Environment.NewLine, result.Errors));
		Assert.IsTrue(Directory.Exists(Path.Join(repository, ".git")));
	}

	[TestMethod]
	[DataRow("plain")]
	[DataRow("with space")]
	[DataRow("")]
	[DataRow("say \"hi\"")]
	[DataRow(@"C:\Users\John Smith\")]
	[DataRow(@"trailing\\")]
	public async Task TheDefaultListOverloadQuotesEachArgumentSoItArrivesIntact(string value)
	{
		using TempDir root = TempDir.Create();
		string config = Path.Join(root.Root, "a config file");
		IProcessService process = new StringOnlyProcessService();

		ProcessResult set = await process.RunAsync("git", ["config", "--file", config, "test.value", value]).ConfigureAwait(false);
		ProcessResult get = await process.RunAsync("git", ["config", "--file", config, "test.value"]).ConfigureAwait(false);

		Assert.AreEqual(0, set.ExitCode, string.Join(Environment.NewLine, set.Errors));
		Assert.AreEqual(value, get.Output.Single());
	}

	private static Task<int> RunMigrationAsync(RecordingGit git, string target, string? authorsFile) =>
		new SvnMigrateService(git).MigrateAsync(
			new Uri(EncodedSvnUrl),
			AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(target),
			authorsFile is null ? null : AbsoluteFilePath.Create<AbsoluteFilePath>(authorsFile));

	/// <summary>
	/// Stands in for git. It records the argv each call would really receive, splitting a flat argument
	/// string on whitespace the way process start-up does, and fails to start in a working directory that
	/// does not exist, as a real process does.
	/// </summary>
	private sealed class RecordingGit : IProcessService
	{
		public bool CloneCreatesTarget { get; init; } = true;

		public int CloneExitCode { get; init; }

		public List<(IReadOnlyList<string> Argv, string? WorkingDirectory)> Calls { get; } = [];

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default) =>
			RunAsync(command, arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), workingDirectory, ct);

		public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken ct = default)
		{
			if (workingDirectory is not null && !Directory.Exists(workingDirectory))
			{
				throw new Win32Exception(267, "The directory name is invalid.");
			}

			Calls.Add((arguments, workingDirectory));

			if (arguments.Take(2).SequenceEqual(["svn", "clone"]))
			{
				if (CloneExitCode != 0)
				{
					return Task.FromResult(new ProcessResult(CloneExitCode, [], ["clone failed"]));
				}

				// Only a rooted target is created, so a clone handed split-up arguments cannot litter the working directory.
				if (CloneCreatesTarget && arguments.Count > 3 && Path.IsPathRooted(arguments[3]))
				{
					_ = Directory.CreateDirectory(arguments[3]);
				}
			}

			return Task.FromResult(new ProcessResult(0, [], []));
		}
	}

	/// <summary>
	/// Runs real processes but implements only the string overloads, so the list overload falls through to
	/// the interface's quoting default.
	/// </summary>
	private sealed class StringOnlyProcessService : IProcessService
	{
		private readonly ProcessService inner = new();

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			inner.RunAsync(command, arguments, workingDirectory, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default) =>
			inner.RunAsync(command, arguments, workingDirectory, environmentVariables, ct);
	}

	private sealed class TempDir : IDisposable
	{
		private TempDir(string root) => Root = root;

		public string Root { get; }

		public static TempDir Create()
		{
			string root = Path.Join(Path.GetTempPath(), $"ktsu_svn_{Guid.NewGuid():N}");
			_ = Directory.CreateDirectory(root);
			return new TempDir(root);
		}

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
				{
					File.SetAttributes(file, FileAttributes.Normal);
				}

				Directory.Delete(Root, recursive: true);
			}
		}
	}
}
