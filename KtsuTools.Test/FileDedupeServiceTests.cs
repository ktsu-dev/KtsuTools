// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.IO;
using System.Linq;
using ktsu.Semantics.Paths;
using KtsuTools.FileDedupe;

[TestClass]
public class FileDedupeServiceTests
{
	[TestMethod]
	public async Task PlanAsyncMissingDirectoryReturnsEmptyPlan()
	{
		FileDedupeService service = new();
		string missing = Path.Combine(Path.GetTempPath(), $"ktsu_dedup_missing_{Guid.NewGuid():N}");
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(missing);
		DedupePlan plan = await service.PlanAsync(path).ConfigureAwait(false);
		Assert.AreEqual(0, plan.Groups.Count);
		Assert.AreEqual(0, plan.Removals.Count);
	}

	[TestMethod]
	public async Task PlanAsyncGroupsByContentAndPicksShortestFilenameAsKeeper()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_dedup_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			string shortName = Path.Combine(root, "a.txt");
			string mediumName = Path.Combine(root, "aaa.txt");
			string longName = Path.Combine(root, "aaaaa.txt");
			string unique = Path.Combine(root, "b.txt");

			await File.WriteAllTextAsync(shortName, "same").ConfigureAwait(false);
			await File.WriteAllTextAsync(mediumName, "same").ConfigureAwait(false);
			await File.WriteAllTextAsync(longName, "same").ConfigureAwait(false);
			await File.WriteAllTextAsync(unique, "different").ConfigureAwait(false);

			FileDedupeService service = new();
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);
			DedupePlan plan = await service.PlanAsync(path).ConfigureAwait(false);

			Assert.AreEqual(1, plan.Groups.Count, "Only the three duplicates form a group; the unique file is excluded.");
			DuplicateGroup group = plan.Groups[0];
			Assert.AreEqual(3, group.Files.Count);
			Assert.AreEqual(shortName, plan.Keepers.Single(), "Keeper is the shortest filename.");
			CollectionAssert.AreEquivalent(new[] { mediumName, longName }, plan.Removals.ToArray());
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task PlanAsyncIgnoresSingletonGroups()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_dedup_solo_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			await File.WriteAllTextAsync(Path.Combine(root, "x.txt"), "x").ConfigureAwait(false);
			await File.WriteAllTextAsync(Path.Combine(root, "y.txt"), "y").ConfigureAwait(false);

			FileDedupeService service = new();
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);
			DedupePlan plan = await service.PlanAsync(path).ConfigureAwait(false);

			Assert.AreEqual(0, plan.Groups.Count);
			Assert.AreEqual(0, plan.WastedBytes);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task DeleteRedundantRemovesAllButTheKeeper()
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_dedup_del_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			string keeper = Path.Combine(root, "a.txt");
			string dup1 = Path.Combine(root, "aaa.txt");
			string dup2 = Path.Combine(root, "aaaaa.txt");
			await File.WriteAllTextAsync(keeper, "same").ConfigureAwait(false);
			await File.WriteAllTextAsync(dup1, "same").ConfigureAwait(false);
			await File.WriteAllTextAsync(dup2, "same").ConfigureAwait(false);

			FileDedupeService service = new();
			AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root);
			DedupePlan plan = await service.PlanAsync(path).ConfigureAwait(false);

			int deleted = service.DeleteRedundant(plan);

			Assert.AreEqual(2, deleted);
			Assert.IsTrue(File.Exists(keeper));
			Assert.IsFalse(File.Exists(dup1));
			Assert.IsFalse(File.Exists(dup2));
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}
}
