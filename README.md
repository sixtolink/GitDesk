# GitDesk

GitDesk is a cross-platform desktop Git UI built with C# and Avalonia. It focuses on a dense, practical workflow for browsing workspaces, inspecting commits, comparing file changes, and running common Git commands from a desktop interface.

## Features

- Browse and remember multiple workspaces with LevelDB-backed workspace history.
- Display a project directory tree with right-click Git operations.
- Delete files or folders from the project tree while either keeping or deleting local files.
- Tree delete handles untracked and ignored paths without surfacing Git pathspec failures.
- Show local unpushed commits in `ChangeLists`.
- Show repository commit history with `Local` and `Remote` state markers.
- Inspect commit file changes in a docked `CL Changes` panel.
- Search a commit directly through `Search -> ByCommit`.
- Open a standalone commit changes window from commit search.
- Compare changed files in a WinMerge-style two-pane compare window.
- Run common Git commands including status, add, commit, revert, fetch, pull, push, checkout, and log.
- Pull can automatically remove untracked files that Git reports would be overwritten by a fast-forward merge, then retry the pull once.
- Checking out a history commit hard-resets the current branch to that commit and removes local untracked or ignored files so the workspace matches the selected commit.
- Push opens a branch selection dialog and defaults to the current branch.
- Configure Git identity and GitHub HTTPS credentials through `Tools -> Settings`.
- Hide and restore the output panel.
- Copy full commit content from `ChangeLists` and `History`.
- Store text and Git process output as UTF-8.
- Render status output with readable state labels instead of raw Git status codes.

## Requirements

- .NET SDK 9.0 or newer
- Git available on `PATH`

## Run

```powershell
dotnet run
```

## Build

```powershell
dotnet build
```

## Publish Examples

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish -c Release -r linux-x64 --self-contained true
dotnet publish -c Release -r osx-arm64 --self-contained true
```

## Notes

- The app shells out to the local `git` executable. It does not implement a custom Git protocol stack.
- Workspace history is stored locally under the user's application data directory.
- GitHub tokens are handed to Git Credential Manager through `git credential approve`; GitDesk only stores non-secret settings such as host, username, and Git author identity.
- Fetch, pull, and push run through Git Credential Manager and open Settings when authentication fails.
- GitHub tokens only apply to HTTPS remotes. SSH remotes such as `git@github.com:owner/repo.git` should be converted to HTTPS before using token authentication.
- The vendored LevelDB.NET source is kept under `third_party/leveldb.net` for source visibility.

## Contributing

This project is open source and free to use. If you want to request a change, report a bug, or propose a feature, please open a GitHub Issue first so the requested modification can be discussed and tracked.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
