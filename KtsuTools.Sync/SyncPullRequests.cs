// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Sync;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using KtsuTools.Core.Services.GitHub;
using KtsuTools.Core.Services.Process;

using LibGit2Sharp;

using Spectre.Console;

/// <summary>
/// A file the sync copied, and the version it was copied from, so a pull request can say what it
/// carries rather than leaving a reviewer to diff for it.
/// </summary>
/// <param name="FileName">The file that was synced.</param>
/// <param name="SourceHash">Hash of the content every copy was brought to.</param>
/// <param name="SourceDirectory">Directory the winning copy came from, relative to the scan root.</param>
public sealed record SyncedFile(string FileName, string SourceHash, string SourceDirectory);

/// <summary>
/// The owner and repository name of a GitHub remote.
/// </summary>
/// <param name="Owner">The account or organization owning the repository.</param>
/// <param name="Name">The repository name, without the <c>.git</c> suffix.</param>
public sealed record GitHubSlug(string Owner, string Name)
{
	/// <inheritdoc/>
	public override string ToString() => $"{Owner}/{Name}";
}

/// <summary>
/// Opens a pull request for each repository a sync pushed a branch to, preferring the <c>gh</c> CLI
/// because it carries the user's own credentials, and falling back to the GitHub API when it is
/// absent.
/// </summary>
/// <param name="processService">Used to probe for and drive <c>gh</c>.</param>
/// <param name="gitHubService">The API fallback, used when <c>gh</c> is not installed.</param>
public class SyncPullRequestOpener(IProcessService processService, IGitHubService gitHubService)
{
	private readonly IProcessService processService = processService;
	private readonly IGitHubService gitHubService = gitHubService;

	private const string RemoteName = "origin";

	/// <summary>
	/// Opens a pull request in each repository, skipping the ones that are not on GitHub and the
	/// ones that already have an open pull request for this branch.
	/// </summary>
	/// <param name="repoRoots">Working directories of the repositories that were pushed.</param>
	/// <param name="branchName">The branch the sync committed onto.</param>
	/// <param name="baseBranchByRepoRoot">The branch each repository was on before the sync, which is what the pull request targets.</param>
	/// <param name="syncedFiles">The files the sync copied, used to describe the change.</param>
	/// <param name="ct">Cancellation token.</param>
	public async Task OpenAsync(
		IReadOnlyList<string> repoRoots,
		string branchName,
		IReadOnlyDictionary<string, string> baseBranchByRepoRoot,
		IReadOnlyList<SyncedFile> syncedFiles,
		CancellationToken ct = default)
	{
		Ensure.NotNull(repoRoots);
		Ensure.NotNull(baseBranchByRepoRoot);

		if (repoRoots.Count == 0)
		{
			return;
		}

		AnsiConsole.WriteLine();

		bool hasGhCli = await IsGhCliAvailableAsync(ct).ConfigureAwait(false);
		if (!hasGhCli && !await EnsureApiFallbackAsync(ct).ConfigureAwait(false))
		{
			AnsiConsole.MarkupLine("[yellow]Skipping pull requests: the gh CLI is not installed and no GitHub token was found in GH_TOKEN or GITHUB_TOKEN.[/]");
			return;
		}

		string title = BuildTitle(syncedFiles);
		string body = BuildBody(syncedFiles, branchName);

		foreach (string repoRoot in repoRoots)
		{
			ct.ThrowIfCancellationRequested();

			string? remoteUrl = RemoteUrlOf(repoRoot);
			GitHubSlug? slug = ParseGitHubSlug(remoteUrl);
			if (slug is null)
			{
				AnsiConsole.MarkupLine($"[yellow]Not a GitHub remote, no pull request opened:[/] {repoRoot.EscapeMarkup()}");
				continue;
			}

			string baseBranch = baseBranchByRepoRoot.TryGetValue(repoRoot, out string? recorded) ? recorded : string.Empty;
			if (string.IsNullOrEmpty(baseBranch))
			{
				AnsiConsole.MarkupLine($"[yellow]No base branch recorded, no pull request opened:[/] {repoRoot.EscapeMarkup()}");
				continue;
			}

			await OpenForRepoAsync(repoRoot, slug, branchName, baseBranch, title, body, hasGhCli, ct).ConfigureAwait(false);
		}
	}

	private async Task OpenForRepoAsync(
		string repoRoot,
		GitHubSlug slug,
		string branchName,
		string baseBranch,
		string title,
		string body,
		bool useGhCli,
		CancellationToken ct)
	{
		Uri? existing = useGhCli
			? await FindOpenPullRequestWithGhAsync(repoRoot, branchName, ct).ConfigureAwait(false)
			: await gitHubService.FindOpenPullRequestAsync(slug.Owner, slug.Name, branchName, ct).ConfigureAwait(false);

		if (existing is not null)
		{
			AnsiConsole.MarkupLine($"[dim]Pull request already open for {branchName.EscapeMarkup()} in {slug.ToString().EscapeMarkup()}:[/] {existing.ToString().EscapeMarkup()}");
			return;
		}

		Uri? created = useGhCli
			? await CreatePullRequestWithGhAsync(repoRoot, branchName, baseBranch, title, body, ct).ConfigureAwait(false)
			: await gitHubService.CreatePullRequestAsync(slug.Owner, slug.Name, branchName, baseBranch, title, body, ct).ConfigureAwait(false);

		// A null means the attempt was already reported; one repository's rejected pull request is
		// not a reason to abandon the rest of the run.
		if (created is not null)
		{
			AnsiConsole.MarkupLine($"[green]Opened pull request in {slug.ToString().EscapeMarkup()}:[/] {created.ToString().EscapeMarkup()}");
		}
		else if (!useGhCli)
		{
			AnsiConsole.MarkupLine($"[red]GitHub rejected the pull request in:[/] {slug.ToString().EscapeMarkup()}");
		}
	}

	private async Task<bool> IsGhCliAvailableAsync(CancellationToken ct)
	{
		try
		{
			ProcessResult probe = await processService.RunAsync("gh", "--version", null, ct).ConfigureAwait(false);
			return probe.ExitCode == 0;
		}
		catch (System.ComponentModel.Win32Exception)
		{
			// gh is not on PATH at all.
			return false;
		}
	}

	private async Task<bool> EnsureApiFallbackAsync(CancellationToken ct)
	{
		if (gitHubService.IsAuthenticated)
		{
			return true;
		}

		string token = Environment.GetEnvironmentVariable("GH_TOKEN")
			?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
			?? string.Empty;

		if (string.IsNullOrWhiteSpace(token))
		{
			return false;
		}

		await gitHubService.InitializeAsync(token, ct).ConfigureAwait(false);
		return true;
	}

	private async Task<Uri?> FindOpenPullRequestWithGhAsync(string repoRoot, string branchName, CancellationToken ct)
	{
		ProcessResult result = await processService
			.RunAsync("gh", BuildListArguments(branchName), repoRoot, ct)
			.ConfigureAwait(false);

		return result.ExitCode != 0 ? null : ParseFirstPullRequestUrl(string.Join(string.Empty, result.Output));
	}

	private async Task<Uri?> CreatePullRequestWithGhAsync(
		string repoRoot,
		string branchName,
		string baseBranch,
		string title,
		string body,
		CancellationToken ct)
	{
		ProcessResult result = await processService
			.RunAsync("gh", BuildCreateArguments(branchName, baseBranch, title, body), repoRoot, ct)
			.ConfigureAwait(false);

		if (result.ExitCode != 0)
		{
			string message = result.Errors.Count > 0 ? string.Join('\n', result.Errors) : string.Join('\n', result.Output);
			AnsiConsole.MarkupLine($"[red]Could not open a pull request in {repoRoot.EscapeMarkup()}:[/] {message.EscapeMarkup()}");
			return null;
		}

		// gh prints the new pull request's URL on its own line.
		string? printed = result.Output.LastOrDefault(line => line.StartsWith("https://", StringComparison.Ordinal));
		return printed is null ? null : new Uri(printed);
	}

	/// <summary>
	/// Reads the push URL of a repository's <c>origin</c> remote.
	/// </summary>
	/// <param name="repoRoot">The repository working directory.</param>
	/// <returns>The remote URL, or <see langword="null"/> when there is no <c>origin</c>.</returns>
	internal static string? RemoteUrlOf(string repoRoot)
	{
		try
		{
			using Repository repo = new(repoRoot);
			return repo.Network.Remotes[RemoteName]?.Url;
		}
		catch (LibGit2SharpException)
		{
			return null;
		}
	}

	/// <summary>
	/// Reads the owner and repository name out of a GitHub remote URL, in any of the forms git
	/// writes them: HTTPS, SSH shorthand, and the ssh:// scheme, each with or without <c>.git</c>.
	/// </summary>
	/// <param name="remoteUrl">The remote URL to read.</param>
	/// <returns>The slug, or <see langword="null"/> when the remote is not a GitHub one.</returns>
	internal static GitHubSlug? ParseGitHubSlug(string? remoteUrl)
	{
		if (string.IsNullOrWhiteSpace(remoteUrl))
		{
			return null;
		}

		string trimmed = remoteUrl.Trim();

		// git@github.com:owner/repo.git — an scp-style address, which is not a URI at all.
		const string sshPrefix = "git@github.com:";
		string? remainder = trimmed.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase)
			? trimmed[sshPrefix.Length..]
			: PathOfGitHubUri(trimmed);

		if (string.IsNullOrEmpty(remainder))
		{
			return null;
		}

		string[] segments = remainder
			.Trim('/')
			.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (segments.Length != 2)
		{
			return null;
		}

		string name = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
			? segments[1][..^".git".Length]
			: segments[1];

		return string.IsNullOrEmpty(name) ? null : new GitHubSlug(segments[0], name);
	}

	private static string? PathOfGitHubUri(string remoteUrl)
	{
		if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri))
		{
			return null;
		}

		bool isGitHub = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
			|| uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);

		return isGitHub ? uri.AbsolutePath : null;
	}

	/// <summary>
	/// Builds the <c>gh</c> arguments that list the open pull requests for a branch.
	/// </summary>
	/// <param name="branchName">The branch the pull request would be opened from.</param>
	/// <returns>The arguments to pass to gh.</returns>
	internal static string BuildListArguments(string branchName) =>
		$"pr list --head {QuoteArgument(branchName)} --state open --json url";

	/// <summary>
	/// Builds the <c>gh</c> arguments that open a pull request.
	/// </summary>
	/// <param name="branchName">The branch carrying the sync commits.</param>
	/// <param name="baseBranch">The branch to merge into.</param>
	/// <param name="title">The pull request title.</param>
	/// <param name="body">The pull request body.</param>
	/// <returns>The arguments to pass to gh.</returns>
	internal static string BuildCreateArguments(string branchName, string baseBranch, string title, string body) =>
		$"pr create --head {QuoteArgument(branchName)} --base {QuoteArgument(baseBranch)} --title {QuoteArgument(title)} --body {QuoteArgument(body)}";

	/// <summary>
	/// Reads the first URL out of <c>gh pr list --json url</c> output.
	/// </summary>
	/// <param name="json">The JSON gh printed, which is an array of objects carrying a url.</param>
	/// <returns>The first pull request's address, or <see langword="null"/> when the list is empty or unreadable.</returns>
	internal static Uri? ParseFirstPullRequestUrl(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind is not JsonValueKind.Array)
			{
				return null;
			}

			foreach (JsonElement element in document.RootElement.EnumerateArray())
			{
				if (element.TryGetProperty("url", out JsonElement url)
					&& url.ValueKind is JsonValueKind.String
					&& Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? parsed))
				{
					return parsed;
				}
			}
		}
		catch (JsonException)
		{
			return null;
		}

		return null;
	}

	/// <summary>
	/// Builds the pull request title, naming the synced files while staying a readable single line.
	/// </summary>
	/// <param name="syncedFiles">The files the sync copied.</param>
	/// <returns>The title.</returns>
	internal static string BuildTitle(IReadOnlyList<SyncedFile> syncedFiles)
	{
		IReadOnlyList<string> names = DistinctFileNames(syncedFiles);

		return names.Count switch
		{
			0 => "Sync shared files",
			1 => $"Sync {names[0]}",
			2 => $"Sync {names[0]} and {names[1]}",
			_ => $"Sync {names[0]}, {names[1]} and {names.Count - 2} more",
		};
	}

	/// <summary>
	/// Builds the pull request body, which records each synced file and the version it came from so
	/// a reviewer can tell one sync from another without diffing.
	/// </summary>
	/// <param name="syncedFiles">The files the sync copied.</param>
	/// <param name="branchName">The branch the sync committed onto.</param>
	/// <returns>The body.</returns>
	internal static string BuildBody(IReadOnlyList<SyncedFile> syncedFiles, string branchName)
	{
		StringBuilder builder = new();
		_ = builder.AppendLine(CultureInfo.InvariantCulture, $"Opened by `ktsu sync` from branch `{branchName}`.");
		_ = builder.AppendLine();

		if (syncedFiles is null || syncedFiles.Count == 0)
		{
			_ = builder.AppendLine("No file differences were recorded for this sync.");
			return builder.ToString();
		}

		_ = builder.AppendLine("| File | Source version | Synced from |");
		_ = builder.AppendLine("|---|---|---|");

		foreach (SyncedFile file in syncedFiles.OrderBy(f => f.FileName, StringComparer.Ordinal))
		{
			string source = string.IsNullOrEmpty(file.SourceDirectory) ? "(scan root)" : file.SourceDirectory;
			_ = builder.AppendLine(CultureInfo.InvariantCulture, $"| `{file.FileName}` | `{ShortHash(file.SourceHash)}` | `{source}` |");
		}

		return builder.ToString();
	}

	/// <summary>
	/// Shortens a content hash to the leading characters, which is enough to tell two versions
	/// apart in a table without filling the row.
	/// </summary>
	/// <param name="hash">The full hash.</param>
	/// <returns>The shortened hash.</returns>
	internal static string ShortHash(string hash)
	{
		const int shortLength = 12;
		return string.IsNullOrEmpty(hash) || hash.Length <= shortLength ? hash ?? string.Empty : hash[..shortLength];
	}

	private static IReadOnlyList<string> DistinctFileNames(IReadOnlyList<SyncedFile> syncedFiles) =>
		syncedFiles is null
			? []
			: [.. syncedFiles.Select(f => f.FileName).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

	/// <summary>
	/// Quotes an argument for the single command line that <see cref="IProcessService"/> takes,
	/// following the rules .NET uses to split it back apart: a run of backslashes is doubled only
	/// when a quote follows it, and an embedded quote is escaped.
	/// </summary>
	/// <param name="value">The argument value.</param>
	/// <returns>The quoted argument.</returns>
	internal static string QuoteArgument(string value)
	{
		StringBuilder builder = new();
		_ = builder.Append('"');

		string text = value ?? string.Empty;
		int backslashes = 0;

		foreach (char character in text)
		{
			switch (character)
			{
				case '\\':
					backslashes++;
					break;

				case '"':
					_ = builder.Append('\\', (backslashes * 2) + 1).Append('"');
					backslashes = 0;
					break;

				default:
					_ = builder.Append('\\', backslashes).Append(character);
					backslashes = 0;
					break;
			}
		}

		// Trailing backslashes would otherwise escape the closing quote.
		_ = builder.Append('\\', backslashes * 2).Append('"');
		return builder.ToString();
	}
}
