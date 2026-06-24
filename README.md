# GitDesk
<img width="1648" height="957" alt="image" src="https://github.com/user-attachments/assets/865aca4c-2094-4812-a5d1-87558bf2f71a" />


GitDesk is a cross-platform desktop Git UI built with C# and Avalonia. It focuses on a dense, practical workflow for browsing workspaces, inspecting commits, comparing file changes, and running common Git commands from a desktop interface.

## Features

- Browse and remember multiple workspaces with LevelDB-backed workspace history.
- Display a project directory tree with right-click Git operations.
- Delete files or folders from the project tree while either keeping or deleting local files.
- Tree delete handles untracked and ignored paths without surfacing Git pathspec failures.
- Show local unpushed commits in `ChangeLists`.
- Show repository commit history with `Local` and `Remote` state markers.
- Show merge conflicts as a dedicated `Conflicts` ChangeList that includes the full incoming pull/merge change set with per-file conflict or pull status.
- Resolve merge conflicts with a WinMerge-style editor that supports selected-block or all-block Ours, Theirs, Both, and Base merge choices.
- Open the merge editor directly from the top-level `Conflicts` ChangeList.
- Show a blocking busy overlay while Git commands and long-running workspace operations are executing.
- Inspect commit file changes in a docked `CL Changes` panel.
- Search a commit directly through `Search -> ByCommit`.
- Open a standalone commit changes window from commit search.
- Compare changed files in a WinMerge-style two-pane compare window.
- Run common Git commands including status, add, commit, revert, fetch, pull, push, checkout, and log.
- Pull can automatically remove untracked files that Git reports would be overwritten by a fast-forward merge, then retry the pull once.
- Checking out a history commit hard-resets the current branch to that commit and removes local untracked or ignored files under paths changed between HEAD and the selected commit, with retry handling for stale invalid paths reported by Git clean.
- Push opens a branch selection dialog and defaults to the current branch.
- Configure Git identity and GitHub HTTPS credentials through `Tools -> Settings`.
- Hide and restore the output panel.
- Copy full commit content from `ChangeLists` and `History`.
- Store text and Git process output as UTF-8.
- Render status output with readable state labels instead of raw Git status codes.

## Requirements

- .NET SDK 10.0 (the project multi-targets `net9.0;net10.0`, so the .NET 10 SDK is
  required to build — it can build the `net9.0` target too).
- Git available on `PATH`

### Install the .NET SDKs (winget)

```powershell
# .NET SDK 9
winget install --id Microsoft.DotNet.SDK.9 -e --accept-source-agreements --accept-package-agreements

# .NET SDK 10
winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
```

Both SDKs install side by side under `C:\Program Files\dotnet\` and do not conflict.
Open a new terminal afterwards and run `dotnet --list-sdks` to verify.

## Target Frameworks

The project multi-targets both `net9.0` and `net10.0`:

```xml
<TargetFrameworks>net9.0;net10.0</TargetFrameworks>
```

A plain `dotnet build` / `dotnet run` builds both. Pass `-f <tfm>` to pick one.
Because `net10.0` is listed, the **.NET 10 SDK must be installed** or the build
fails with `NETSDK1045` (this also blocks the `net9.0` target). Running the
`net10.0` output requires the **.NET 10 Desktop Runtime** on the target machine.

## Run

```powershell
dotnet run                 # default TFM
dotnet run -f net10.0      # .NET 10
dotnet run -f net9.0       # .NET 9
```

## Build

```powershell
dotnet build               # both target frameworks
dotnet build -c Release -f net10.0   # Release, .NET 10 only
dotnet build -c Release -f net9.0    # Release, .NET 9 only
```

> Close any running `GitDesk.exe` before rebuilding, otherwise the build fails to
> overwrite the locked executable.

## Publish Examples

```powershell
# Framework-dependent (target machine needs the .NET 10 Desktop Runtime)
dotnet publish -c Release -f net10.0 -r win-x64

# Self-contained single file (no runtime needed on the target machine)
dotnet publish -c Release -f net10.0 -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -f net9.0  -r linux-x64 --self-contained true
dotnet publish -c Release -f net10.0 -r osx-arm64 --self-contained true
```

## Notes

- The app shells out to the local `git` executable. It does not implement a custom Git protocol stack.
- Workspace history is stored locally under the user's application data directory.
- GitHub tokens are handed to Git Credential Manager through `git credential approve`; GitDesk only stores non-secret settings such as host, username, and Git author identity.
- Fetch, pull, and push run through Git Credential Manager and open Settings when authentication fails.
- GitHub tokens only apply to HTTPS remotes. SSH remotes such as `git@github.com:owner/repo.git` should be converted to HTTPS before using token authentication.
- On an unhandled crash the app writes a full minidump to `dumps/*.dmp` next to the executable and appends details to `crash.log`, for analysis in WinDbg/cdb. Both paths are git-ignored.
- The vendored LevelDB.NET source is kept under `third_party/leveldb.net` for source visibility.
- Repository text files are expected to be UTF-8 without BOM. Run `tools/verify-utf8-nobom.ps1` to check this locally; the included Git hook uses the same check.

## Git Hooks

```powershell
git config core.hooksPath .githooks
```

This enables the repository pre-commit hook that rejects tracked files containing a UTF-8 BOM.

## Contributing

This project is open source and free to use. If you want to request a change, report a bug, or propose a feature, please open a GitHub Issue first so the requested modification can be discussed and tracked.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
