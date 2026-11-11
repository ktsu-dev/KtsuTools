## v1.11.1 (patch)

Changes since v1.11.0:

- Exit non-zero from sync when a push or pull request fails [patch] (#338) (matt-edmondson)
- Pass everything after `ktools git --` to git verbatim [patch] (#339) (matt-edmondson)
- Keep each file's encoding and BOM when merge writes it back [patch] (#348) (matt-edmondson)
- Skip .git, .svn, .hg and .bzr when dedup scans a tree [patch] (#334) (matt-edmondson)
- Exit non-zero from dedup dedupe when a planned deletion fails [patch] (#337) (matt-edmondson)

