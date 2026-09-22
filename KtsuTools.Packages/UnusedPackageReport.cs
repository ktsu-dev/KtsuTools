// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Packages;

/// <summary>
/// How a declared <c>PackageReference</c> relates to the project's compiled source.
/// </summary>
public enum PackageUsage
{
	/// <summary>The package's namespace or identifier appears in the project's source.</summary>
	Used,

	/// <summary>Nothing in the project's source refers to the package.</summary>
	Unused,

	/// <summary>
	/// The package cannot appear in source by design — an analyzer, a build-time-only
	/// reference, or one whose compile assets are suppressed. Reported separately so it
	/// never shows up as a false positive.
	/// </summary>
	BuildTimeOnly,
}

/// <summary>
/// One <c>PackageReference</c> as classified against the project that declares it.
/// </summary>
/// <param name="ProjectPath">Full path to the declaring <c>.csproj</c>.</param>
/// <param name="PackageId">The package identifier.</param>
/// <param name="Usage">How the package relates to the project's source.</param>
/// <param name="Reason">Why the classification was reached, for display.</param>
public sealed record PackageFinding(string ProjectPath, string PackageId, PackageUsage Usage, string Reason);

/// <summary>
/// The result of scanning a tree for <c>PackageReference</c>s that no source refers to.
/// </summary>
public sealed record UnusedPackageReport
{
	/// <summary>Every classified package reference, across every project scanned.</summary>
	public IReadOnlyList<PackageFinding> Findings { get; init; } = [];

	/// <summary>
	/// <c>PackageVersion</c> entries in <c>Directory.Packages.props</c> that no project,
	/// and no <c>Directory.Build.props</c>/<c>.targets</c>, references at all.
	/// </summary>
	public IReadOnlyList<string> OrphanedPackageVersions { get; init; } = [];

	/// <summary>How many project files were scanned.</summary>
	public int ProjectsScanned { get; init; }

	/// <summary>The findings that are genuinely unused.</summary>
	public IEnumerable<PackageFinding> Unused => Findings.Where(f => f.Usage == PackageUsage.Unused);

	/// <summary>The findings held back because the package cannot appear in source.</summary>
	public IEnumerable<PackageFinding> BuildTimeOnly => Findings.Where(f => f.Usage == PackageUsage.BuildTimeOnly);
}
