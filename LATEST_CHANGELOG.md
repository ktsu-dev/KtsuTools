## v1.11.1 (patch)

Changes since v1.11.0:

- Carry synced files onto an existing sync branch that holds another copy [patch] (#349) (matt-edmondson)
- Commit only the files sync wrote, not every scanned copy [patch] (#354) (matt-edmondson)
- Migrate SVN tags as git tags, and keep branches named like trunk [patch] (#355) (matt-edmondson)
- Report a malformed --svn-url as an option error instead of a UriFormatException [patch] (#359) (matt-edmondson)
- Match a bare --exclude name only below the scan root [patch] (#361) (matt-edmondson)
- Check Directory.Packages.props against every project, and only on a folder scan [patch] (#362) (matt-edmondson)
- Pass ktools git arguments as a list so backslashes reach git intact [patch] (#369) (matt-edmondson)
- Name the solution in repo build's dotnet build and test [patch] (#373) (matt-edmondson)
- Read Razor and XAML imports in packages find-unused [patch] (#377) (matt-edmondson)
- Count only changed lines against the merge diff preview cap [patch] (#378) (matt-edmondson)
- Keep a csproj or props file's byte order mark in packages update and migrate-cpm [patch] (#379) (matt-edmondson)
- Name ktools, and keep --path, in the git command's suggestions [patch] (#384) (matt-edmondson)
- Leave a file's line endings and leading white space alone in markdown clean [patch] (#385) (matt-edmondson)
- Read PackageReferences in a project that declares the MSBuild namespace [patch] (#386) (matt-edmondson)
- Exit non-zero from sync when a push or pull request fails [patch] (#338) (matt-edmondson)
- Pass everything after `ktools git --` to git verbatim [patch] (#339) (matt-edmondson)
- Keep each file's encoding and BOM when merge writes it back [patch] (#348) (matt-edmondson)
- Skip .git, .svn, .hg and .bzr when dedup scans a tree [patch] (#334) (matt-edmondson)
- Exit non-zero from dedup dedupe when a planned deletion fails [patch] (#337) (matt-edmondson)

