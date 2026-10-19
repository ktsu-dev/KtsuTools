// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System;
using System.Reflection;
using System.Threading.Tasks;
using KtsuTools.Sync;

[TestClass]
public class CliIdentityTests
{
	// Spectre's CommandApp keeps hold of the first console it writes to, so both runs share one capture.
	[TestMethod]
	[DoNotParallelize]
	public async Task VersionAndHelpNameTheBuiltExecutable()
	{
		string expected = typeof(CliIdentity).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

		string output = await ConsoleCapture.CaptureAsync(() =>
		{
			RunCli("--version");
			RunCli("--help");
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		string[] lines = output.Split('\n', 2);
		string version = lines[0];
		string help = lines.Length > 1 ? lines[1] : string.Empty;

		Assert.AreEqual(expected, version.Trim());
		Assert.AreNotEqual("0.1.0", version.Trim(), "The version was a literal that never changed.");
		StringAssert.Contains(help, "ktools build-monitor");
		Assert.IsFalse(help.Contains("ktsu build-monitor", StringComparison.Ordinal), "Examples copied from --help named a command that doesn't exist.");
		Assert.IsFalse(help.Contains("Azure DevOps", StringComparison.Ordinal), "build-monitor only talks to GitHub.");
	}

	[TestMethod]
	public void SyncPullRequestBodyNamesTheExecutable()
	{
		string body = SyncPullRequestOpener.BuildBody([], "sync/shared");

		StringAssert.Contains(body, "`ktools sync`");
	}

	// Runs the real entry point, so the name and version come from the configuration Program builds.
	private static void RunCli(string argument)
	{
		MethodInfo main = typeof(CliIdentity).Assembly.EntryPoint
			?? throw new InvalidOperationException("ktools has no entry point.");

		Assert.AreEqual(0, (int)main.Invoke(null, [new[] { argument }])!, $"ktools {argument} failed.");
	}
}
