// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Collections.ObjectModel;
using KtsuTools.Sync;

[TestClass]
public class SyncConfigResolverTests
{
	private static SyncConfigEntry SavedConfig() => new()
	{
		Path = "/saved/repos",
		Filenames = [".editorconfig", ".gitignore"],
		AutoPush = true,
		Branch = "sync/saved",
		OpenPullRequest = true,
	};

	[TestMethod]
	public void SavedConfigSuppliesEveryInputWhenNoFlagsAreGiven()
	{
		SyncRunOptions options = SyncConfigResolver.Resolve(SavedConfig(), string.Empty, [], false, string.Empty, false);

		Assert.AreEqual("/saved/repos", options.Path);
		Assert.AreEqual(".editorconfig|.gitignore", string.Join('|', options.Filenames));
		Assert.IsTrue(options.AutoPush);
		Assert.AreEqual("sync/saved", options.Branch);
		Assert.IsTrue(options.OpenPullRequest);
	}

	[TestMethod]
	public void ExplicitPathOverridesTheSavedPath()
	{
		SyncRunOptions options = SyncConfigResolver.Resolve(SavedConfig(), "/flag/repos", [], false, string.Empty, false);

		Assert.AreEqual("/flag/repos", options.Path, "An explicit --path is the whole point of overriding.");
		Assert.AreEqual(".editorconfig|.gitignore", string.Join('|', options.Filenames), "The filenames the run did not name still come from the configuration.");
	}

	[TestMethod]
	public void ExplicitFilenamesReplaceTheSavedListRatherThanAddingToIt()
	{
		SyncRunOptions options = SyncConfigResolver.Resolve(SavedConfig(), string.Empty, ["Directory.Build.props"], false, string.Empty, false);

		Assert.AreEqual("Directory.Build.props", string.Join('|', options.Filenames));
	}

	[TestMethod]
	public void ExplicitBranchOverridesTheSavedBranch()
	{
		SyncRunOptions options = SyncConfigResolver.Resolve(SavedConfig(), string.Empty, [], false, "sync/flag", false);

		Assert.AreEqual("sync/flag", options.Branch);
	}

	[TestMethod]
	public void AWhitespaceFlagDoesNotCountAsAnOverride()
	{
		SyncRunOptions options = SyncConfigResolver.Resolve(SavedConfig(), "   ", [], false, "  ", false);

		Assert.AreEqual("/saved/repos", options.Path);
		Assert.AreEqual("sync/saved", options.Branch);
	}

	[TestMethod]
	public void BooleanFlagsTurnASavedFalseOn()
	{
		SyncConfigEntry saved = SavedConfig() with { AutoPush = false, OpenPullRequest = false };

		SyncRunOptions options = SyncConfigResolver.Resolve(saved, string.Empty, [], true, string.Empty, true);

		Assert.IsTrue(options.AutoPush);
		Assert.IsTrue(options.OpenPullRequest);
	}

	[TestMethod]
	public void WithoutASavedConfigTheFlagsStandAlone()
	{
		SyncRunOptions options = SyncConfigResolver.Resolve(null, "/flag/repos", ["a.txt"], true, "sync/flag", true);

		Assert.AreEqual("/flag/repos", options.Path);
		Assert.AreEqual("a.txt", string.Join('|', options.Filenames));
		Assert.IsTrue(options.AutoPush);
		Assert.AreEqual("sync/flag", options.Branch);
		Assert.IsTrue(options.OpenPullRequest);
	}

	[TestMethod]
	public void WithNeitherFlagsNorAConfigEveryInputIsEmpty()
	{
		SyncRunOptions options = SyncConfigResolver.Resolve(null, null, null, false, null, false);

		Assert.AreEqual(string.Empty, options.Path);
		Assert.AreEqual(0, options.Filenames.Count);
		Assert.IsFalse(options.AutoPush);
		Assert.AreEqual(string.Empty, options.Branch);
		Assert.IsFalse(options.OpenPullRequest);
	}

	[TestMethod]
	public void ASavedConfigWithoutABranchResolvesToTheCheckedOutBranch()
	{
		SyncConfigEntry saved = SavedConfig() with { Branch = null };

		SyncRunOptions options = SyncConfigResolver.Resolve(saved, string.Empty, [], false, string.Empty, false);

		Assert.AreEqual(string.Empty, options.Branch);
	}

	[TestMethod]
	public void CommaSeparatedFilenamesAreExpandedTrimmedAndDeduplicated()
	{
		Collection<string> expanded = SyncConfigResolver.ExpandFilenames([".editorconfig, .gitignore", ".gitignore", "  ", null]);

		Assert.AreEqual(".editorconfig|.gitignore", string.Join('|', expanded));
	}

	[TestMethod]
	public void SavedFilenamesAreExpandedTheSameWayFlagsAre()
	{
		SyncConfigEntry saved = SavedConfig() with { Filenames = [".editorconfig,.gitignore"] };

		SyncRunOptions options = SyncConfigResolver.Resolve(saved, string.Empty, [], false, string.Empty, false);

		Assert.AreEqual(".editorconfig|.gitignore", string.Join('|', options.Filenames));
	}
}
