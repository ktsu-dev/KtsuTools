// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.ComponentModel;
using ktsu.Semantics.Paths;
using KtsuTools.Core.Services.Process;
using KtsuTools.SvnMigrate;
using Spectre.Console.Cli;

/// <summary>
/// Pins what happens when a tool ktools shells out to, such as git, is not installed: the error names
/// the tool rather than the working directory, the command exits 1 rather than 255, and svn-migrate's
/// prerequisite check reports it as a validation error.
/// </summary>
[TestClass]
public class MissingExecutableTests
{
	private const string MissingCommand = "ktools-test-no-such-executable";

	[TestMethod]
	public async Task RunningAMissingExecutableThrowsExecutableNotFound()
	{
		ExecutableNotFoundException exception = await Assert.ThrowsExactlyAsync<ExecutableNotFoundException>(
			() => new ProcessService().RunAsync(MissingCommand, "--version", Path.GetTempPath())).ConfigureAwait(false);

		Assert.AreEqual(MissingCommand, exception.Command);
		Assert.Contains(MissingCommand, exception.Message);
		Assert.IsInstanceOfType<Win32Exception>(exception.InnerException);
	}

	[TestMethod]
	public async Task AMissingWorkingDirectoryIsNotReportedAsAMissingExecutable()
	{
		string missingDirectory = Path.Combine(Path.GetTempPath(), "ktools-no-such-dir-" + Guid.NewGuid().ToString("N"));

		Exception exception = await Assert.ThrowsAsync<Exception>(
			() => new ProcessService().RunAsync("dotnet", "--version", missingDirectory)).ConfigureAwait(false);

		Assert.IsNotInstanceOfType<ExecutableNotFoundException>(exception);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task AMissingExecutableExitsOneWithAMessageNamingIt()
	{
		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(() =>
		{
			exitCode = Program.HandleException(new ExecutableNotFoundException("git", new Win32Exception(2)));
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode);
		Assert.Contains("'git' was not found", output);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task OtherExceptionsStillExitMinusOneWithTheirMessage()
	{
		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(() =>
		{
			exitCode = Program.HandleException(new InvalidOperationException("something else broke"));
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		Assert.AreEqual(-1, exitCode);
		Assert.Contains("Error: something else broke", output);
	}

	[TestMethod]
	public void ConstructorsNameTheCommandAndKeepTheCause()
	{
		Win32Exception cause = new(2);

		Assert.AreEqual(string.Empty, new ExecutableNotFoundException().Command);
		Assert.AreEqual("git", new ExecutableNotFoundException("git").Command);
		Assert.IsNull(new ExecutableNotFoundException("git").InnerException);
		Assert.AreSame(cause, new ExecutableNotFoundException("git", cause).InnerException);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task AMissingExecutableWrappedInAnotherExceptionStillExitsOne()
	{
		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(() =>
		{
			exitCode = Program.HandleException(
				new InvalidOperationException("outer", new ExecutableNotFoundException("git")));
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode);
		Assert.Contains("'git' was not found", output);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task ACommandLineErrorIsRenderedAsSpectreRendersIt()
	{
		CommandApp app = new();
		app.Configure(config =>
		{
			Program.ConfigureCommands(config);
			config.PropagateExceptions();
		});

		CommandAppException parseError = Assert.ThrowsExactly<CommandParseException>(() => app.Run(["no-such-command"]));
		Assert.IsNotNull(parseError.Pretty, "Spectre should supply a rendered form of a parse error.");

		int exitCode = 0;
		string output = await ConsoleCapture.CaptureAsync(() =>
		{
			exitCode = Program.HandleException(parseError);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		Assert.AreEqual(-1, exitCode);
		Assert.Contains("no-such-command", output);
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task SvnMigrateReportsAMissingGitAsAValidationError()
	{
		string target = Path.Combine(Path.GetTempPath(), "ktools-svn-target-" + Guid.NewGuid().ToString("N"));
		SvnMigrateService service = new(new NoGitProcessService());
		int exitCode = 0;

		string output = await ConsoleCapture.CaptureAsync(async () =>
			exitCode = await service.MigrateAsync(
				new Uri("svn://example.com/repo"),
				AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(target)).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exitCode);
		Assert.Contains("git is not installed or not on PATH", output);
	}

	/// <summary>A process service on a machine without git: every git call fails to launch.</summary>
	private sealed class NoGitProcessService : IProcessService
	{
		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			RunAsync(command, arguments, workingDirectory, null, ct);

		public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default) =>
			throw new ExecutableNotFoundException(command, new Win32Exception(2));

		public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken ct = default) =>
			throw new ExecutableNotFoundException(command, new Win32Exception(2));
	}
}
