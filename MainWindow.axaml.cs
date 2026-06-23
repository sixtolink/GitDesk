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
        await ViewModel.InitializeWorkspaceHistoryAsync();
    }

    private async void OnBrowseWorkspace(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
        {
            await ViewModel.OpenWorkspaceAsync(folder);
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
            await ViewModel.OpenWorkspaceFromHistoryAsync(path);
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

        WorkspaceTree.SelectedItem = node;
        ViewModel.SelectedNode = node;
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
        await ViewModel.RevertSelectedPendingCommitAsync();
    }

    private async void OnCancelStagedAdd(object? sender, RoutedEventArgs e)
    {
        await ViewModel.CancelSelectedStagedAddAsync();
    }

    private async void OnCancelStagedDelete(object? sender, RoutedEventArgs e)
    {
        await ViewModel.CancelSelectedStagedDeleteAsync();
    }

    private async void OnRevertStagedModified(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RevertSelectedStagedModifiedAsync();
    }

    private async void OnCancelSelectedCLChangeAdd(object? sender, RoutedEventArgs e)
    {
        await ViewModel.CancelSelectedCLChangeAddAsync();
    }

    private async void OnCancelSelectedCLChangeDelete(object? sender, RoutedEventArgs e)
    {
        await ViewModel.CancelSelectedCLChangeDeleteAsync();
    }

    private async void OnRevertSelectedCLChangeModified(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RevertSelectedCLChangeModifiedAsync();
    }

    private async void OnRestorePendingCommit(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RestoreSelectedPendingCommitAsync();
    }

    private async void OnDeletePendingCommitKeepChanges(object? sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteSelectedHeadCommitAsync(keepChanges: true);
    }

    private async void OnDeletePendingCommitDiscardChanges(object? sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteSelectedHeadCommitAsync(keepChanges: false);
    }

    private async void OnCheckoutHistoryCommit(object? sender, RoutedEventArgs e)
    {
        await ViewModel.CheckoutSelectedHistoryCommitAsync();
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
        var diff = await ViewModel.GetSelectedCommitChangeDiffAsync();
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

    private async void OnSearchByCommit(object? sender, RoutedEventArgs e)
    {
        var searchDialog = new CLSearchDialog();
        var accepted = await searchDialog.ShowDialog<bool>(this);
        if (!accepted)
        {
            return;
        }

        var commit = await ViewModel.FindCommitByCommitAsync(searchDialog.CommitText);
        if (commit is null)
        {
            return;
        }

        var changes = await ViewModel.GetCommitChangesAsync(commit);
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
        var changes = await ViewModel.GetWorkingTreeChangesAsync();
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

        var committed = await ViewModel.CommitSelectedFilesAsync(
            dialogViewModel.CommitMessage,
            dialogViewModel.SelectedFiles);

        if (committed)
        {
            ViewModel.CommitMessage = string.Empty;
        }
    }

    private async void OnPushClicked(object? sender, RoutedEventArgs e)
    {
        var branches = await ViewModel.GetLocalBranchesAsync();
        if (branches.Count == 0)
        {
            return;
        }

        var dialogViewModel = new PushDialogViewModel(branches, await ViewModel.GetCurrentBranchNameAsync());
        var dialog = new PushDialog
        {
            DataContext = dialogViewModel,
        };

        var accepted = await dialog.ShowDialog<bool>(this);
        if (!accepted || string.IsNullOrWhiteSpace(dialogViewModel.SelectedBranch))
        {
            return;
        }

        await ViewModel.PushBranchToOriginAsync(dialogViewModel.SelectedBranch);
    }

    private async void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        await ShowSettingsDialogAsync();
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
    }

    private void UpdateCLChangesContextMenu()
    {
        var canCancelAdd = ViewModel.CanCancelSelectedCLChangeAdd;
        var canCancelDelete = ViewModel.CanCancelSelectedCLChangeDelete;
        var canRevertModified = ViewModel.CanRevertSelectedCLChangeModified;
        var hasStateAction = canCancelAdd || canCancelDelete || canRevertModified;

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
