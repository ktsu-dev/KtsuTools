// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Packages;

using System.Xml.Linq;

/// <summary>
/// Reads items from MSBuild project files whatever XML namespace they are written in.
/// </summary>
/// <remarks>
/// Older project files, such as the ones Visual Studio migrated from <c>packages.config</c>, declare
/// <c>xmlns="http://schemas.microsoft.com/developer/msbuild/2003"</c> on the root element, which puts
/// every element in that namespace. A bare <see cref="XName"/> lookup matches none of them, so every
/// reader of project items goes through here and matches on the local name.
/// </remarks>
internal static class ProjectXml
{
	/// <summary>The <c>PackageReference</c> items in <paramref name="doc"/>, in document order.</summary>
	internal static IEnumerable<XElement> PackageReferences(XDocument doc) =>
		doc.Descendants().Where(e => e.Name.LocalName == "PackageReference");
}
