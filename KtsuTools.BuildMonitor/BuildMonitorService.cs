// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.KtsuTools.Test")]
// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.BuildMonitor;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using ktsu.IntervalAction;
using KtsuTools.Core.Services.GitHub;
using Octokit;
using Spectre.Console;
using Spectre.Console.Rendering;

public class BuildMonitorService(IGitHubService gitHubService)
{
	private const string DimDash = "[dim]-[/]";

	public async Task RunDashboardAsync(string owner, int refreshIntervalSeconds, CancellationToken ct = default)
	{
		if (!gitHubService.IsAuthenticated)
		{
			string token = AnsiConsole.Prompt(
				new TextPrompt<string>("Enter GitHub personal access token:")
					.Secret());
			await gitHubService.InitializeAsync(token, ct).ConfigureAwait(false);
		}

		AnsiConsole.MarkupLine($"[bold blue]Build Monitor[/] - Monitoring [green]{owner.EscapeMarkup()}[/]");
		AnsiConsole.MarkupLine("[dim]Press Ctrl+C to exit[/]");
		AnsiConsole.WriteLine();

		IRenderable initial = new Text("Loading...");
		await AnsiConsole.Live(initial)
			.AutoClear(true)
			.StartAsync(async ctx =>
			{
				TimeSpan interval = TimeSpan.FromSeconds(refreshIntervalSeconds);

				// Tick must never throw. The first call runs inside Live, where an exception ends the
				// command, and later calls run on IntervalAction, which stops polling after a fault and
				// leaves the last table on screen.
				void Tick()
				{
					try
					{
						ctx.UpdateTarget(RenderDashboardAsync(owner, ct).GetAwaiter().GetResult());
					}
					catch (OperationCanceledException)
					{
					}
				}

				Tick();

				IntervalAction ticker = IntervalAction.Start(new IntervalActionOptions
				{
					ActionInterval = interval,
					PollingInterval = interval,
					Action = Tick,
				});

				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
				}

				ticker.Stop();
			}).ConfigureAwait(false);
	}

	/// <summary>
	/// Renders the dashboard, or the reason it could not be fetched. GitHub errors are shown rather
	/// than thrown, so a refresh that fails is retried on the next tick.
	/// </summary>
	/// <param name="owner">The user or organization whose repositories are shown.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The dashboard table, or a message describing the error.</returns>
	internal async Task<IRenderable> RenderDashboardAsync(string owner, CancellationToken ct)
	{
		try
		{
			return await BuildDashboardTableAsync(owner, ct).ConfigureAwait(false);
		}
		catch (RateLimitExceededException ex)
		{
			return new Markup($"[red]GitHub rate limit exceeded ({ex.Limit} requests/hour). Resets {ex.Reset.Humanize().EscapeMarkup()}; retrying on the next refresh.[/]");
		}
		catch (AuthorizationException ex)
		{
			return new Markup($"[red]GitHub rejected the token ({(int)ex.StatusCode}): {ex.Message.EscapeMarkup()}[/]");
		}
		catch (ApiException ex)
		{
			return new Markup($"[red]API Error ({(int)ex.StatusCode}): {ex.Message.EscapeMarkup()}[/]");
		}
		catch (HttpRequestException ex)
		{
			return new Markup($"[red]API Error: {ex.Message.EscapeMarkup()}[/]");
		}
	}

	private async Task<Table> BuildDashboardTableAsync(string owner, CancellationToken ct)
	{
		IReadOnlyList<GitHubRepository> repos = await gitHubService.GetRepositoriesAsync(owner, ct).ConfigureAwait(false);
		GitHubRateLimitInfo rateLimit = await gitHubService.GetRateLimitAsync(ct).ConfigureAwait(false);

		Table table = new()
		{
			Border = TableBorder.Rounded,
			Title = new TableTitle($"[bold blue]Build Monitor[/] - {owner.EscapeMarkup()} ({repos.Count} repos)"),
			Caption = new TableTitle($"[dim]Rate Limit: {rateLimit.Remaining}/{rateLimit.Limit} | Resets: {rateLimit.ResetAt.Humanize()}[/]"),
		};

		table.AddColumn("Repository");
		table.AddColumn("Workflow");
		table.AddColumn("Status");
		table.AddColumn("Branch");
		table.AddColumn("Started");
		table.AddColumn("Duration");

		foreach (GitHubRepository repo in repos)
		{
			try
			{
				IReadOnlyList<GitHubWorkflowRun> runs = await gitHubService.GetWorkflowRunsAsync(owner, repo.Name, ct).ConfigureAwait(false);

				if (runs.Count == 0)
				{
					table.AddRow(
						$"[bold]{repo.Name.EscapeMarkup()}[/]",
						"[dim]No workflows[/]",
						DimDash,
						DimDash,
						DimDash,
						DimDash);
					continue;
				}

				GitHubWorkflowRun latestRun = runs[0];
				string statusMarkup = FormatStatus(latestRun.Status, latestRun.Conclusion);
				string duration = latestRun.CompletedAt.HasValue
					? (latestRun.CompletedAt.Value - latestRun.CreatedAt).Humanize(2)
					: "[blue]In progress...[/]";

				table.AddRow(
					$"[bold]{repo.Name.EscapeMarkup()}[/]",
					latestRun.Name.EscapeMarkup(),
					statusMarkup,
					latestRun.Branch.EscapeMarkup(),
					latestRun.CreatedAt.Humanize(),
					duration);
			}
			catch (RateLimitExceededException)
			{
				// Every remaining repository would fail the same way, so the whole refresh is
				// reported as rate limited instead of a table of errors.
				throw;
			}
			catch (Exception ex) when (ex is ApiException or HttpRequestException)
			{
				table.AddRow(
					$"[bold]{repo.Name.EscapeMarkup()}[/]",
					"[red]Error[/]",
					DimDash,
					DimDash,
					DimDash,
					DimDash);
			}
		}

		return table;
	}

	private static string FormatStatus(string status, string conclusion)
	{
		if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrEmpty(conclusion))
		{
			return conclusion.ToUpperInvariant() switch
			{
				"SUCCESS" => "[green]Pass[/]",
				"FAILURE" => "[red]Fail[/]",
				"CANCELLED" => "[yellow]Cancelled[/]",
				"SKIPPED" => "[dim]Skipped[/]",
				_ => $"[dim]{conclusion.EscapeMarkup()}[/]",
			};
		}

		return status.ToUpperInvariant() switch
		{
			"IN_PROGRESS" => "[blue]Running[/]",
			"QUEUED" => "[yellow]Queued[/]",
			"WAITING" => "[yellow]Waiting[/]",
			_ => $"[dim]{status.EscapeMarkup()}[/]",
		};
	}
}
