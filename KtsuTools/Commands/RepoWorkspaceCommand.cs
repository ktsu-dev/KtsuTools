// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Commands;

using System.ComponentModel;
using System.IO;
using ktsu.Semantics.Paths;
using KtsuTools.Core.UI;
using KtsuTools.Repo;
using Spectre.Console.Cli;

/// <summary>
/// Base for the <c>repo</c> verbs that take a workspace root and operate on every repository found
/// under it. Each one differs only in which <see cref="RepoService"/> call it makes, so resolving
/// the path and wiring up Ctrl-C lives here rather than once per verb.
/// </summary>
/// <param name="repoService">The service the verb delegates to.</param>
public abstract class RepoWorkspaceCommand(RepoService repoService) : AsyncCommand<RepoWorkspaceCommand.Settings>
{
	/// <summary>Gets the service the verb delegates to.</summary>
	protected RepoService RepoService { get; } = repoService;

	/// <summary>
	/// Settings shared by every workspace-wide repo verb.
	/// </summary>
	public sealed class Settings : CommandSettings
	{
		/// <summary>
		/// Gets the root directory to search for repositories.
		/// </summary>
		[CommandOption("--path <PATH>")]
		[Description("Root directory containing the repositories to operate on")]
		[DefaultValue("c:/dev/ktsu-dev")]
		public string Path { get; init; } = "c:/dev/ktsu-dev";
	}

	/// <summary>
	/// Runs the verb against a resolved workspace root.
	/// </summary>
	/// <param name="path">The workspace root, already made absolute.</param>
	/// <param name="ct">Cancels when the user interrupts the run.</param>
	/// <returns>The process exit code.</returns>
	protected abstract Task<int> RunAsync(AbsoluteDirectoryPath path, CancellationToken ct);

	/// <inheritdoc/>
	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		Ensure.NotNull(settings);
		using CtrlCScope scope = new();
		AbsoluteDirectoryPath path = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.GetFullPath(settings.Path));
		return await RunAsync(path, scope.Token).ConfigureAwait(false);
	}
}
