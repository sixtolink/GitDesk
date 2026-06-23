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

    public event Func<Task>? GitHubAuthenticationRequested;

    public MainWindowViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        InitRepositoryCommand = new AsyncRelayCommand(_ => InitRepositoryAsync());
        FetchCommand = new AsyncRelayCommand(_ => RunRepositoryCommandAsync("Fetch", new[] { "fetch", "--all", "--prune" }, true));
        PullCommand = new AsyncRelayCommand(_ => PullAsync());
        CommitCommand = new AsyncRelayCommand(_ => CommitAsync());
        OpenSelectedFolderCommand = new AsyncRelayCommand(_ => OpenSelectedFolderAsync());
        RevertPendingCommitCommand = new AsyncRelayCommand(_ => RevertSelectedPendingCommitAsync());
        RestorePendingCommitCommand = new AsyncRelayCommand(_ => RestoreSelectedPendingCommitAsync());
        DeleteHeadPendingCommitKeepChangesCommand = new AsyncRelayCommand(_ => DeleteSelectedHeadCommitAsync(keepChanges: true));
        DeleteHeadPendingCommitDiscardChangesCommand = new AsyncRelayCommand(_ => DeleteSelectedHeadCommitAsync(keepChanges: false));
        AddSelectedCommand = new AsyncRelayCommand(_ => RunPathCommandAsync("Add", new[] { "add" }, true));
        RevertSelectedCommand = new AsyncRelayCommand(_ => RevertSelectedPathAsync());
        DeleteSelectedKeepLocalCommand = new AsyncRelayCommand(_ => DeleteSelectedPathAsync(keepLocal: true));
        DeleteSelectedDeleteLocalCommand = new AsyncRelayCommand(_ => DeleteSelectedPathAsync(keepLocal: false));
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

    public string OutputText => string.Join(Environment.NewLine, OutputLines);

    public void NotifyOutputTextChanged()
    {
        OnPropertyChanged(nameof(OutputText));
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand InitRepositoryCommand { get; }

    public AsyncRelayCommand FetchCommand { get; }

    public AsyncRelayCommand PullCommand { get; }

    public AsyncRelayCommand CommitCommand { get; }

    public AsyncRelayCommand OpenSelectedFolderCommand { get; }

    public AsyncRelayCommand RevertPendingCommitCommand { get; }

    public AsyncRelayCommand RestorePendingCommitCommand { get; }

    public AsyncRelayCommand DeleteHeadPendingCommitKeepChangesCommand { get; }

    public AsyncRelayCommand DeleteHeadPendingCommitDiscardChangesCommand { get; }

    public AsyncRelayCommand AddSelectedCommand { get; }

    public AsyncRelayCommand RevertSelectedCommand { get; }

    public AsyncRelayCommand DeleteSelectedKeepLocalCommand { get; }

    public AsyncRelayCommand DeleteSelectedDeleteLocalCommand { get; }

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
                NotifySelectedPendingCommitActionStateChanged();
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
        set
        {
            if (SetProperty(ref _selectedCommitChange, value))
            {
                NotifySelectedCommitChangeActionStateChanged();
            }
        }
    }

    public string PendingCountText => $"{PendingCommits.Count} ChangeLists";

    public bool CanOperateSelectedPendingCommit => SelectedPendingCommit is { IsCommitEntry: true };

    public bool CanCancelSelectedStagedAdd => IsSelectedChangeListState("Staged Added");

    public bool CanCancelSelectedStagedDelete => IsSelectedChangeListState("Staged Deleted");

    public bool CanRevertSelectedStagedModified => IsSelectedChangeListState("Staged Modified");

    public bool CanCancelSelectedCLChangeAdd => SelectedCommitChange is not null && CanCancelSelectedStagedAdd;

    public bool CanCancelSelectedCLChangeDelete => SelectedCommitChange is not null && CanCancelSelectedStagedDelete;

    public bool CanRevertSelectedCLChangeModified => SelectedCommitChange is not null && CanRevertSelectedStagedModified;

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
        var statusOutput = result.IsSuccess ? _git.FormatStatusOutput(result.StandardOutput) : result.StandardOutput;
        AppendCommand("Status", repositoryRoot, new[] { "status", "--short", "--branch" }, statusOutput, result.StandardError);

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
                CurrentBranch = line == "## HEAD (no branch)"
                    ? "Detached HEAD (no branch)"
                    : line[3..];
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

    public async Task CancelSelectedStagedAddAsync()
    {
        await RunSelectedChangeListCommandAsync(
            "Cancel Add",
            "Staged Added",
            new[] { "restore", "--staged" });
    }

    public async Task CancelSelectedStagedDeleteAsync()
    {
        await RunSelectedChangeListCommandAsync(
            "Cancel Delete",
            "Staged Deleted",
            new[] { "restore", "--staged", "--worktree" });
    }

    public async Task RevertSelectedStagedModifiedAsync()
    {
        await RunSelectedChangeListCommandAsync(
            "Revert Modified",
            "Staged Modified",
            new[] { "restore", "--staged", "--worktree" });
    }

    public async Task CancelSelectedCLChangeAddAsync()
    {
        await RunSelectedChangeCommandAsync(
            "Cancel Add",
            "Staged Added",
            new[] { "restore", "--staged" });
    }

    public async Task CancelSelectedCLChangeDeleteAsync()
    {
        await RunSelectedChangeCommandAsync(
            "Cancel Delete",
            "Staged Deleted",
            new[] { "restore", "--staged", "--worktree" });
    }

    public async Task RevertSelectedCLChangeModifiedAsync()
    {
        await RunSelectedChangeCommandAsync(
            "Revert Modified",
            "Staged Modified",
            new[] { "restore", "--staged", "--worktree" });
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
        var currentBranch = await _git.GetCurrentBranchNameAsync(repositoryRoot);
        if (string.IsNullOrWhiteSpace(currentBranch))
        {
            if (await RecoverDetachedHeadAsync(repositoryRoot, revision))
            {
                currentBranch = await _git.GetCurrentBranchNameAsync(repositoryRoot);
                if (string.IsNullOrWhiteSpace(currentBranch))
                {
                    StatusText = "Checkout failed";
                    return;
                }
            }
            else
            {
                AppendOutput("Cannot checkout commit on a branch because repository is currently detached and no containing local or remote branch was found.");
                AppendOutput("Checkout a local branch first, then checkout the commit again.");
                StatusText = "Checkout failed";
                return;
            }
        }

        StatusText = $"Checkout {commit.ShortRevision}";
        var args = new[] { "checkout", "-B", currentBranch, revision };
        var result = await _git.RunAsync(repositoryRoot, args);
        AppendCommand(
            $"Checkout {commit.ShortRevision} on {currentBranch}",
            repositoryRoot,
            args,
            result.StandardOutput,
            result.StandardError);

        if (result.IsSuccess)
        {
            var upstream = await _git.GetUpstreamBranchNameAsync(repositoryRoot);
            if (string.IsNullOrWhiteSpace(upstream))
            {
                var setUpstreamArgs = new[] { "branch", "--set-upstream-to", $"origin/{currentBranch}", currentBranch };
                var setUpstreamResult = await _git.RunAsync(repositoryRoot, setUpstreamArgs);
                AppendCommand("Set branch upstream", repositoryRoot, setUpstreamArgs, setUpstreamResult.StandardOutput, setUpstreamResult.StandardError);
            }
        }

        await RefreshAsync();
        StatusText = result.IsSuccess ? "Ready" : "Checkout failed";
    }

    private async Task<bool> CheckoutBranchAsync(string repositoryRoot, string branch)
    {
        StatusText = $"Checkout {branch}";
        var args = new[] { "checkout", branch };
        var result = await _git.RunAsync(repositoryRoot, args);
        AppendCommand($"Checkout {branch}", repositoryRoot, args, result.StandardOutput, result.StandardError);

        await RefreshAsync();
        StatusText = result.IsSuccess ? "Ready" : "Checkout failed";
        return result.IsSuccess;
    }

    private async Task<bool> CheckoutBranchFromRemoteAsync(string repositoryRoot, string localBranch, string remoteBranch)
    {
        StatusText = $"Checkout {localBranch}";
        var args = new[] { "checkout", "-B", localBranch, "--track", remoteBranch };
        var result = await _git.RunAsync(repositoryRoot, args);
        AppendCommand($"Checkout {localBranch} from {remoteBranch}", repositoryRoot, args, result.StandardOutput, result.StandardError);

        if (!result.IsSuccess)
        {
            var fallbackArgs = new[] { "checkout", "-B", localBranch, remoteBranch };
            result = await _git.RunAsync(repositoryRoot, fallbackArgs);
            AppendCommand($"Checkout {localBranch} from {remoteBranch}", repositoryRoot, fallbackArgs, result.StandardOutput, result.StandardError);
        }

        if (result.IsSuccess)
        {
            var upstreamArgs = new[] { "branch", "--set-upstream-to", remoteBranch, localBranch };
            var upstreamResult = await _git.RunAsync(repositoryRoot, upstreamArgs);
            AppendCommand("Set branch upstream", repositoryRoot, upstreamArgs, upstreamResult.StandardOutput, upstreamResult.StandardError);
        }

        await RefreshAsync();
        StatusText = result.IsSuccess ? "Ready" : "Checkout failed";
        return result.IsSuccess;
    }

    private async Task PullAsync()
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var currentBranch = await _git.GetCurrentBranchNameAsync(repositoryRoot);
        if (string.IsNullOrWhiteSpace(currentBranch))
        {
            if (!await RecoverDetachedHeadAsync(repositoryRoot, "HEAD"))
            {
                AppendOutput("Cannot pull from detached HEAD. No containing local or remote branch was found.");
                StatusText = "Pull failed";
                return;
            }
        }

        await RunRepositoryCommandAsync("Pull", new[] { "pull", "--ff-only" }, true);
    }

    private async Task<bool> RecoverDetachedHeadAsync(string repositoryRoot, string revision)
    {
        var containingLocalBranch = (await _git.GetLocalBranchesContainingAsync(repositoryRoot, revision)).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(containingLocalBranch))
        {
            AppendOutput($"Repository is detached. Checking out local branch: {containingLocalBranch}");
            return await CheckoutBranchAsync(repositoryRoot, containingLocalBranch);
        }

        var containingRemoteBranch = (await _git.GetRemoteBranchesContainingAsync(repositoryRoot, revision)).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(containingRemoteBranch))
        {
            return false;
        }

        var localBranch = GetLocalBranchNameFromRemote(containingRemoteBranch);
        AppendOutput($"Repository is detached. Restoring branch {localBranch} from {containingRemoteBranch}.");
        return await CheckoutBranchFromRemoteAsync(repositoryRoot, localBranch, containingRemoteBranch);
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

    public async Task<GitHubSettings> LoadGitHubSettingsAsync()
    {
        GitHubSettings settings;
        try
        {
            settings = await _workspaceStore.LoadGitHubSettingsAsync();
        }
        catch (Exception ex)
        {
            AppendOutput($"Load GitHub settings failed: {ex.Message}");
            settings = GitHubSettings.Default;
        }

        var workingDirectory = GetGitCommandWorkingDirectory();
        var gitUserName = settings.GitUserName;
        var gitUserEmail = settings.GitUserEmail;

        if (string.IsNullOrWhiteSpace(gitUserName))
        {
            var result = await _git.GetConfigValueAsync(workingDirectory, "user.name", global: true);
            if (result.IsSuccess)
            {
                gitUserName = result.StandardOutput.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(gitUserEmail))
        {
            var result = await _git.GetConfigValueAsync(workingDirectory, "user.email", global: true);
            if (result.IsSuccess)
            {
                gitUserEmail = result.StandardOutput.Trim();
            }
        }

        return settings with
        {
            GitUserName = gitUserName,
            GitUserEmail = gitUserEmail,
        };
    }

    public async Task<string> GetCurrentOriginUrlAsync()
    {
        var repositoryRoot = !string.IsNullOrWhiteSpace(RepositoryRoot) && Directory.Exists(RepositoryRoot)
            ? RepositoryRoot
            : await _git.FindRepositoryRootAsync(!string.IsNullOrWhiteSpace(SelectedPath) ? SelectedPath : WorkspacePath);

        return repositoryRoot is null
            ? string.Empty
            : await _git.GetOriginUrlAsync(repositoryRoot);
    }

    public async Task SaveGitHubSettingsAsync(
        GitHubSettings settings,
        string token,
        bool configureGitIdentity,
        bool saveCredential,
        bool removeStoredCredential,
        bool convertOriginToHttps)
    {
        var normalized = settings.Normalized();
        var workingDirectory = GetGitCommandWorkingDirectory();
        var hasStoredCredential = normalized.HasStoredCredential;

        if (configureGitIdentity)
        {
            if (!string.IsNullOrWhiteSpace(normalized.GitUserName))
            {
                var result = await _git.SetGlobalConfigValueAsync(workingDirectory, "user.name", normalized.GitUserName);
                AppendCommand("Set git user.name", workingDirectory, new[] { "config", "--global", "user.name", normalized.GitUserName }, result.StandardOutput, result.StandardError);
            }

            if (!string.IsNullOrWhiteSpace(normalized.GitUserEmail))
            {
                var result = await _git.SetGlobalConfigValueAsync(workingDirectory, "user.email", normalized.GitUserEmail);
                AppendCommand("Set git user.email", workingDirectory, new[] { "config", "--global", "user.email", normalized.GitUserEmail }, result.StandardOutput, result.StandardError);
            }
        }

        if (removeStoredCredential)
        {
            var result = await _git.RejectCredentialAsync(workingDirectory, normalized.Host, normalized.Username);
            AppendOutput(result.IsSuccess
                ? $"Removed GitHub credential for {normalized.Username}@{normalized.Host}."
                : $"Remove GitHub credential failed: {FirstOutputLine(result.StandardError, result.StandardOutput)}");
            hasStoredCredential = result.IsSuccess ? false : hasStoredCredential;
        }

        if (saveCredential && !string.IsNullOrWhiteSpace(token))
        {
            var result = await _git.StoreCredentialAsync(workingDirectory, normalized.Host, normalized.Username, token);
            AppendOutput(result.IsSuccess
                ? $"Stored GitHub credential for {normalized.Username}@{normalized.Host} with Git Credential Manager."
                : $"Store GitHub credential failed: {FirstOutputLine(result.StandardError, result.StandardOutput)}");
            hasStoredCredential = result.IsSuccess;
        }

        if (convertOriginToHttps)
        {
            var repositoryRoot = await ResolveRepositoryRootAsync();
            if (repositoryRoot is not null)
            {
                var result = await _git.ConvertOriginToHttpsAsync(repositoryRoot, normalized.Host);
                AppendCommand("Convert origin to HTTPS", repositoryRoot, new[] { "remote", "set-url", "origin", "https://..." }, result.StandardOutput, result.StandardError);
            }
        }

        var nextSettings = normalized with { HasStoredCredential = hasStoredCredential };
        try
        {
            await _workspaceStore.SaveGitHubSettingsAsync(nextSettings);
            AppendOutput($"Saved GitHub settings for {nextSettings.Host}.");
        }
        catch (Exception ex)
        {
            AppendOutput($"Save GitHub settings failed: {ex.Message}");
        }
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
        var branch = await GetCurrentBranchNameAsync();
        if (string.IsNullOrWhiteSpace(branch))
        {
            AppendOutput("Cannot push from detached HEAD. Checkout a branch before pushing.");
            StatusText = "Push failed";
            return;
        }

        await PushBranchToOriginAsync(branch);
    }

    public async Task<string?> GetCurrentBranchNameAsync()
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return null;
        }

        return await _git.GetCurrentBranchNameAsync(repositoryRoot);
    }

    public async Task<IReadOnlyList<string>> GetLocalBranchesAsync()
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return Array.Empty<string>();
        }

        var branches = await _git.GetLocalBranchesAsync(repositoryRoot);
        if (branches.Count == 0)
        {
            AppendOutput("No local branches found.");
        }

        var currentBranch = await _git.GetCurrentBranchNameAsync(repositoryRoot);
        return string.IsNullOrWhiteSpace(currentBranch)
            ? branches
            : branches
                .OrderByDescending(branch => string.Equals(branch, currentBranch, StringComparison.Ordinal))
                .ThenBy(branch => branch, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    public async Task PushBranchToOriginAsync(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            AppendOutput("No branch selected.");
            StatusText = "Push failed";
            return;
        }

        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var branches = await _git.GetLocalBranchesAsync(repositoryRoot);
        if (!branches.Contains(branch, StringComparer.Ordinal))
        {
            AppendOutput($"Local branch does not exist: {branch}");
            StatusText = "Push failed";
            return;
        }

        await RunRepositoryCommandAsync(
            $"Push {branch}",
            new[] { "push", "-u", "origin", $"refs/heads/{branch}:refs/heads/{branch}" },
            true);
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

    private async Task RunSelectedChangeListCommandAsync(
        string title,
        string expectedState,
        IReadOnlyList<string> baseArguments)
    {
        if (SelectedPendingCommit is not { IsChangeEntry: true } changeList ||
            !string.Equals(changeList.ChangeListState, expectedState, StringComparison.Ordinal))
        {
            AppendOutput($"{title} is only available for {expectedState} ChangeLists.");
            return;
        }

        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var pathspecs = changeList.Changes
            .SelectMany(change => GetPathspecs(change.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (pathspecs.Length == 0)
        {
            AppendOutput($"{title} has no paths to operate on.");
            return;
        }

        var args = baseArguments.ToList();
        args.Add("--");
        args.AddRange(pathspecs);

        StatusText = title;
        var result = await _git.RunAsync(repositoryRoot, args);
        AppendCommand(title, repositoryRoot, args, result.StandardOutput, result.StandardError);

        await RefreshAsync();
        StatusText = result.IsSuccess ? "Ready" : $"{title} failed";
    }

    private async Task RunSelectedChangeCommandAsync(
        string title,
        string expectedState,
        IReadOnlyList<string> baseArguments)
    {
        if (SelectedCommitChange is null)
        {
            AppendOutput($"No file selected for {title}.");
            return;
        }

        if (!IsSelectedChangeListState(expectedState))
        {
            AppendOutput($"{title} is only available for {expectedState} ChangeLists.");
            return;
        }

        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        var pathspecs = GetPathspecs(SelectedCommitChange.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (pathspecs.Length == 0)
        {
            AppendOutput($"{title} has no paths to operate on.");
            return;
        }

        var args = baseArguments.ToList();
        args.Add("--");
        args.AddRange(pathspecs);

        StatusText = title;
        var result = await _git.RunAsync(repositoryRoot, args);
        AppendCommand(title, repositoryRoot, args, result.StandardOutput, result.StandardError);

        await RefreshAsync();
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
        var result = await _git.RunAuthenticatedAsync(repositoryRoot, arguments);
        AppendCommand(title, repositoryRoot, arguments, result.StandardOutput, result.StandardError);

        if (GitService.IsAuthenticationFailure(result))
        {
            await HandleAuthenticationFailureAsync(result);
        }

        if (refreshAfter)
        {
            await RefreshAsync();
        }

        StatusText = result.IsSuccess ? "Ready" : $"{title} failed";
    }

    private async Task HandleAuthenticationFailureAsync(GitCommandResult result)
    {
        if (GitService.IsSshPublicKeyFailure(result))
        {
            AppendOutput("Authentication failed for an SSH remote. GitHub tokens only work with HTTPS remotes; open Settings and convert origin to HTTPS, or configure an SSH key.");
        }
        else
        {
            AppendOutput("Authentication failed. Opening Settings so GitHub credentials can be configured.");
        }

        var handler = GitHubAuthenticationRequested;
        if (handler is not null)
        {
            await handler();
        }
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

    private async Task RevertSelectedPathAsync()
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPath))
        {
            AppendOutput("No path selected.");
            return;
        }

        var fullPath = Path.GetFullPath(SelectedPath);
        if (PathEquals(fullPath, repositoryRoot))
        {
            AppendOutput("Cannot revert the repository root from the tree menu.");
            return;
        }

        var restoreArgs = new List<string> { "restore", "--staged", "--worktree" };
        GitService.AddPathspec(restoreArgs, repositoryRoot, SelectedPath);

        StatusText = "Revert";
        var restoreResult = await _git.RunAsync(repositoryRoot, restoreArgs);
        AppendCommand("Revert", repositoryRoot, restoreArgs, restoreResult.StandardOutput, restoreResult.StandardError);

        if (!restoreResult.IsSuccess && IsUnknownPathspec(restoreResult))
        {
            await CleanUnknownSelectedPathAsync(repositoryRoot);
            await RefreshAsync();
            StatusText = "Ready";
            return;
        }

        await RefreshAsync();
        StatusText = restoreResult.IsSuccess ? "Ready" : "Revert failed";
    }

    private async Task CleanUnknownSelectedPathAsync(string repositoryRoot)
    {
        var cleanArgs = new List<string> { "clean", "-fd" };
        GitService.AddPathspec(cleanArgs, repositoryRoot, SelectedPath);
        var cleanResult = await _git.RunAsync(repositoryRoot, cleanArgs);
        AppendCommand("Clean untracked path", repositoryRoot, cleanArgs, cleanResult.StandardOutput, cleanResult.StandardError);

        if (cleanResult.IsSuccess && !PathExists(SelectedPath))
        {
            RefreshWorkspaceTree();
            return;
        }

        var cleanIgnoredArgs = new List<string> { "clean", "-fdX" };
        GitService.AddPathspec(cleanIgnoredArgs, repositoryRoot, SelectedPath);
        var cleanIgnoredResult = await _git.RunAsync(repositoryRoot, cleanIgnoredArgs);
        AppendCommand("Clean ignored path", repositoryRoot, cleanIgnoredArgs, cleanIgnoredResult.StandardOutput, cleanIgnoredResult.StandardError);

        if (cleanIgnoredResult.IsSuccess)
        {
            RefreshWorkspaceTree();
        }
    }

    private async Task DeleteSelectedPathAsync(bool keepLocal)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync();
        if (repositoryRoot is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPath))
        {
            AppendOutput("No path selected.");
            return;
        }

        var fullPath = Path.GetFullPath(SelectedPath);
        if (PathEquals(fullPath, repositoryRoot))
        {
            AppendOutput("Cannot delete the repository root.");
            return;
        }

        var args = keepLocal
            ? new List<string> { "rm", "-r", "--cached" }
            : new List<string> { "rm", "-r" };
        GitService.AddPathspec(args, repositoryRoot, SelectedPath);

        var title = keepLocal ? "Delete selected path, keep local" : "Delete selected path and local files";
        StatusText = title;
        var result = await _git.RunAsync(repositoryRoot, args);
        AppendCommand(title, repositoryRoot, args, result.StandardOutput, result.StandardError);

        if (!result.IsSuccess && IsUnknownPathspec(result))
        {
            if (keepLocal)
            {
                AppendOutput($"Path is not tracked by Git; keeping local requires no delete: {SelectedPath}");
                await RefreshAsync();
                StatusText = "Ready";
                return;
            }

            await CleanUnknownSelectedPathAsync(repositoryRoot);
            await RefreshAsync();
            StatusText = PathExists(SelectedPath) ? $"{title} failed" : "Ready";
            return;
        }

        if (!keepLocal && result.IsSuccess)
        {
            RefreshWorkspaceTree();
        }

        await RefreshAsync();
        StatusText = result.IsSuccess ? "Ready" : $"{title} failed";
    }

    private void RefreshWorkspaceTree()
    {
        foreach (var root in Roots)
        {
            root.Refresh();
        }
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

    private string GetGitCommandWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(RepositoryRoot) && Directory.Exists(RepositoryRoot))
        {
            return RepositoryRoot;
        }

        if (!string.IsNullOrWhiteSpace(WorkspacePath) && Directory.Exists(WorkspacePath))
        {
            return WorkspacePath;
        }

        return AppContext.BaseDirectory;
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

        OnPropertyChanged(nameof(OutputText));
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

    private void NotifySelectedPendingCommitActionStateChanged()
    {
        OnPropertyChanged(nameof(CanOperateSelectedPendingCommit));
        OnPropertyChanged(nameof(CanCancelSelectedStagedAdd));
        OnPropertyChanged(nameof(CanCancelSelectedStagedDelete));
        OnPropertyChanged(nameof(CanRevertSelectedStagedModified));
        NotifySelectedCommitChangeActionStateChanged();
    }

    private void NotifySelectedCommitChangeActionStateChanged()
    {
        OnPropertyChanged(nameof(CanCancelSelectedCLChangeAdd));
        OnPropertyChanged(nameof(CanCancelSelectedCLChangeDelete));
        OnPropertyChanged(nameof(CanRevertSelectedCLChangeModified));
    }

    private bool IsSelectedChangeListState(string state)
    {
        return SelectedPendingCommit is { IsChangeEntry: true } changeList &&
               string.Equals(changeList.ChangeListState, state, StringComparison.Ordinal);
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

    private static string FirstOutputLine(string first, string second)
    {
        return SplitOutput(first).Concat(SplitOutput(second)).FirstOrDefault() ?? "No output.";
    }

    private static bool IsUnknownPathspec(GitCommandResult result)
    {
        var output = $"{result.StandardOutput}\n{result.StandardError}";
        return output.Contains("pathspec", StringComparison.OrdinalIgnoreCase) &&
               output.Contains("did not match any file", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    private static string GetLocalBranchNameFromRemote(string remoteBranch)
    {
        var slashIndex = remoteBranch.IndexOf('/');
        return slashIndex >= 0 && slashIndex + 1 < remoteBranch.Length
            ? remoteBranch[(slashIndex + 1)..]
            : remoteBranch;
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
