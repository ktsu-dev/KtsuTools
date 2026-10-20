## v1.10.14 (patch)

Changes since v1.10.13:

- Cover the unreadable --repo-list and empty --repo paths (matt-edmondson)
- Report a missing or empty --repo-list instead of crashing or prompting [patch] (matt-edmondson)
- Give the image help example its required --output, and test every example [patch] (matt-edmondson)
- Accept every --size the image validation allows, and check padding up front [patch] (matt-edmondson)
- Fail repo discover on a --path that does not exist [patch] (matt-edmondson)
- Merge main into the Spectre.Console bump (matt-edmondson)
- Fail repo pull, build and update-packages on a --path that does not exist [patch] (Claude)
- Report the built version and the ktools name from --version and --help [patch] (Claude)
- Create the codegen output folder and report a path that cannot be written [patch] (Claude)
- Run the dedup verbs through a CommandApp in tests (Claude)
- Share one settings type across the dedup verbs (Claude)
- Have the parser enforce every required option so a missing one is named [patch] (Claude)
- Reject negative --padding and a --size below 1 before processing images [patch] (Claude)
- Reject a zero or negative --refresh on the three monitors [patch] (Claude)
- Make command Execute overrides public for Spectre.Console.Cli 0.57 (Claude)
- Merge main into the Spectre.Console bump (Claude)

