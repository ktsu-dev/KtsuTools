// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Commands;
using Spectre.Console;

[TestClass]
public class SyncCommandSettingsTests
{
	[TestMethod]
	public void ValidateRejectsPullRequestsWithoutABranch()
	{
		SyncCommand.Settings settings = new() { OpenPullRequest = true, Branch = string.Empty };

		ValidationResult result = settings.Validate();

		Assert.IsFalse(result.Successful, "--pr has nothing to open a pull request from without --branch.");
		StringAssert.Contains(result.Message ?? string.Empty, "--branch");
	}

	[TestMethod]
	public void ValidateRejectsPullRequestsWhenTheBranchIsOnlyWhitespace()
	{
		SyncCommand.Settings settings = new() { OpenPullRequest = true, Branch = "   " };

		Assert.IsFalse(settings.Validate().Successful, "A whitespace branch names no branch.");
	}

	[TestMethod]
	public void ValidateAcceptsPullRequestsWithABranch()
	{
		SyncCommand.Settings settings = new() { OpenPullRequest = true, Branch = "sync/shared" };

		Assert.IsTrue(settings.Validate().Successful);
	}

	[TestMethod]
	public void ValidateAcceptsASyncThatAsksForNoPullRequests()
	{
		SyncCommand.Settings settings = new() { OpenPullRequest = false, Branch = string.Empty };

		Assert.IsTrue(settings.Validate().Successful, "Without --pr the branch is optional, as it always was.");
	}
}
