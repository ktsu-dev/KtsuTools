## v1.10.7 (patch)

Changes since v1.10.6:

- Write LF files back with LF on Windows too ([@Claude](https://github.com/Claude))
- Cover skipped non-concrete versions and the migrate-cpm guard in tests ([@Claude](https://github.com/Claude))
- Use Path.Join in the dedup symlink tests ([@Claude](https://github.com/Claude))
- Skip symlinks in dedup so a link never gets its target deleted [patch] ([@Claude](https://github.com/Claude))
- Skip a sync commit when the user has other changes staged [patch] ([@Claude](https://github.com/Claude))
- Leave property, range and floating versions alone in packages update [patch] ([@Claude](https://github.com/Claude))
- Find .slnx solutions and stop dropping sibling repos that share a prefix [patch] ([@Claude](https://github.com/Claude))

