## v1.11.2 (patch)

Changes since v1.11.1:

- Show GitHub errors in build-monitor, mark running runs, and list org private repos [patch] (#350) (matt-edmondson)
- Fail packages update on an unsupported --source or a failed version lookup [patch] (#363) (matt-edmondson)
- Keep version 2 exactly when merging over an empty version 1 [patch] (#366) (matt-edmondson)
- Merge only copies of the same file when the pattern matches several names [patch] (#367) (matt-edmondson)
- Show the explorer's menu choices as text, not Spectre markup [patch] (#370) (matt-edmondson)
- Skip node_modules, bin, obj, .git and nested repositories in markdown lint and clean [patch] (#372) (matt-edmondson)
- Skip a repository with no upstream in repo pull, and show why a pull failed [patch] (#374) (matt-edmondson)
- Keep memfrag monitor's "Process has exited." on screen and exit 1 [patch] (#380) (matt-edmondson)
- Name a missing git (or other tool) and exit 1 instead of 255 [patch] (#381) (matt-edmondson)
- Keep a bullet nested under an ordered item inside it in markdown lint [patch] (#392) (matt-edmondson)

