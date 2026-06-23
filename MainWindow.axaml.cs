using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GitDesk.Models;
using GitDesk.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GitDesk;

public partial class MainWindow : Window
{
    private bool _isSettingsDialogOpen;
    private bool _suppressWorkspaceTreeHistoryLoad;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        viewModel.GitHubAuthenticationRequested += ShowSettingsDialogAsync;
        DataContext = viewModel;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await ViewModel.RunBusyAsync("Loading workspace", ViewModel.InitializeWorkspaceHistoryAsync);
    }

    private async void OnBrowseWorkspace(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
        {
            await ViewModel.RunBusyAsync("Opening workspace", () => ViewModel.OpenWorkspaceAsync(folder));
        }
    }

    private async void OnWorkspaceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsOpeningWorkspace)
        {
            return;
        }

        if (WorkspaceSelector.SelectedItem is string path)
        {
            await ViewModel.RunBusyAsync("Opening workspace", () => ViewModel.OpenWorkspaceFromHistoryAsync(path));
        }
    }

    private void OnWorkspaceTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(WorkspaceTree).Properties.IsRightButtonPressed)
        {
            return;
        }

        var node = FindNodeFromSource(e.Source);
        if (node is null || node.IsPlaceholder)
        {
            return;
        }

        _suppressWorkspaceTreeHistoryLoad = true;
        try
        {
            WorkspaceTree.SelectedItem = node;
            ViewModel.SelectedNode = node;
        }
        finally
        {
            _suppressWorkspaceTreeHistoryLoad = false;
        }
    }

    private async void OnWorkspaceTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWorkspaceTreeHistoryLoad)
        {
            return;
        }

        if (WorkspaceTree.SelectedItem is not FileSystemNode node || node.IsPlaceholder)
        {
            return;
        }

        ViewModel.SelectedNode = node;
        SelectHistoryTab();
        if (ViewModel.IsHistoryLoadedForSelectedPath())
        {
            return;
        }

        await ViewModel.RunBusyAsync("Loading history", ViewModel.LoadHistoryForSelectedAsync);
    }

    private void OnHistoryGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(HistoryGrid).Properties.IsRightButtonPressed)
        {
            return;
        }

        var entry = FindDataContextFromSource<GitHistoryEntry>(e.Source);
        if (entry is null)
        {
            return;
        }

        HistoryGrid.SelectedItem = entry;
        ViewModel.SelectedHistoryEntry = entry;
    }

    private void OnPendingGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PendingGrid).Properties.IsRightButtonPressed)
        {
            return;
        }

        var entry = FindDataContextFromSource<GitHistoryEntry>(e.Source);
        if (entry is null)
        {
            return;
        }

        PendingGrid.SelectedItem = entry;
        ViewModel.SelectedPendingCommit = entry;
        UpdatePendingContextMenu();
    }

    private void OnCLChangesGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CLChangesGrid).Properties.IsRightButtonPressed)
        {
            return;
        }

        var change = FindDataContextFromSource<GitChange>(e.Source);
        if (change is null)
        {
            return;
        }

        CLChangesGrid.SelectedItem = change;
        ViewModel.SelectedCommitChange = change;
        UpdateCLChangesContextMenu();
    }

    private void OnMainCommitTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender != MainCommitTabControl)
        {
            return;
        }

        if (MainCommitTabControl.SelectedItem is TabItem { Header: "ChangeLists" })
        {
            HistoryGrid.SelectedItem = null;
            ViewModel.SelectedHistoryEntry = null;
            ViewModel.ClearSelectedCommitChanges();
            return;
        }

        if (MainCommitTabControl.SelectedItem is TabItem { Header: "History" })
        {
            PendingGrid.SelectedItem = null;
            ViewModel.SelectedPendingCommit = null;
            ViewModel.ClearSelectedCommitChanges();
        }
    }

    private async void OnRevertPendingCommit(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Reverting commit", ViewModel.RevertSelectedPendingCommitAsync);
    }

    private async void OnCancelStagedAdd(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Canceling add", ViewModel.CancelSelectedStagedAddAsync);
    }

    private async void OnCancelStagedDelete(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Canceling delete", ViewModel.CancelSelectedStagedDeleteAsync);
    }

    private async void OnRevertStagedModified(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Reverting modified files", ViewModel.RevertSelectedStagedModifiedAsync);
    }

    private async void OnCancelSelectedCLChangeAdd(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Canceling add", ViewModel.CancelSelectedCLChangeAddAsync);
    }

    private async void OnCancelSelectedCLChangeDelete(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Canceling delete", ViewModel.CancelSelectedCLChangeDeleteAsync);
    }

    private async void OnRevertSelectedCLChangeModified(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Reverting modified file", ViewModel.RevertSelectedCLChangeModifiedAsync);
    }

    private async void OnRestorePendingCommit(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Restoring commit changes", ViewModel.RestoreSelectedPendingCommitAsync);
    }

    private async void OnDeletePendingCommitKeepChanges(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Deleting commit", () => ViewModel.DeleteSelectedHeadCommitAsync(keepChanges: true));
    }

    private async void OnDeletePendingCommitDiscardChanges(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Deleting commit", () => ViewModel.DeleteSelectedHeadCommitAsync(keepChanges: false));
    }

    private async void OnCheckoutHistoryCommit(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Checking out commit", ViewModel.CheckoutSelectedHistoryCommitAsync);
    }

    private async void OnCopyPendingContent(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPendingCommit is null)
        {
            return;
        }

        await CopyTextAsync(FormatCommitContent("ChangeList", ViewModel.SelectedPendingCommit));
    }

    private async void OnCopyHistoryContent(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHistoryEntry is null)
        {
            return;
        }

        await CopyTextAsync(FormatCommitContent("Commit", ViewModel.SelectedHistoryEntry));
    }

    private async void OnCompareCLChange(object? sender, RoutedEventArgs e)
    {
        var diff = await ViewModel.RunBusyAsync("Loading compare", ViewModel.GetSelectedCommitChangeDiffAsync);
        if (diff is null)
        {
            return;
        }

        var dialog = new CompareDialog
        {
            DataContext = new CompareDialogViewModel(diff),
        };

        await dialog.ShowDialog(this);
    }

    private async void OnOpenMergeConflict(object? sender, RoutedEventArgs e)
    {
        await ShowSelectedMergeConflictAsync();
    }

    private async void OnOpenMergeFromChangeList(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.SelectFirstConflictFromSelectedChangeList())
        {
            return;
        }

        await ShowSelectedMergeConflictAsync();
    }

    private async Task ShowSelectedMergeConflictAsync()
    {
        var file = await ViewModel.RunBusyAsync("Opening merge", ViewModel.GetSelectedMergeConflictFileAsync);
        if (file is null)
        {
            return;
        }

        var dialog = new MergeConflictDialog(
            content => ViewModel.RunBusyAsync("Saving merge file", () => ViewModel.SaveSelectedMergeConflictContentAsync(content)),
            () => ViewModel.RunBusyAsync("Marking resolved", ViewModel.MarkSelectedConflictResolvedAsync))
        {
            DataContext = new MergeConflictDialogViewModel(file),
        };

        await dialog.ShowDialog<bool>(this);
    }

    private async void OnUseOursConflict(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Using ours", ViewModel.UseOursForSelectedConflictAsync);
    }

    private async void OnUseTheirsConflict(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Using theirs", ViewModel.UseTheirsForSelectedConflictAsync);
    }

    private async void OnMarkConflictResolved(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Marking resolved", ViewModel.MarkSelectedConflictResolvedAsync);
    }

    private async void OnAbortMerge(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Aborting merge", ViewModel.AbortMergeAsync);
    }

    private async void OnSearchByCommit(object? sender, RoutedEventArgs e)
    {
        var searchDialog = new CLSearchDialog();
        var accepted = await searchDialog.ShowDialog<bool>(this);
        if (!accepted)
        {
            return;
        }

        var commit = await ViewModel.RunBusyAsync("Searching commit", () => ViewModel.FindCommitByCommitAsync(searchDialog.CommitText));
        if (commit is null)
        {
            return;
        }

        var changes = await ViewModel.RunBusyAsync("Loading commit changes", () => ViewModel.GetCommitChangesAsync(commit));
        var dialog = new CLChangesDialog
        {
            DataContext = new CLChangesDialogViewModel(
                commit,
                changes,
                () => ViewModel.CheckoutCommitAsync(commit),
                (targetCommit, change) => ViewModel.GetCommitChangeDiffAsync(targetCommit, change)),
        };

        await dialog.ShowDialog(this);
    }

    private async void OnCommitClicked(object? sender, RoutedEventArgs e)
    {
        var changes = await ViewModel.RunBusyAsync("Loading working tree changes", ViewModel.GetWorkingTreeChangesAsync);
        var dialogViewModel = new CommitDialogViewModel(ViewModel.CommitMessage, changes);
        var dialog = new CommitDialog
        {
            DataContext = dialogViewModel,
        };

        var accepted = await dialog.ShowDialog<bool>(this);
        if (!accepted)
        {
            return;
        }

        var committed = await ViewModel.RunBusyAsync(
            "Committing",
            () => ViewModel.CommitSelectedFilesAsync(
                dialogViewModel.CommitMessage,
                dialogViewModel.SelectedFiles));

        if (committed)
        {
            ViewModel.CommitMessage = string.Empty;
        }
    }

    private async void OnPushClicked(object? sender, RoutedEventArgs e)
    {
        var branches = await ViewModel.RunBusyAsync("Loading branches", ViewModel.GetLocalBranchesAsync);
        if (branches.Count == 0)
        {
            return;
        }

        var currentBranch = await ViewModel.RunBusyAsync("Loading current branch", ViewModel.GetCurrentBranchNameAsync);
        var dialogViewModel = new PushDialogViewModel(branches, currentBranch);
        var dialog = new PushDialog
        {
            DataContext = dialogViewModel,
        };

        var accepted = await dialog.ShowDialog<bool>(this);
        if (!accepted || string.IsNullOrWhiteSpace(dialogViewModel.SelectedBranch))
        {
            return;
        }

        await ViewModel.RunBusyAsync("Pushing", () => ViewModel.PushBranchToOriginAsync(dialogViewModel.SelectedBranch));
    }

    private async void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunBusyAsync("Opening settings", ShowSettingsDialogAsync);
    }

    private async Task ShowSettingsDialogAsync()
    {
        if (_isSettingsDialogOpen)
        {
            return;
        }

        _isSettingsDialogOpen = true;
        try
        {
            var settings = await ViewModel.LoadGitHubSettingsAsync();
            var originUrl = await ViewModel.GetCurrentOriginUrlAsync();
            var dialogViewModel = new SettingsDialogViewModel(settings, originUrl);
            var dialog = new SettingsDialog
            {
                DataContext = dialogViewModel,
            };

            var accepted = await dialog.ShowDialog<bool>(this);
            if (!accepted)
            {
                return;
            }

            await ViewModel.SaveGitHubSettingsAsync(
                dialogViewModel.ToSettings(dialogViewModel.HasStoredCredential),
                dialogViewModel.Token,
                dialogViewModel.ConfigureGitIdentity,
                dialogViewModel.SaveCredential,
                dialogViewModel.RemoveStoredCredential,
                dialogViewModel.ConvertOriginToHttps);
        }
        finally
        {
            _isSettingsDialogOpen = false;
        }
    }

    private void OnClearOutput(object? sender, RoutedEventArgs e)
    {
        ViewModel.OutputLines.Clear();
        ViewModel.NotifyOutputTextChanged();
    }

    private void OnExit(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnPendingContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        UpdatePendingContextMenu();
    }

    private void OnCLChangesContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        UpdateCLChangesContextMenu();
    }

    private void UpdatePendingContextMenu()
    {
        var isCommit = ViewModel.CanOperateSelectedPendingCommit;
        PendingPushMenuItem.IsVisible = isCommit;
        RevertCommitMenuItem.IsVisible = isCommit;
        RestoreCommitMenuItem.IsVisible = isCommit;
        DeleteHeadKeepMenuItem.IsVisible = isCommit;
        DeleteHeadDiscardMenuItem.IsVisible = isCommit;
        PendingCommitSeparator.IsVisible = isCommit;
        PendingDeleteSeparator.IsVisible = isCommit;

        CancelStagedAddMenuItem.IsVisible = ViewModel.CanCancelSelectedStagedAdd;
        CancelStagedDeleteMenuItem.IsVisible = ViewModel.CanCancelSelectedStagedDelete;
        RevertStagedModifiedMenuItem.IsVisible = ViewModel.CanRevertSelectedStagedModified;
        PendingOpenMergeMenuItem.IsVisible = ViewModel.CanOpenSelectedChangeListMerge;
        PendingAbortMergeMenuItem.IsVisible = ViewModel.CanAbortMerge;
    }

    private void UpdateCLChangesContextMenu()
    {
        var canCancelAdd = ViewModel.CanCancelSelectedCLChangeAdd;
        var canCancelDelete = ViewModel.CanCancelSelectedCLChangeDelete;
        var canRevertModified = ViewModel.CanRevertSelectedCLChangeModified;
        var hasStateAction = canCancelAdd || canCancelDelete || canRevertModified;
        var canResolveConflict = ViewModel.CanResolveSelectedConflict;
        var canAbortMerge = ViewModel.CanAbortMerge;
        var hasMergeAction = canResolveConflict || canAbortMerge;

        OpenMergeConflictMenuItem.IsVisible = canResolveConflict;
        UseOursMenuItem.IsVisible = canResolveConflict;
        UseTheirsMenuItem.IsVisible = canResolveConflict;
        MarkResolvedMenuItem.IsVisible = canResolveConflict;
        AbortMergeMenuItem.IsVisible = canAbortMerge;
        CLChangeMergeSeparator.IsVisible = hasMergeAction;
        CancelCLChangeAddMenuItem.IsVisible = canCancelAdd;
        CancelCLChangeDeleteMenuItem.IsVisible = canCancelDelete;
        RevertCLChangeModifiedMenuItem.IsVisible = canRevertModified;
        CLChangeActionSeparator.IsVisible = hasStateAction;
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open Workspace",
                AllowMultiple = false,
            });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private void SelectHistoryTab()
    {
        foreach (var item in MainCommitTabControl.Items)
        {
            if (item is TabItem { Header: "History" } tab)
            {
                MainCommitTabControl.SelectedItem = tab;
                return;
            }
        }
    }

    private static FileSystemNode? FindNodeFromSource(object? source)
    {
        return FindDataContextFromSource<FileSystemNode>(source);
    }

    private static T? FindDataContextFromSource<T>(object? source)
        where T : class
    {
        var visual = source as Avalonia.Visual;
        while (visual is not null)
        {
            if (visual is Control { DataContext: T item })
            {
                return item;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    private async Task CopyTextAsync(string text)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }

    private static string FormatCommitContent(string title, GitHistoryEntry entry)
    {
        return string.Join(
            Environment.NewLine,
            $"{title}: {entry.ShortRevision}",
            $"Revision: {entry.Revision}",
            $"State: {entry.PublishState}",
            $"ChangeListState: {entry.ChangeListState}",
            $"Author: {entry.Author}",
            $"Date: {entry.Date}",
            $"Description: {entry.Subject}");
    }

}
