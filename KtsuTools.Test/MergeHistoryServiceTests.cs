// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Test;

using KtsuTools.Core.Services.Settings;
using KtsuTools.Merge;
using Moq;

[TestClass]
public class MergeHistoryServiceTests
{
	private static (MergeHistoryService Service, MergeHistorySettings Store, Mock<ISettingsService> SettingsMock) BuildService()
	{
		MergeHistorySettings store = new();
		Mock<ISettingsService> settings = new();
		settings.Setup(s => s.LoadOrCreate<MergeHistorySettings>()).Returns(store);
		settings.Setup(s => s.SaveAsync(It.IsAny<MergeHistorySettings>())).Returns(Task.CompletedTask);
		MergeHistoryService service = new(settings.Object);
		return (service, store, settings);
	}

	private static MergeHistoryEntry MakeEntry(DateTimeOffset ts, int exit = 0, string? batch = null) => new()
	{
		Timestamp = ts,
		Directory = "/tmp/repos",
		Filename = ".editorconfig",
		DiffStyle = "side-by-side",
		BatchName = batch,
		ExitCode = exit,
	};

	[TestMethod]
	public async Task RecordRoundTripsAndListReturnsMostRecentFirst()
	{
		(MergeHistoryService service, _, _) = BuildService();

		DateTimeOffset t0 = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);
		await service.RecordAsync(MakeEntry(t0)).ConfigureAwait(false);
		await service.RecordAsync(MakeEntry(t0.AddMinutes(1))).ConfigureAwait(false);
		await service.RecordAsync(MakeEntry(t0.AddMinutes(2))).ConfigureAwait(false);

		IReadOnlyList<MergeHistoryEntry> entries = service.List();

		Assert.AreEqual(3, entries.Count);
		Assert.AreEqual(t0.AddMinutes(2), entries[0].Timestamp);
		Assert.AreEqual(t0.AddMinutes(1), entries[1].Timestamp);
		Assert.AreEqual(t0, entries[2].Timestamp);
	}

	[TestMethod]
	public async Task RecordCapsAtMaxEntriesAndEvictsOldest()
	{
		(MergeHistoryService service, MergeHistorySettings store, _) = BuildService();

		DateTimeOffset t0 = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);
		for (int i = 0; i < MergeHistoryService.MaxEntries + 1; i++)
		{
			await service.RecordAsync(MakeEntry(t0.AddSeconds(i))).ConfigureAwait(false);
		}

		Assert.AreEqual(MergeHistoryService.MaxEntries, store.Entries.Count, "Store is capped at MaxEntries.");

		// The original first entry (i=0) should have been evicted.
		Assert.IsFalse(store.Entries.Any(e => e.Timestamp == t0), "Oldest entry was evicted.");
		Assert.IsTrue(store.Entries.Any(e => e.Timestamp == t0.AddSeconds(MergeHistoryService.MaxEntries)),
			"Newest entry is retained.");
	}

	[TestMethod]
	public async Task ClearEmptiesTheStoreAndIsIdempotent()
	{
		(MergeHistoryService service, MergeHistorySettings store, Mock<ISettingsService> settings) = BuildService();

		await service.RecordAsync(MakeEntry(DateTimeOffset.UtcNow)).ConfigureAwait(false);
		Assert.AreEqual(1, store.Entries.Count);

		await service.ClearAsync().ConfigureAwait(false);
		Assert.AreEqual(0, store.Entries.Count);

		// A second clear on an empty store should not re-persist.
		settings.Invocations.Clear();
		await service.ClearAsync().ConfigureAwait(false);
		settings.Verify(s => s.SaveAsync(It.IsAny<MergeHistorySettings>()), Times.Never,
			"Clearing an already-empty store must be a no-op.");
	}

	[TestMethod]
	public async Task RecordIncludesFailedRunsByDefault()
	{
		(MergeHistoryService service, _, _) = BuildService();

		await service.RecordAsync(MakeEntry(DateTimeOffset.UtcNow, exit: 0)).ConfigureAwait(false);
		await service.RecordAsync(MakeEntry(DateTimeOffset.UtcNow, exit: 1)).ConfigureAwait(false);

		IReadOnlyList<MergeHistoryEntry> entries = service.List();
		Assert.AreEqual(2, entries.Count);
		Assert.IsTrue(entries.Any(e => e.ExitCode == 1), "Failures are recorded too (the gate lives at the call site).");
	}
}
