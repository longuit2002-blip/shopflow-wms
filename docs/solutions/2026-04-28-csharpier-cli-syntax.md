# CSharpier 0.30.x — `--check` flag, not `check` subcommand

**Date**: 2026-04-28
**Affects**: `Taskfile.yml`, `.husky/pre-commit`, any future CI workflow

## Problem

`task pre-commit` and `task ci` first failed with:

```
There was no file or directory found at check
```

CSharpier received `check` as a path (file/directory) argument and didn't find it. Same for `dotnet csharpier check .`.

## Root cause

In CSharpier 0.30.6 (the version pinned in [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json)), the CLI uses **flag** syntax — `--check` is an option, not a subcommand. Confirmed via `dotnet csharpier --help`:

```
Usage:
  dotnet-csharpier [options] [<directoryOrFile>...]

Options:
  --check    Check that files are formatted. Will not write any changes.
```

A later major version (1.0+) reorganises the CLI into subcommands (`csharpier check .`, `csharpier format .`). Documentation found online may be ahead of the version actually installed.

## Solution

```bash
# In Taskfile.yml, .husky/pre-commit, CI workflows
dotnet csharpier --check .   # check mode — fails on unformatted files
dotnet csharpier .           # format mode — writes changes
```

NOT:

```bash
dotnet csharpier check .     # interpreted as path argument; will fail
dotnet csharpier format .    # same
```

## Prevention

1. The CSharpier version is pinned in `.config/dotnet-tools.json`. Bumping requires re-checking which CLI shape it expects:

   ```bash
   dotnet csharpier --help
   ```

2. When upgrading to CSharpier 1.0+ (whenever that happens), update `Taskfile.yml`, `.husky/pre-commit`, and `.github/workflows/ci.yml` *together* in one commit. The comment block at the top of `.husky/pre-commit` already flags this dependency.

## References

- `Taskfile.yml` — `pre-commit`, `ci`, `format` tasks
- `.husky/pre-commit` — invoked by every `git commit`
- `.config/dotnet-tools.json` — version pin
