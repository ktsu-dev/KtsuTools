// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using ktsu.Semantics.Paths;

using KtsuTools.FileExplorer;

using Spectre.Console;
using Spectre.Console.Testing;

/// <summary>
/// Drives the explorer's prompts through a <see cref="TestConsole"/>, so a label Spectre would
/// read as markup fails here rather than on the first prompt of <c>ktsu explorer</c>.
/// </summary>
[TestClass]
public class FileExplorerPromptTests
{
	private static TestConsole CreateConsole(params ConsoleKey[] keys)
	{
		TestConsole console = new();
		console.Profile.Capabilities.Interactive = true;
		foreach (ConsoleKey key in keys)
		{
			console.Input.PushKey(key);
		}

		console.Input.PushKey(ConsoleKey.Enter);
		return console;
	}

	private static FileSystemEntry Dir(string name) => new()
	{
		Name = name,
		FullPath = Path.Combine(Path.GetTempPath(), name),
		IsDirectory = true,
	};

	[TestMethod]
	public void PromptNavigation_ShowsTheFixedChoicesAsText()
	{
		using TestConsole console = CreateConsole(ConsoleKey.DownArrow, ConsoleKey.DownArrow);

		NavigationChoice choice = new FileExplorerService().PromptNavigation(console, []);

		Assert.AreEqual(NavigationKind.Quit, choice.Kind);
		StringAssert.Contains(console.Output, "[..] Go up");
		StringAssert.Contains(console.Output, "[D] Select drive");
		StringAssert.Contains(console.Output, "[Q] Quit");
	}

	[TestMethod]
	public void PromptNavigation_ShowsABracketedDirectoryNameAsText()
	{
		using TestConsole console = CreateConsole();

		NavigationChoice choice = new FileExplorerService().PromptNavigation(console, [Dir("[old]")]);

		Assert.AreEqual(new NavigationChoice(NavigationKind.Directory, "[old]"), choice);
		StringAssert.Contains(console.Output, "[DIR] [old]");
	}

	[TestMethod]
	public void PromptNavigation_EntersADirectoryNamedLikeTheQuitCommand()
	{
		using TestConsole console = CreateConsole();

		NavigationChoice choice = new FileExplorerService().PromptNavigation(console, [Dir("[Q] Quit")]);

		Assert.AreEqual(NavigationKind.Directory, choice.Kind);
		Assert.AreEqual("[Q] Quit", choice.Name);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task RunAsync_EntersGoesBackPicksADriveGoesUpAndQuits()
	{
		string root = Path.Combine(Path.GetTempPath(), "ktsu-explorer-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(root, "[old]"));
		IAnsiConsole original = AnsiConsole.Console;

		using TestConsole console = new();
		console.Profile.Capabilities.Interactive = true;
		console.Profile.Width = 200;

		// root: [DIR] [old], [..] Go up, [D] Select drive, [Q] Quit
		console.Input.PushKey(ConsoleKey.Enter); // enter [old]

		// [old]: [..] Go up, [<] Back, [D] Select drive, [Q] Quit
		console.Input.PushKey(ConsoleKey.DownArrow);
		console.Input.PushKey(ConsoleKey.Enter); // back to root

		// root again: select drive, then Cancel, which is the last drive choice
		console.Input.PushKey(ConsoleKey.DownArrow);
		console.Input.PushKey(ConsoleKey.DownArrow);
		console.Input.PushKey(ConsoleKey.Enter);
		console.Input.PushKey(ConsoleKey.End);
		console.Input.PushKey(ConsoleKey.Enter);

		// root again: go up, then quit from the parent, where Quit is the last choice
		console.Input.PushKey(ConsoleKey.DownArrow);
		console.Input.PushKey(ConsoleKey.Enter);
		console.Input.PushKey(ConsoleKey.End);
		console.Input.PushKey(ConsoleKey.Enter);

		try
		{
			AnsiConsole.Console = console;
			int exitCode = await new FileExplorerService().RunAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root)).ConfigureAwait(false);

			Assert.AreEqual(0, exitCode);
			StringAssert.Contains(console.Output, "[DIR] [old]");
			StringAssert.Contains(console.Output, "[<] Back");
			StringAssert.Contains(console.Output, "[Cancel]");
			StringAssert.Contains(console.Output, Path.GetFileName(Path.GetDirectoryName(root)!));
		}
		finally
		{
			AnsiConsole.Console = original;
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void PromptDrive_ReturnsTheChosenDrivesRoot()
	{
		using TestConsole console = CreateConsole();

		string? root = FileExplorerService.PromptDrive(console, [new DriveChoice("/", "[root]")]);

		Assert.AreEqual("/", root);
		StringAssert.Contains(console.Output, "/ ([root])");
	}

	[TestMethod]
	public void PromptDrive_ShowsCancelAsTextAndReturnsNullForIt()
	{
		using TestConsole console = CreateConsole(ConsoleKey.DownArrow);

		string? root = FileExplorerService.PromptDrive(console, [new DriveChoice("/", "Local Disk")]);

		Assert.IsNull(root);
		StringAssert.Contains(console.Output, "[Cancel]");
	}
}
