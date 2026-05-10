# Husky.NET pre-commit — PATH-discovery shim for Windows post-winget

**Date**: 2026-04-28
**Affects**: `.husky/pre-commit`

## Problem

After installing .NET 8 SDK + Task + Docker via `winget` on Windows, the running Git Bash session does not see the new PATH. Subsequent `git commit` invocations fire the Husky.NET pre-commit hook, which calls `dotnet csharpier --check .`, which fails:

```
.husky/pre-commit: line 13: dotnet: command not found
husky - pre-commit hook exited with code 127 (error)
```

The fix isn't to restart every shell on the developer's machine — the user may have an editor terminal, a CI agent, or a third-party Git GUI all hitting `git commit` with stale PATH at different times. The hook itself needs to be resilient.

## Root cause

`winget` writes the new tool's directory into the system or user `PATH` environment variable. Already-running shell processes — including Git Bash, IDE terminals, and Windows Terminal tabs that opened before the install — keep their snapshot of PATH from process-start time. They never see the new tool until they exit and a fresh process spawns.

`dotnet`, after `winget install Microsoft.DotNet.SDK.8`, lives at `C:\Program Files\dotnet\dotnet.exe`. Predictable location. The hook can find it without depending on PATH being current.

## Solution

[`.husky/pre-commit`](../../.husky/pre-commit) probes a list of standard install locations and prepends to `PATH` if `dotnet` is found there:

```sh
if ! command -v dotnet >/dev/null 2>&1; then
  for candidate in \
    "/c/Program Files/dotnet" \
    "/c/Program Files (x86)/dotnet" \
    "$LOCALAPPDATA/Microsoft/dotnet" \
    "/usr/local/share/dotnet" \
    "/usr/share/dotnet" \
    "$HOME/.dotnet"
  do
    if [ -x "$candidate/dotnet" ] || [ -x "$candidate/dotnet.exe" ]; then
      export PATH="$candidate:$PATH"
      break
    fi
  done

  if ! command -v dotnet >/dev/null 2>&1; then
    echo "pre-commit: 'dotnet' not found on PATH or at standard install locations." >&2
    echo "Install .NET 8 SDK (winget install Microsoft.DotNet.SDK.8) and restart your shell." >&2
    exit 1
  fi
fi
```

The list covers Windows (Program Files, LocalAppData, ~/.dotnet) + macOS (`/usr/local/share/dotnet`) + Linux (`/usr/share/dotnet`, `$HOME/.dotnet`). When `dotnet` isn't installed at all, the hook fails with a clear, actionable message — not a cryptic "command not found".

## Prevention

- The hook is committed to the repo with mode `100755`. Husky.NET regenerates `.husky/_/husky.sh` on every `dotnet husky install`, but our `pre-commit` is preserved.
- For other tools the hook might call later (e.g., `task`, `node`), apply the same pattern: probe known install paths, fail with a clear install instruction.
- `task setup` (the documented one-command onboarding) refreshes PATH from registry inside its own PowerShell context, so users who go through the documented flow don't hit this. The shim covers users who don't (or whose `git commit` happens via an IDE before they ran setup).

## References

- `.husky/pre-commit` — the shim
- `tools/extract-docs.sh` — same pattern would apply if it grew dependencies (currently uses only python3 + unzip which are universally available)
- Future CI workflows on Windows runners — likely fine since GitHub Actions runners have current PATH, but worth re-checking when CI lands in U9
