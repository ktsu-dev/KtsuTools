## v1.5.0 (minor)

Changes since v1.4.0:

- test(sync): derive the path root instead of assuming a leading separator [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): map and filter with LINQ instead of accumulating loops [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): canonicalise the temp repo paths for macOS [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): cover the branch-mode paths for the quality gate [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- test(sync): use Path.Join for the temp repo paths [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- sync: commit onto a dedicated branch with --branch [minor] ([@matt-edmondson](https://github.com/matt-edmondson))
- Assert parallel build overlap by concurrency, not wall clock ([@matt-edmondson](https://github.com/matt-edmondson))
- Cover the output paths of repo list ([@matt-edmondson](https://github.com/matt-edmondson))
- Add a repo list verb for cached repositories and solutions ([@matt-edmondson](https://github.com/matt-edmondson))
- Align the ktsu.Semantics.Strings pin with ktsu.Semantics.Paths ([@matt-edmondson](https://github.com/matt-edmondson))
- Gate Dependabot auto-merge on CI actually being green ([@Claude](https://github.com/Claude))
- ci: adopt the consolidated .NET workflow [patch] ([@Claude](https://github.com/Claude))

