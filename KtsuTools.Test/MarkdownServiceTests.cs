// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using ktsu.Semantics.Paths;

using KtsuTools.Markdown;

[TestClass]
public class MarkdownServiceTests
{
	private const string Unlinted = "* item\n";

	// Each of these sits where markdown lint and clean must not reach.
	private static readonly string[] VendoredFiles =
	[
		"node_modules/pkg/README.md",
		"bin/Debug/package.md",
		"obj/notes.md",
		".git/description.md",
		"nested/README.md",
		"nested/docs/guide.md",
		"submodule/README.md",
	];

	private static readonly string[] OwnFiles = ["README.md", "docs/guide.md", ".github/CONTRIBUTING.md"];

	private static readonly string[] OwnFileBesideLinks = ["docs/a.md"];

	private static string CreateTree()
	{
		string root = Path.Combine(Path.GetTempPath(), "ktsu-markdown-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		foreach (string relative in VendoredFiles.Concat(OwnFiles))
		{
			string path = Path.Combine(root, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, Unlinted);
		}

		// A nested repository has a .git directory, and a submodule has a .git file.
		Directory.CreateDirectory(Path.Combine(root, "nested", ".git"));
		File.WriteAllText(Path.Combine(root, "submodule", ".git"), "gitdir: ../.git/modules/submodule\n");
		return root;
	}

	[TestMethod]
	public void FindMarkdownFilesSkipsVendoredBuildAndNestedRepositoryFolders()
	{
		string root = CreateTree();
		try
		{
			List<string> files = MarkdownService.FindMarkdownFiles(root);

			string[] relative = [.. files.Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))];
			CollectionAssert.AreEquivalent(OwnFiles, relative);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task LintAsyncLeavesVendoredFilesByteIdenticalAndLintsTheRepositorysOwn()
	{
		string root = CreateTree();
		try
		{
			_ = await ConsoleCapture.CaptureAsync(() =>
				new MarkdownService().LintAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root), CancellationToken.None)).ConfigureAwait(false);

			foreach (string relative in VendoredFiles)
			{
				Assert.AreEqual(Unlinted, await File.ReadAllTextAsync(Path.Combine(root, relative)).ConfigureAwait(false), relative);
			}

			Assert.AreEqual("- item" + Environment.NewLine, await File.ReadAllTextAsync(Path.Combine(root, "README.md")).ConfigureAwait(false));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// Builds <c>md/docs/a.md</c> with a <c>docs/up -&gt; ..</c> loop and an <c>ext</c> link to a directory
	/// outside the tree that holds <c>o.md</c>.
	/// </summary>
	private static (string Root, string Outside) CreateLinkedTree()
	{
		string root = Path.Combine(Path.GetTempPath(), "ktsu-markdown-" + Guid.NewGuid().ToString("N"));
		string outside = root + "-outside";
		Directory.CreateDirectory(Path.Combine(root, "docs"));
		Directory.CreateDirectory(outside);
		File.WriteAllText(Path.Combine(root, "docs", "a.md"), Unlinted);
		File.WriteAllText(Path.Combine(outside, "o.md"), Unlinted);
		Directory.CreateSymbolicLink(Path.Combine(root, "docs", "up"), "..");
		Directory.CreateSymbolicLink(Path.Combine(root, "ext"), outside);
		return (root, outside);
	}

	[TestMethod]
	public void FindMarkdownFilesDoesNotFollowLinkedDirectories()
	{
		(string root, string outside) = CreateLinkedTree();
		try
		{
			List<string> files = MarkdownService.FindMarkdownFiles(root);

			string[] relative = [.. files.Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))];
			CollectionAssert.AreEqual(OwnFileBesideLinks, relative);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
			Directory.Delete(outside, recursive: true);
		}
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task LintAsyncLeavesFilesBehindALinkOutsideThePathUntouched()
	{
		(string root, string outside) = CreateLinkedTree();
		try
		{
			_ = await ConsoleCapture.CaptureAsync(() =>
				new MarkdownService().LintAsync(AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root), CancellationToken.None)).ConfigureAwait(false);

			Assert.AreEqual(Unlinted, await File.ReadAllTextAsync(Path.Combine(outside, "o.md")).ConfigureAwait(false));
			Assert.AreNotEqual(Unlinted, await File.ReadAllTextAsync(Path.Combine(root, "docs", "a.md")).ConfigureAwait(false));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
			Directory.Delete(outside, recursive: true);
		}
	}
}
