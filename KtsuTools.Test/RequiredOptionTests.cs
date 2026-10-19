// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Test;

using System.Reflection;
using System.Runtime.CompilerServices;

using KtsuTools.Commands;

using Spectre.Console.Cli;

[TestClass]
public class RequiredOptionTests
{
	/// <summary>
	/// The C# <c>required</c> keyword is only enforced when code constructs the settings object.
	/// Spectre.Console.Cli ignores it, so an option left off the command line arrives as null and
	/// crashes later with "Value cannot be null". Every <c>required</c> option must therefore also
	/// be marked <c>isRequired</c>, so a new command cannot reintroduce the crash.
	/// </summary>
	[TestMethod]
	public void EveryRequiredOptionIsEnforcedByTheParser()
	{
		List<string> unenforced = [.. typeof(ImageProcessCommand).Assembly.GetTypes()
			.Where(type => typeof(CommandSettings).IsAssignableFrom(type))
			.SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			.Where(property => property.IsDefined(typeof(RequiredMemberAttribute)))
			.Where(property => property.GetCustomAttribute<CommandOptionAttribute>() is { IsRequired: false })
			.Select(property => $"{property.DeclaringType?.FullName}.{property.Name}")];

		Assert.IsEmpty(unenforced, "Options declared required but not isRequired: " + string.Join(", ", unenforced));
	}

	[TestMethod]
	public void MissingRequiredOptionIsReportedByName()
	{
		CommandApp app = new();
		app.Configure(config =>
		{
			config.PropagateExceptions();
			config.AddCommand<ImageProcessCommand>("image");
		});

		CommandRuntimeException exception = Assert.ThrowsExactly<CommandRuntimeException>(() => app.Run(["image", "--input", "."]));

		Assert.AreEqual("Command 'image' is missing required argument 'output'.", exception.Message);
	}
}
