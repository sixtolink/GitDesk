# GitDesk

GitDesk is a cross-platform desktop Git UI built with C# and Avalonia. It focuses on a dense, practical workflow for browsing workspaces, inspecting commits, comparing file changes, and running common Git commands from a desktop interface.

## Features

- Browse and remember multiple workspaces with LevelDB-backed workspace history.
- Display a project directory tree with right-click Git operations.
- Show local unpushed commits in `ChangeLists`.
- Show repository commit history with `Local` and `Remote` state markers.
- Inspect commit file changes in a docked `CL Changes` panel.
- Search a commit directly through `Search -> ByCommit`.
- Open a standalone commit changes window from commit search.
- Compare changed files in a WinMerge-style two-pane compare window.
- Run common Git commands including status, add, commit, revert, fetch, pull, push, checkout, and log.
- Configure Git identity and GitHub HTTPS credentials through `Tools -> Settings`.
- Hide and restore the output panel.
- Copy full commit content from `ChangeLists` and `History`.
- Store text and Git process output as UTF-8.

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
