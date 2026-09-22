// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

/// <summary>
/// Drives the real <c>git</c> binary to build and inspect test repositories.
/// </summary>
/// <remarks>
/// The code under test shells out to <c>git</c> through ktsu.GitIntegration, so the fixtures do
/// too. Building them with a second implementation of git's behaviour — which is what a libgit2
/// binding is — would leave the suite able to pass against repositories the subject cannot read.
/// </remarks>
internal static class TestGit
{
	/// <summary>
	/// Runs git in a repository and returns its trimmed standard output.
	/// </summary>
	/// <param name="repoRoot">The working directory to run in.</param>
	/// <param name="arguments">The arguments to pass to git.</param>
	/// <returns>Standard output, with trailing whitespace removed.</returns>
	internal static string Run(string repoRoot, params string[] arguments)
	{
		ProcessStartInfo startInfo = new()
		{
			FileName = "git",
			WorkingDirectory = repoRoot,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		// Identity and signing are configured per invocation so the suite does not depend on, or
		// disturb, whatever the machine running it has set globally.
		foreach (string argument in Identity.Concat(arguments))
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("git could not be started.");

		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();

		return process.ExitCode == 0
			? output.TrimEnd()
			: throw new InvalidOperationException(
				string.Format(
					CultureInfo.InvariantCulture,
					"git {0} failed in {1} with {2}: {3}",
					string.Join(' ', arguments),
					repoRoot,
					process.ExitCode,
					string.IsNullOrWhiteSpace(error) ? output : error));
	}

	private static readonly string[] Identity =
	[
		"-c", "user.name=A Human",
		"-c", "user.email=human@example.test",
		"-c", "commit.gpgsign=false",
		"-c", "core.hooksPath=/dev/null",
	];

	/// <summary>
	/// Creates a repository with a deterministic initial branch name and its own identity.
	/// </summary>
	/// <remarks>
	/// The identity is written into the repository rather than relied upon from the machine,
	/// because a CI runner usually has none and <c>git commit</c> refuses without one. Code under
	/// test that commits through git's configured identity — as <c>GitService.CommitAsync</c> does,
	/// matching what it did through libgit2 — needs the fixture to look like a real checkout on a
	/// developer's machine, which always has one.
	/// </remarks>
	/// <param name="repoRoot">The directory to initialize.</param>
	internal static void Init(string repoRoot)
	{
		_ = Run(repoRoot, "init", "--initial-branch", "main");
		_ = Run(repoRoot, "config", "user.name", "A Human");
		_ = Run(repoRoot, "config", "user.email", "human@example.test");
	}

	/// <summary>
	/// Stages one file and commits it under the given author.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="fileName">The file to stage, relative to the root.</param>
	/// <param name="message">The commit message.</param>
	/// <param name="author">The author name; the email is derived from it.</param>
	/// <returns>The new commit's sha.</returns>
	internal static string Commit(string repoRoot, string fileName, string message, string author)
	{
		_ = Run(repoRoot, "add", "--", fileName);
		_ = Run(repoRoot, "commit", "-m", message, "--author", $"{author} <{author}@example.test>");
		return HeadSha(repoRoot);
	}

	/// <summary>The branch HEAD is on, or an empty string when HEAD is detached.</summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <returns>The branch name.</returns>
	internal static string CurrentBranch(string repoRoot) =>
		Run(repoRoot, "rev-parse", "--abbrev-ref", "HEAD") is string name && name != "HEAD"
			? name
			: string.Empty;

	/// <summary>The sha HEAD points at.</summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <returns>The commit sha.</returns>
	internal static string HeadSha(string repoRoot) => Run(repoRoot, "rev-parse", "HEAD");

	/// <summary>The sha a branch points at.</summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="branchName">The branch to read.</param>
	/// <returns>The commit sha.</returns>
	internal static string TipOf(string repoRoot, string branchName) =>
		Run(repoRoot, "rev-parse", branchName);

	/// <summary>Checks out an existing branch.</summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="branchName">The branch to check out.</param>
	internal static void Checkout(string repoRoot, string branchName) =>
		_ = Run(repoRoot, "checkout", branchName);

	/// <summary>Creates a branch at HEAD and checks it out.</summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="branchName">The branch to create.</param>
	internal static void CheckoutNew(string repoRoot, string branchName) =>
		_ = Run(repoRoot, "checkout", "-b", branchName);

	/// <summary>Adds a remote.</summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <param name="name">The remote name.</param>
	/// <param name="url">The remote URL.</param>
	internal static void AddRemote(string repoRoot, string name, string url) =>
		_ = Run(repoRoot, "remote", "add", name, url);
}
