// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using KtsuTools.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Pins that a malformed <c>--svn-url</c> is reported as an option error before the command builds a
/// <see cref="Uri"/> from it, rather than escaping as a <see cref="UriFormatException"/> stack trace.
/// </summary>
[TestClass]
public class SvnMigrateCommandSettingsTests
{
	[TestMethod]
	[DataRow("svn.example.com/repo")]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("not a url")]
	[DataRow("svn.example.com:8080/repo")]
	[DataRow("mailto:someone@example.com")]
	public void RejectsAnSvnUrlThatIsNotAnAbsoluteSvnUrl(string candidate)
	{
		ValidationResult result = Settings(candidate).Validate();

		Assert.IsFalse(result.Successful, $"'{candidate}' should be rejected before new Uri() sees it.");
		StringAssert.Contains(result.Message ?? string.Empty, "--svn-url");
	}

	[TestMethod]
	[DataRow("https://svn.example.com/repo")]
	[DataRow("http://svn.example.com/repo")]
	[DataRow("svn://svn.example.com/repo")]
	[DataRow("svn+ssh://user@svn.example.com/repo")]
	[DataRow("file:///srv/svn/repo")]
	public void AcceptsAnAbsoluteSvnUrl(string candidate)
	{
		ValidationResult result = Settings(candidate).Validate();

		Assert.IsTrue(result.Successful, result.Message);
	}

	[TestMethod]
	public void ASchemelessSvnUrlFailsValidationNamingTheOptionInsteadOfThrowingUriFormatException()
	{
		CommandApp app = new();
		app.Configure(config =>
		{
			Program.ConfigureCommands(config);
			config.PropagateExceptions();
		});

		CommandRuntimeException exception = Assert.ThrowsExactly<CommandRuntimeException>(
			() => app.Run(["svn-migrate", "--svn-url", "svn.example.com/repo", "--target", "./out"]));

		StringAssert.Contains(exception.Message, "--svn-url");
	}

	private static SvnMigrateCommand.Settings Settings(string candidate) =>
		new() { SvnUrl = candidate, TargetPath = "./out" };
}
