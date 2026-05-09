// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Test;

using KtsuTools.Core.Services.Settings;
using KtsuTools.Merge;
using Moq;

[TestClass]
public class MergeBatchServiceTests
{
	private static (MergeBatchService Service, MergeBatchSettings Store, Mock<ISettingsService> SettingsMock) BuildService()
	{
		MergeBatchSettings store = new();
		Mock<ISettingsService> settings = new();
		settings.Setup(s => s.LoadOrCreate<MergeBatchSettings>()).Returns(store);
		settings.Setup(s => s.SaveAsync(It.IsAny<MergeBatchSettings>())).Returns(Task.CompletedTask);
		MergeBatchService service = new(settings.Object);
		return (service, store, settings);
	}

	[TestMethod]
	public async Task SaveListShowDeleteRoundTrip()
	{
		(MergeBatchService service, MergeBatchSettings store, Mock<ISettingsService> settings) = BuildService();

		MergeBatchEntry entry = new()
		{
			Directory = "/tmp/repos",
			Filename = "*.yml",
			DiffStyle = "side-by-side",
		};

		await service.SaveAsync("ci-yaml", entry).ConfigureAwait(false);

		Assert.AreEqual(1, service.List().Count);
		Assert.IsTrue(service.List().ContainsKey("ci-yaml"));

		MergeBatchEntry? loaded = service.Get("ci-yaml");
		Assert.IsNotNull(loaded);
		Assert.AreEqual("/tmp/repos", loaded.Directory);
		Assert.AreEqual("*.yml", loaded.Filename);
		Assert.AreEqual("side-by-side", loaded.DiffStyle);

		bool removed = await service.DeleteAsync("ci-yaml").ConfigureAwait(false);
		Assert.IsTrue(removed);
		Assert.AreEqual(0, service.List().Count);
		Assert.IsNull(service.Get("ci-yaml"));

		settings.Verify(s => s.SaveAsync(store), Times.Exactly(2));
	}

	[TestMethod]
	public async Task DeleteUnknownReturnsFalseAndDoesNotPersist()
	{
		(MergeBatchService service, _, Mock<ISettingsService> settings) = BuildService();

		bool removed = await service.DeleteAsync("missing").ConfigureAwait(false);

		Assert.IsFalse(removed);
		settings.Verify(s => s.SaveAsync(It.IsAny<MergeBatchSettings>()), Times.Never);
	}

	[TestMethod]
	public void GetUnknownReturnsNull()
	{
		(MergeBatchService service, _, _) = BuildService();
		Assert.IsNull(service.Get("nope"));
	}

	[TestMethod]
	public async Task SaveOverwritesExistingEntry()
	{
		(MergeBatchService service, _, _) = BuildService();

		await service.SaveAsync("name", new MergeBatchEntry { Directory = "a", Filename = "b" }).ConfigureAwait(false);
		await service.SaveAsync("name", new MergeBatchEntry { Directory = "c", Filename = "d", DiffStyle = "git" }).ConfigureAwait(false);

		MergeBatchEntry? loaded = service.Get("name");
		Assert.IsNotNull(loaded);
		Assert.AreEqual("c", loaded.Directory);
		Assert.AreEqual("d", loaded.Filename);
		Assert.AreEqual("git", loaded.DiffStyle);
	}
}
