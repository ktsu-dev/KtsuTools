// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools;

using System.Reflection;

/// <summary>
/// The name and version the CLI reports in <c>--help</c> and <c>--version</c>, read from the built
/// assembly so they follow <c>AssemblyName</c> and the version the build stamps rather than literals.
/// </summary>
public static class CliIdentity
{
	private static readonly Assembly CliAssembly = typeof(CliIdentity).Assembly;

	/// <summary>Gets the command name, which is the executable's assembly name.</summary>
	public static string Name { get; } = CliAssembly.GetName().Name ?? "ktools";

	/// <summary>Gets the informational version the build stamped into the assembly.</summary>
	public static string Version { get; } =
		CliAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? CliAssembly.GetName().Version?.ToString()
		?? "0.0.0";
}
