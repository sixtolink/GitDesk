using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GitDesk.Models;
using LevelDB;

namespace GitDesk.Services;

public sealed class WorkspaceStore
{
    private const string HistoryKey = "workspace-history";
    private const string GitHubSettingsKey = "github-settings";
    private const int MaxHistoryCount = 40;
    private readonly string _databasePath;

    public WorkspaceStore()
    {
        _databasePath = Path.Combine(GetAppDataRoot(), "leveldb");
    }

    public Task<IReadOnlyList<string>> LoadAsync()
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            using var database = OpenDatabase();
            var json = database.Get(HistoryKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<string>();
            }

            var document = JsonSerializer.Deserialize<WorkspaceHistoryDocument>(json);
            return NormalizeHistory(document?.Workspaces ?? Array.Empty<string>());
        });
    }

    public async Task<IReadOnlyList<string>> AddAsync(string workspacePath)
    {
        var fullPath = Path.GetFullPath(workspacePath);
        var history = (await LoadAsync()).ToList();
        var comparer = GetPathComparer();

        history.RemoveAll(path => comparer.Equals(path, fullPath));
        history.Insert(0, fullPath);

        if (history.Count > MaxHistoryCount)
        {
            history.RemoveRange(MaxHistoryCount, history.Count - MaxHistoryCount);
        }

        await SaveAsync(history);
        return history;
    }

    public Task<GitHubSettings> LoadGitHubSettingsAsync()
    {
        return Task.Run(() =>
        {
            using var database = OpenDatabase();
            var json = database.Get(GitHubSettingsKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return GitHubSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<GitHubSettingsDocument>(json);
            return new GitHubSettings(
                settings?.Host ?? GitHubSettings.Default.Host,
                settings?.Username ?? string.Empty,
                settings?.GitUserName ?? string.Empty,
                settings?.GitUserEmail ?? string.Empty,
                settings?.HasStoredCredential ?? false).Normalized();
        });
    }

    public Task SaveGitHubSettingsAsync(GitHubSettings settings)
    {
        return Task.Run(() =>
        {
            using var database = OpenDatabase();
            var normalized = settings.Normalized();
            var document = new GitHubSettingsDocument(
                normalized.Host,
                normalized.Username,
                normalized.GitUserName,
                normalized.GitUserEmail,
                normalized.HasStoredCredential);
            database.Put(GitHubSettingsKey, JsonSerializer.Serialize(document));
        });
    }

    private Task SaveAsync(IReadOnlyList<string> workspaces)
    {
        return Task.Run(() =>
        {
            using var database = OpenDatabase();
            var document = new WorkspaceHistoryDocument(workspaces.ToArray());
            var json = JsonSerializer.Serialize(document);
            database.Put(HistoryKey, json);
        });
    }

    private DB OpenDatabase()
    {
        Directory.CreateDirectory(_databasePath);
        var options = new Options
        {
            CreateIfMissing = true,
        };

        return new DB(options, _databasePath, Encoding.UTF8);
    }

    private static IReadOnlyList<string> NormalizeHistory(IEnumerable<string> paths)
    {
        var comparer = GetPathComparer();
        var result = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            if (result.Any(existing => comparer.Equals(existing, fullPath)))
            {
                continue;
            }

            result.Add(fullPath);
        }

        return result;
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static string GetAppDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "GitDesk");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".gitdesk");
    }

    private sealed record WorkspaceHistoryDocument(string[] Workspaces);

    private sealed record GitHubSettingsDocument(
        string Host,
        string Username,
        string GitUserName,
        string GitUserEmail,
        bool HasStoredCredential);
}
