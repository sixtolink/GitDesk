using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using GitDesk.Models;
using GitDesk.Services;

namespace GitDesk.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly GitService _git = new();
    private readonly WorkspaceStore _workspaceStore = new();
    private readonly List<GitHistoryEntry> _allPendingCommits = new();
    private readonly List<GitHistoryEntry> _allPendingChangeLists = new();
    private string _workspacePath = string.Empty;
    private string _repositoryRoot = string.Empty;
    private string _currentBranch = "No repository";
    private string _selectedPath = string.Empty;
    private string _statusText = "Ready";
    private string _commitMessage = string.Empty;
    private string _filterText = string.Empty;
    private string _selectedPendingCommitChangesTitle = "Selected CL Changes";
    private bool _isOutputVisible = true;
    private int _commitChangesRequestVersion;
    private string _selectedCommitChangesRevision = string.Empty;
    private string? _selectedWorkspaceHistory;
    private FileSystemNode? _selectedNode;
    private GitHistoryEntry? _selectedPendingCommit;
    private GitHistoryEntry? _selectedHistoryEntry;
    private GitChange? _selectedCommitChange;

    public MainWindowViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        InitRepositoryCommand = new AsyncRelayCommand(_ => InitRepositoryAsync());
        FetchCommand = new AsyncRelayCommand(_ => RunRepositoryCommandAsync("Fetch", new[] { "fetch", "--all", "--prune" }, true));
        PullCommand = new AsyncRelayCommand(_ => RunRepositoryCommandAsync("Pull", new[] { "pull", "--ff-only" }, true));
        PushCommand = new AsyncRelayCommand(_ => PushToOriginAsync());
        CommitCommand = new AsyncRelayCommand(_ => CommitAsync());
        OpenSelectedFolderCommand = new AsyncRelayCommand(_ => OpenSelectedFolderAsync());
        RevertPendingCommitCommand = new AsyncRelayCommand(_ => RevertSelectedPendingCommitAsync());
        RestorePendingCommitCommand = new AsyncRelayCommand(_ => RestoreSelectedPendingCommitAsync());
        DeleteHeadPendingCommitKeepChangesCommand = new AsyncRelayCommand(_ => DeleteSelectedHeadCommitAsync(keepChanges: true));
        DeleteHeadPendingCommitDiscardChangesCommand = new AsyncRelayCommand(_ => DeleteSelectedHeadCommitAsync(keepChanges: false));
        AddSelectedCommand = new AsyncRelayCommand(_ => RunPathCommandAsync("Add", new[] { "add" }, true));
        RevertSelectedCommand = new AsyncRelayCommand(_ => RunPathCommandAsync("Revert", new[] { "restore", "--staged", "--worktree" }, true));
        DiffSelectedCommand = new AsyncRelayCommand(_ => ShowDiffAsync());
        LogSelectedCommand = new AsyncRelayCommand(_ => LoadHistoryForSelectedAsync());
        CheckoutHistoryCommitCommand = new AsyncRelayCommand(_ => CheckoutSelectedHistoryCommitAsync());
        StatusSelectedCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ToggleOutputVisibilityCommand = new AsyncRelayCommand(_ =>
        {
            IsOutputVisible = !IsOutputVisible;
            return Task.CompletedTask;
        });
        RefreshTreeCommand = new AsyncRelayCommand(_ =>
        {
            foreach (var root in Roots)
            {
                root.Refresh();
            }

            return Task.CompletedTask;
        });
    }

    public ObservableCollection<FileSystemNode> Roots { get; } = new();

    public ObservableCollection<string> WorkspaceHistory { get; } = new();

    public ObservableCollection<GitHistoryEntry> History { get; } = new();

    public ObservableCollection<GitChange> SelectedPendingCommitChanges { get; } = new();

    public ObservableCollection<string> OutputLines { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand InitRepositoryCommand { get; }

    public AsyncRelayCommand FetchCommand { get; }

    public AsyncRelayCommand PullCommand { get; }

    public AsyncRelayCommand PushCommand { get; }

    public AsyncRelayCommand CommitCommand { get; }

    public AsyncRelayCommand OpenSelectedFolderCommand { get; }

    public AsyncRelayCommand RevertPendingCommitCommand { get; }

    public AsyncRelayCommand RestorePendingCommitCommand { get; }

    public AsyncRelayCommand DeleteHeadPendingCommitKeepChangesCommand { get; }

    public AsyncRelayCommand DeleteHeadPendingCommitDiscardChangesCommand { get; }

    public AsyncRelayCommand AddSelectedCommand { get; }

    public AsyncRelayCommand RevertSelectedCommand { get; }

    public AsyncRelayCommand DiffSelectedCommand { get; }

    public AsyncRelayCommand LogSelectedCommand { get; }

    public AsyncRelayCommand CheckoutHistoryCommitCommand { get; }

    public AsyncRelayCommand StatusSelectedCommand { get; }

    public AsyncRelayCommand ToggleOutputVisibilityCommand { get; }

    public AsyncRelayCommand RefreshTreeCommand { get; }

    public string WorkspacePath
    {
        get => _workspacePath;
        set => SetProperty(ref _workspacePath, value);
    }

    public string? SelectedWorkspaceHistory
    {
        get => _selectedWorkspaceHistory;
        set => SetProperty(ref _selectedWorkspaceHistory, value);
    }

    public bool IsOpeningWorkspace { get; private set; }

    public string RepositoryRoot
    {
        get => _repositoryRoot;
        private set => SetProperty(ref _repositoryRoot, value);
    }

    public string CurrentBranch
    {
        get => _currentBranch;
        private set => SetProperty(ref _currentBranch, value);
    }

    public string SelectedPath
    {
        get => _selectedPath;
        private set => SetProperty(ref _selectedPath, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set => SetProperty(ref _commitMessage, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyPendingFilter();
            }
        }
    }

    public FileSystemNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                SelectedPath = value?.FullPath ?? string.Empty;
            }
        }
    }

    public ObservableCollection<GitHistoryEntry> PendingCommits { get; } = new();

    public GitHistoryEntry? SelectedPendingCommit
    {
        get => _selectedPendingCommit;
        set
        {
            if (SetProperty(ref _selectedPendingCommit, value))
            {
                _ = LoadSelectedCommitChangesAsync(value);
            }
        }
    }

    public GitHistoryEntry? SelectedHistoryEntry
    {
        get => _selectedHistoryEntry;
        set
        {
            if (SetProperty(ref _selectedHistoryEntry, value))
            {
                _ = LoadSelectedCommitChangesAsync(value);
            }
        }
    }

    public GitChange? SelectedCommitChange
    {
        get => _selectedCommitChange;
        set => SetProperty(ref _selectedCommitChange, value);
    }

    public string PendingCountText => $"{PendingCommits.Count} ChangeLists";

    public string SelectedPendingCommitChangesTitle
    {
        get => _selectedPendingCommitChangesTitle;
        private set => SetProperty(ref _selectedPendingCommitChangesTitle, value);
    }

    public bool IsOutputVisible
    {
        get => _isOutputVisible;
        set
        {
            if (SetProperty(ref _isOutputVisible, value))
            {
                OnPropertyChanged(nameof(IsOutputHidden));
                OnPropertyChanged(nameof(OutputSplitterRowHeight));
                OnPropertyChanged(nameof(OutputDockRowHeight));
                OnPropertyChanged(nameof(OutputVisibilityIcon));
                OnPropertyChanged(nameof(OutputVisibilityButtonText));
                OnPropertyChanged(nameof(OutputVisibilityMenuText));
            }
        }
    }

    public bool IsOutputHidden => !IsOutputVisible;

    public GridLength OutputSplitterRowHeight => IsOutputVisible
        ? new GridLength(5)
        : new GridLength(0);

    public GridLength OutputDockRowHeight => IsOutputVisible
        ? new GridLength(180)
        : new GridLength(0);

    public string OutputVisibilityIcon => IsOutputVisible ? "▼" : "▲";

    public string OutputVisibilityButtonText => IsOutputVisible ? "Hide Output" : "Show Output";

    public string OutputVisibilityMenuText => IsOutputVisible ? "Hide Output" : "Show Output";

    public async Task InitializeWorkspaceHistoryAsync()
    {
        IReadOnlyList<string> history;
        try
        {
            history = await _workspaceStore.LoadAsync();
        }
        catch (Exception ex)
        {
            history = Array.Empty<string>();
            AppendOutput($"Load workspace history failed: {ex.Message}");
        }

        ReplaceWorkspaceHistory(history, null);

        var firstValidWorkspace = history.FirstOrDefault(Directory.Exists);
        if (firstValidWorkspace is not null)
        {
            await OpenWorkspaceAsync(firstValidWorkspace, remember: false);
            return;
        }

        Roots.Clear();
        History.Clear();
        SelectedPendingCommitChanges.Clear();
        _allPendingCommits.Clear();
        _allPendingChangeLists.Clear();
        PendingCommits.Clear();
        WorkspacePath = string.Empty;
        SelectedWorkspaceHistory = null;
        SelectedPath = string.Empty;
        RepositoryRoot = string.Empty;
        CurrentBranch = "No repository";
        StatusText = history.Count == 0 ? "Browse workspace" : "No valid workspace";
        OnPropertyChanged(nameof(PendingCountText));

        if (history.Count > 0)
        {
            AppendOutput("Workspace history contains no valid paths.");
        }
    }

    public async Task<bool> OpenWorkspaceFromHistoryAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (PathEquals(path, WorkspacePath))
        {
            return true;
        }

        if (!Directory.Exists(path))
        {
            AppendOutput($"Invalid workspace path: {path}");
            StatusText = "Invalid workspace";
            SelectedWorkspaceHistory = string.IsNullOrWhiteSpace(WorkspacePath) ? null : WorkspacePath;
            return false;
        }

        return await OpenWorkspaceAsync(path);
    }

    public async Task<bool> OpenWorkspaceAsync(string path, bool remember = true)
    {
        if (!Directory.Exists(path))
        {
            AppendOutput($"Invalid workspace path: {path}");
            StatusText = "Invalid workspace";
            SelectedWorkspaceHistory = string.IsNullOrWhiteSpace(WorkspacePath) ? null : WorkspacePath;
            return false;
        }

        IsOpeningWorkspace = true;
        try
        {
            WorkspacePath = Path.GetFullPath(path);

            if (remember)
            {
                try
                {
                    var history = await _workspaceStore.AddAsync(WorkspacePath);
                    ReplaceWorkspaceHistory(history, WorkspacePath);
                }
                catch (Exception ex)
                {
                    AppendOutput($"Save workspace history failed: {ex.Message}");
                }
            }
            else
            {
                SelectedWorkspaceHistory = WorkspaceHistory.FirstOrDefault(item => PathEquals(item, WorkspacePath));
            }

            Roots.Clear();
            Roots.Add(new FileSystemNode(WorkspacePath, true));
            SelectedPath = WorkspacePath;
            AppendOutput($"Workspace: {WorkspacePath}");
            await RefreshAsync();
            return true;
        }
        finally
        {
            IsOpeningWorkspace = false;
        }
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspacePath) || !Directory.Exists(WorkspacePath))
        {
            RepositoryRoot = string.Empty;
            CurrentBranch = "No repository";
            StatusText = "Browse workspace";
            _allPendingCommits.Clear();
            _allPendingChangeLists.Clear();
            PendingCommits.Clear();
            SelectedPendingCommitChanges.Clear();
            SelectedPendingCommitChangesTitle = "Selected CL Changes";
            OnPropertyChanged(nameof(PendingCountText));
            return;
        }

        StatusText = "Refreshing";

        var probePath = !string.IsNullOrWhiteSpace(SelectedPath) ? SelectedPath : WorkspacePath;
        var root = await _git.FindRepositoryRootAsync(probePath);
        if (root is null)
        {
            RepositoryRoot = string.Empty;
            CurrentBranch = "No repository";
            StatusText = "No Git repository";
            _allPendingCommits.Clear();
            _allPendingChangeLists.Clear();
            PendingCommits.Clear();
            SelectedPendingCommitChanges.Clear();
            SelectedPendingCommitChangesTitle = "Selected CL Changes";
            OnPropertyChanged(nameof(PendingCountText));
            AppendOutput($"No Git repository found for: {probePath}");
            return;
        }

        RepositoryRoot = root;
        await LoadStatusAsync(root);
        await LoadHistoryAsync(root, null);
        StatusText = "Ready";
    }

    private async Task InitRepositoryAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspacePath))
        {
            AppendOutput("No workspace selected.");
            return;
        }

        var workingDirectory = GitService.ToWorkingDirectory(WorkspacePath);
        if (workingDirectory is null || !Directory.Exists(workingDirectory))
        {
            AppendOutput($"Workspace does not exist: {WorkspacePath}");
            return;
        }

        StatusText = "Initializing repository";
        var result = await _git.RunAsync(workingDirectory, new[] { "init" });
        AppendCommand("Init Repository", workingDirectory, new[] { "init" }, result.StandardOutput, result.StandardError);
        await RefreshAsync();
    }

    private async Task LoadStatusAsync(string repositoryRoot)
    {
        var result = await _git.RunAsync(repositoryRoot, new[] { "status", "--short", "--branch" });
        AppendCommand("Status", repositoryRoot, new[] { "status", "--short", "--branch" }, result.StandardOutput, result.StandardError);

        if (!result.IsSuccess)
        {
            CurrentBranch = "Status failed";
            return;
        }

        var nextPendingCommits = new List<GitHistoryEntry>();
        var nextPendingChangeLists = new List<GitHistoryEntry>();

        foreach (var line in result.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CurrentBranch = line[3..];
                continue;
            }

            if (line.Length < 4)
            {
                continue;
            }

        }

        foreach (var commit in await _git.GetPendingCommitsAsync(repositoryRoot))
        {
            nextPendingCommits.Add(commit);
            nextPendingChangeLists.Add(commit);
        }

        var stagedChanges = (await _git.GetStatusAsync(repositoryRoot))
            .Select(change => new { Change = change, State = GetStagedChangeListState(change) })
            .Where(item => item.State is not null)
            .GroupBy(item => item.State!, item => item.Change)
            .ToArray();

        foreach (var group in stagedChanges)
        {
            nextPendingChangeLists.Add(GitHistoryEntry.FromChanges(group.Key, group.ToArray()));
        }

        _allPendingCommits.Clear();
        _allPendingCommits.AddRange(nextPendingCommits);
        _allPendingChangeLists.Clear();
        _allPendingChangeLists.AddRange(nextPendingChangeLists);
        ApplyPendingFilter();
    }

    private async Task LoadHistoryAsync(string repositoryRoot, string? path)
    {
        History.Clear();
        var localRevisions = _allPendingCommits
            .Select(commit => commit.Revision)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in await _git.GetHistoryAsync(repositoryRoot, path))
        {
            var publishState = localRevisions.Contains(entry.Revision) ? "Local" : "Remote";
            History.Add(entry.WithPublishState(publishState));
        }
    }

    private async Task LoadHistoryForSelectedAsync()
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        await LoadHistoryAsync(repositoryRoot, SelectedPath);
        AppendOutput($"History loaded for: {SelectedPath}");
    }

    public void ClearSelectedCommitChanges()
    {
        _commitChangesRequestVersion++;
        _selectedCommitChangesRevision = string.Empty;
        SelectedCommitChange = null;
        SelectedPendingCommitChanges.Clear();
        SelectedPendingCommitChangesTitle = "Selected CL Changes";
    }

    private async Task LoadSelectedCommitChangesAsync(GitHistoryEntry? commit)
    {
        var requestVersion = ++_commitChangesRequestVersion;
        _selectedCommitChangesRevision = commit is { IsCommitEntry: true } ? commit.Revision : string.Empty;
        SelectedCommitChange = null;
        SelectedPendingCommitChanges.Clear();

        if (commit is null)
        {
            SelectedPendingCommitChangesTitle = "Selected CL Changes";
            return;
        }

        if (commit.IsChangeEntry)
        {
            foreach (var change in commit.Changes)
            {
                SelectedPendingCommitChanges.Add(change);
            }

            SelectedPendingCommitChangesTitle = $"Selected CL Changes - {commit.Subject}";
            return;
        }

        SelectedPendingCommitChangesTitle = $"Selected CL Changes - {commit.ShortRevision}";

        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var selectedRevision = commit.Revision;
        var changes = await _git.GetCommitChangesAsync(repositoryRoot, selectedRevision);

        if (requestVersion != _commitChangesRequestVersion)
        {
            return;
        }

        foreach (var change in changes)
        {
            SelectedPendingCommitChanges.Add(change);
        }

        SelectedPendingCommitChangesTitle = $"Selected CL Changes - {commit.ShortRevision} ({changes.Count})";
    }

    public async Task<CommitChangeDiff?> GetSelectedCommitChangeDiffAsync()
    {
        if (SelectedCommitChange is null)
        {
            AppendOutput("No CL change selected.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_selectedCommitChangesRevision))
        {
            AppendOutput("No commit selected for compare.");
            return null;
        }

        return await GetCommitChangeDiffAsync(_selectedCommitChangesRevision, SelectedCommitChange);
    }

    public async Task<CommitChangeDiff?> GetCommitChangeDiffAsync(GitHistoryEntry commit, GitChange change)
    {
        return await GetCommitChangeDiffAsync(commit.Revision, change);
    }

    private async Task<CommitChangeDiff?> GetCommitChangeDiffAsync(string revision, GitChange change)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return null;
        }

        var args = new List<string>
        {
            "show",
            "--format=",
            "--find-renames",
            "--patch",
            "--unified=999999",
            revision,
            "--",
        };
        args.AddRange(GetPathspecs(change.Path));

        StatusText = "Compare CL change";
        var result = await _git.RunAsync(repositoryRoot, args);
        var diffText = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";

        if (string.IsNullOrWhiteSpace(diffText))
        {
            diffText = result.IsSuccess
                ? "No diff output for this file."
                : "Compare failed without output.";
        }

        StatusText = result.IsSuccess ? "Ready" : "Compare failed";
        var (leftPath, rightPath) = GetComparePaths(change.Path);
        return new CommitChangeDiff(
            revision,
            change.Path,
            leftPath,
            rightPath,
            change.Status,
            change.Details,
            diffText);
    }

    public async Task<GitHistoryEntry?> FindCommitByCommitAsync(string commitText)
    {
        if (string.IsNullOrWhiteSpace(commitText))
        {
            AppendOutput("Commit is empty.");
            return null;
        }

        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return null;
        }

        StatusText = $"Search Commit {commitText.Trim()}";
        var commit = await _git.GetCommitAsync(repositoryRoot, commitText.Trim());
        if (commit is null)
        {
            AppendOutput($"Commit not found: {commitText.Trim()}");
            StatusText = "Commit not found";
            return null;
        }

        StatusText = "Ready";
        return commit;
    }

    public async Task<IReadOnlyList<GitChange>> GetCommitChangesAsync(GitHistoryEntry commit)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return Array.Empty<GitChange>();
        }

        return await _git.GetCommitChangesAsync(repositoryRoot, commit.Revision);
    }

    public async Task RevertSelectedPendingCommitAsync()
    {
        if (SelectedPendingCommit is not { IsCommitEntry: true })
        {
            AppendOutput("No ChangeList selected.");
            return;
        }

        await RunSelectedPendingCommitCommandAsync(
            $"Revert {SelectedPendingCommit.ShortRevision}",
            new[] { "revert", SelectedPendingCommit.Revision },
            true);
    }

    public async Task RestoreSelectedPendingCommitAsync()
    {
        if (SelectedPendingCommit is not { IsCommitEntry: true })
        {
            AppendOutput("No ChangeList selected.");
            return;
        }

        await RunSelectedPendingCommitCommandAsync(
            $"Restore {SelectedPendingCommit.ShortRevision}",
            new[] { "revert", "--no-commit", SelectedPendingCommit.Revision },
            true);
    }

    public async Task DeleteSelectedHeadCommitAsync(bool keepChanges)
    {
        if (SelectedPendingCommit is not { IsCommitEntry: true })
        {
            AppendOutput("No ChangeList selected.");
            return;
        }

        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var head = await _git.RunAsync(repositoryRoot, new[] { "rev-parse", "HEAD" });
        if (!head.IsSuccess)
        {
            AppendCommand("Resolve HEAD", repositoryRoot, new[] { "rev-parse", "HEAD" }, head.StandardOutput, head.StandardError);
            return;
        }

        if (!string.Equals(head.StandardOutput.Trim(), SelectedPendingCommit.Revision, StringComparison.OrdinalIgnoreCase))
        {
            AppendOutput("Delete is limited to the current HEAD commit. Select the top ChangeList or use Revert for older commits.");
            return;
        }

        var parent = await _git.RunAsync(repositoryRoot, new[] { "rev-parse", "HEAD^" });
        if (!parent.IsSuccess)
        {
            AppendCommand("Resolve HEAD parent", repositoryRoot, new[] { "rev-parse", "HEAD^" }, parent.StandardOutput, parent.StandardError);
            AppendOutput("Cannot delete the root commit from the UI.");
            return;
        }

        var args = keepChanges
            ? new[] { "reset", "--mixed", "HEAD^" }
            : new[] { "reset", "--hard", "HEAD^" };
        var title = keepChanges
            ? $"Delete {SelectedPendingCommit.ShortRevision} (keep changes)"
            : $"Delete {SelectedPendingCommit.ShortRevision} (discard changes)";

        await RunSelectedPendingCommitCommandAsync(title, args, true);
    }

    public async Task CheckoutSelectedHistoryCommitAsync()
    {
        if (SelectedHistoryEntry is null)
        {
            AppendOutput("No history commit selected.");
            return;
        }

        await CheckoutCommitAsync(SelectedHistoryEntry);
    }

    public async Task CheckoutCommitAsync(GitHistoryEntry commit)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var revision = commit.Revision;
        StatusText = $"Checkout {commit.ShortRevision}";
        var result = await _git.RunAsync(repositoryRoot, new[] { "checkout", revision });
        AppendCommand(
            $"Checkout {commit.ShortRevision}",
            repositoryRoot,
            new[] { "checkout", revision },
            result.StandardOutput,
            result.StandardError);

        await RefreshAsync();
        StatusText = result.IsSuccess ? "Ready" : "Checkout failed";
    }

    public async Task<IReadOnlyList<GitChange>> GetWorkingTreeChangesAsync()
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return Array.Empty<GitChange>();
        }

        var changes = await _git.GetStatusAsync(repositoryRoot);
        if (changes.Count == 0)
        {
            AppendOutput("No added or changed files.");
        }

        return changes;
    }

    public async Task<bool> CommitSelectedFilesAsync(
        string message,
        IReadOnlyList<CommitFileSelection> selectedFiles)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            AppendOutput("Commit message is empty.");
            return false;
        }

        var pathspecs = selectedFiles
            .SelectMany(file => file.Pathspecs)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (pathspecs.Length == 0)
        {
            AppendOutput("No files selected for commit.");
            return false;
        }

        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return false;
        }

        StatusText = "Commit";

        var untrackedPathspecs = selectedFiles
            .Where(file => file.Status == "??")
            .SelectMany(file => file.Pathspecs)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (untrackedPathspecs.Length > 0)
        {
            var addArgs = new List<string> { "add", "--" };
            addArgs.AddRange(untrackedPathspecs);
            var addResult = await _git.RunAsync(repositoryRoot, addArgs);
            AppendCommand("Stage untracked files", repositoryRoot, addArgs, addResult.StandardOutput, addResult.StandardError);

            if (!addResult.IsSuccess)
            {
                await RefreshAsync();
                StatusText = "Commit failed";
                return false;
            }
        }

        var commitArgs = new List<string> { "commit", "-m", message.Trim(), "--" };
        commitArgs.AddRange(pathspecs);
        var commitResult = await _git.RunAsync(repositoryRoot, commitArgs);
        AppendCommand("Commit selected files", repositoryRoot, commitArgs, commitResult.StandardOutput, commitResult.StandardError);

        await RefreshAsync();
        StatusText = commitResult.IsSuccess ? "Ready" : "Commit failed";
        return commitResult.IsSuccess;
    }

    public async Task PushToOriginAsync()
    {
        await RunRepositoryCommandAsync("Push to origin", new[] { "push", "origin", "HEAD" }, true);
    }

    private async Task RunSelectedPendingCommitCommandAsync(string title, IReadOnlyList<string> arguments, bool refreshAfter)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        StatusText = title;
        var result = await _git.RunAsync(repositoryRoot, arguments);
        AppendCommand(title, repositoryRoot, arguments, result.StandardOutput, result.StandardError);

        if (refreshAfter)
        {
            await RefreshAsync();
        }

        StatusText = result.IsSuccess ? "Ready" : $"{title} failed";
    }

    private async Task RunRepositoryCommandAsync(string title, IReadOnlyList<string> arguments, bool refreshAfter)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        StatusText = title;
        var result = await _git.RunAsync(repositoryRoot, arguments);
        AppendCommand(title, repositoryRoot, arguments, result.StandardOutput, result.StandardError);

        if (refreshAfter)
        {
            await RefreshAsync();
        }

        StatusText = result.IsSuccess ? "Ready" : $"{title} failed";
    }

    private async Task RunPathCommandAsync(string title, IReadOnlyList<string> baseArguments, bool refreshAfter)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var args = baseArguments.ToList();
        GitService.AddPathspec(args, repositoryRoot, SelectedPath);

        StatusText = title;
        var result = await _git.RunAsync(repositoryRoot, args);
        AppendCommand(title, repositoryRoot, args, result.StandardOutput, result.StandardError);

        if (refreshAfter)
        {
            await RefreshAsync();
        }

        StatusText = result.IsSuccess ? "Ready" : $"{title} failed";
    }

    private async Task ShowDiffAsync()
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var args = new List<string> { "diff" };
        GitService.AddPathspec(args, repositoryRoot, SelectedPath);
        var result = await _git.RunAsync(repositoryRoot, args);

        if (result.IsSuccess && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            args = new List<string> { "diff", "--cached" };
            GitService.AddPathspec(args, repositoryRoot, SelectedPath);
            result = await _git.RunAsync(repositoryRoot, args);
        }

        AppendCommand("Diff", repositoryRoot, args, result.StandardOutput, result.StandardError);
    }

    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            AppendOutput("Commit message is empty.");
            return;
        }

        var changes = await GetWorkingTreeChangesAsync();
        var selectedFiles = changes.Select(change => new CommitFileSelection(change)).ToArray();
        if (await CommitSelectedFilesAsync(CommitMessage, selectedFiles))
        {
            CommitMessage = string.Empty;
        }
    }

    private Task OpenSelectedFolderAsync()
    {
        var path = !string.IsNullOrWhiteSpace(SelectedPath) ? SelectedPath : WorkspacePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendOutput("No workspace selected.");
            return Task.CompletedTask;
        }

        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            AppendOutput($"Folder does not exist: {path}");
            return Task.CompletedTask;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", folder);
            }
            else
            {
                Process.Start("xdg-open", folder);
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Open folder failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task<string?> ResolveRepositoryRootAsync()
    {
        if (!string.IsNullOrWhiteSpace(RepositoryRoot) && Directory.Exists(RepositoryRoot))
        {
            return RepositoryRoot;
        }

        var probePath = !string.IsNullOrWhiteSpace(SelectedPath) ? SelectedPath : WorkspacePath;
        if (string.IsNullOrWhiteSpace(probePath))
        {
            AppendOutput("No workspace selected.");
            return null;
        }

        var root = await _git.FindRepositoryRootAsync(probePath);
        if (root is null)
        {
            AppendOutput($"No Git repository found for: {probePath}");
        }

        RepositoryRoot = root ?? string.Empty;
        return root;
    }

    private void ReplaceWorkspaceHistory(IReadOnlyList<string> history, string? selectedPath)
    {
        WorkspaceHistory.Clear();
        foreach (var item in history)
        {
            WorkspaceHistory.Add(item);
        }

        SelectedWorkspaceHistory = selectedPath is null
            ? null
            : WorkspaceHistory.FirstOrDefault(item => PathEquals(item, selectedPath));
    }

    private void AppendCommand(
        string title,
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        string stdout,
        string stderr)
    {
        AppendOutput(string.Empty);
        AppendOutput($"[{DateTime.Now:HH:mm:ss}] {title}");
        AppendOutput($"> git -C \"{repositoryRoot}\" {string.Join(' ', arguments)}");

        foreach (var line in SplitOutput(stdout))
        {
            AppendOutput(line);
        }

        foreach (var line in SplitOutput(stderr))
        {
            AppendOutput(line);
        }
    }

    private void AppendOutput(string line)
    {
        OutputLines.Add(line);
        while (OutputLines.Count > 600)
        {
            OutputLines.RemoveAt(0);
        }
    }

    private void ApplyPendingFilter()
    {
        PendingCommits.Clear();

        var filter = FilterText.Trim();
        var visibleCommits = string.IsNullOrWhiteSpace(filter)
            ? _allPendingChangeLists
            : _allPendingChangeLists.Where(commit =>
                commit.Revision.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                commit.Author.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                commit.Date.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                commit.Subject.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                commit.ChangeListState.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var commit in visibleCommits)
        {
            PendingCommits.Add(commit);
        }

        OnPropertyChanged(nameof(PendingCountText));
    }

    private static string? GetStagedChangeListState(GitChange change)
    {
        if (string.IsNullOrWhiteSpace(change.Status))
        {
            return null;
        }

        return change.Status[0] switch
        {
            'A' => "Staged Added",
            'M' => "Staged Modified",
            'D' => "Staged Deleted",
            'R' => "Staged Renamed",
            'C' => "Staged Copied",
            'T' => "Staged Type changed",
            'U' => "Staged Unmerged",
            _ => null,
        };
    }

    private static IEnumerable<string> SplitOutput(string text)
    {
        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> GetPathspecs(string path)
    {
        const string renameMarker = " -> ";
        if (!path.Contains(renameMarker, StringComparison.Ordinal))
        {
            return new[] { path };
        }

        return path
            .Split(new[] { renameMarker }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
    }

    private static (string leftPath, string rightPath) GetComparePaths(string path)
    {
        const string renameMarker = " -> ";
        if (!path.Contains(renameMarker, StringComparison.Ordinal))
        {
            return (path, path);
        }

        var parts = path
            .Split(new[] { renameMarker }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length >= 2 ? (parts[0], parts[1]) : (path, path);
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        return comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    }
}
