using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GitDesk.Models;

namespace GitDesk.Services;

public sealed class GitService
{
    private static readonly string[] Utf8ConfigArguments =
    {
        "-c",
        "core.quotepath=false",
        "-c",
        "i18n.commitEncoding=utf-8",
        "-c",
        "i18n.logOutputEncoding=utf-8",
    };

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            workingDirectory,
            arguments,
            null,
            useUtf8Config: true,
            forceCredentialManager: false,
            allowCredentialPrompt: false,
            cancellationToken);
    }

    public async Task<GitCommandResult> RunAuthenticatedAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            workingDirectory,
            arguments,
            null,
            useUtf8Config: true,
            forceCredentialManager: true,
            allowCredentialPrompt: true,
            cancellationToken);
    }

    public async Task<GitCommandResult> RunCredentialAsync(
        string workingDirectory,
        string command,
        string credentialInput,
        bool forceCredentialManager = true,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (forceCredentialManager)
        {
            args.Add("-c");
            args.Add("credential.helper=");
            args.Add("-c");
            args.Add("credential.helper=manager");
        }

        args.Add("credential");
        args.Add(command);
        return await RunAsync(
            workingDirectory,
            args,
            credentialInput,
            useUtf8Config: false,
            forceCredentialManager: false,
            allowCredentialPrompt: false,
            cancellationToken);
    }

    private async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? standardInput,
        bool useUtf8Config,
        bool forceCredentialManager,
        bool allowCredentialPrompt,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.Environment["LANG"] = "C.UTF-8";
        startInfo.Environment["LC_ALL"] = "C.UTF-8";
        startInfo.Environment["LESSCHARSET"] = "utf-8";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = allowCredentialPrompt ? "1" : "0";
        startInfo.Environment["GCM_INTERACTIVE"] = allowCredentialPrompt ? "1" : "0";
        startInfo.Environment["TERM"] = "dumb";

        if (useUtf8Config)
        {
            foreach (var argument in Utf8ConfigArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (forceCredentialManager)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("credential.helper=");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("credential.helper=manager");
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    public async Task<GitCommandResult> GetConfigValueAsync(
        string workingDirectory,
        string key,
        bool global,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "config" };
        if (global)
        {
            args.Add("--global");
        }

        args.Add("--get");
        args.Add(key);
        return await RunAsync(workingDirectory, args, cancellationToken);
    }

    public static bool IsAuthenticationFailure(GitCommandResult result)
    {
        if (result.IsSuccess)
        {
            return false;
        }

        var output = $"{result.StandardOutput}\n{result.StandardError}";
        return output.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("could not read Username", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("could not read Password", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Could not read from remote repository", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Repository not found", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("HTTP Basic: Access denied", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSshPublicKeyFailure(GitCommandResult result)
    {
        var output = $"{result.StandardOutput}\n{result.StandardError}";
        return output.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Could not read from remote repository", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<GitCommandResult> SetGlobalConfigValueAsync(
        string workingDirectory,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            workingDirectory,
            new[] { "config", "--global", key, value },
            cancellationToken);
    }

    public async Task<GitCommandResult> StoreCredentialAsync(
        string workingDirectory,
        string host,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var input = FormatCredentialInput(host, username, password);
        var result = await RunCredentialAsync(workingDirectory, "approve", input, forceCredentialManager: true, cancellationToken);
        return result.IsSuccess
            ? result
            : await RunCredentialAsync(workingDirectory, "approve", input, forceCredentialManager: false, cancellationToken);
    }

    public async Task<GitCommandResult> RejectCredentialAsync(
        string workingDirectory,
        string host,
        string username,
        CancellationToken cancellationToken = default)
    {
        var input = FormatCredentialInput(host, username, null);
        var result = await RunCredentialAsync(workingDirectory, "reject", input, forceCredentialManager: true, cancellationToken);
        return result.IsSuccess
            ? result
            : await RunCredentialAsync(workingDirectory, "reject", input, forceCredentialManager: false, cancellationToken);
    }

    public async Task<string> GetOriginUrlAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, new[] { "remote", "get-url", "origin" }, cancellationToken);
        return result.IsSuccess ? result.StandardOutput.Trim() : string.Empty;
    }

    public async Task<string?> GetCurrentBranchNameAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, cancellationToken);
        if (!result.IsSuccess)
        {
            return null;
        }

        var branch = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(branch) || branch == "HEAD" ? null : branch;
    }

    public async Task<IReadOnlyList<string>> GetLocalBranchesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            repositoryRoot,
            new[] { "for-each-ref", "--format=%(refname:short)", "refs/heads" },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return Array.Empty<string>();
        }

        return result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(branch => branch.Trim())
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> GetLocalBranchesContainingAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return Array.Empty<string>();
        }

        var result = await RunAsync(
            repositoryRoot,
            new[] { "branch", "--contains", revision, "--format=%(refname:short)" },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return Array.Empty<string>();
        }

        return result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(branch => branch.Trim().TrimStart('*').Trim())
            .Where(IsRealBranchName)
            .OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> GetRemoteBranchesContainingAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return Array.Empty<string>();
        }

        var result = await RunAsync(
            repositoryRoot,
            new[] { "branch", "-r", "--contains", revision, "--format=%(refname:short)" },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return Array.Empty<string>();
        }

        return result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(branch => branch.Trim().TrimStart('*').Trim())
            .Where(branch =>
                IsRealBranchName(branch) &&
                !branch.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(branch => branch.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
            .ThenBy(branch => branch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsRealBranchName(string? branch)
    {
        return !string.IsNullOrWhiteSpace(branch) &&
               !branch.StartsWith("(", StringComparison.Ordinal) &&
               !branch.Contains(" detached", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> GetUpstreamBranchNameAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            repositoryRoot,
            new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}" },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return null;
        }

        var upstream = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(upstream) ? null : upstream;
    }

    public async Task<bool> IsAncestorOfHeadAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return false;
        }

        var result = await RunAsync(
            repositoryRoot,
            new[] { "merge-base", "--is-ancestor", revision, "HEAD" },
            cancellationToken);
        return result.IsSuccess;
    }

    public async Task<GitCommandResult> ConvertOriginToHttpsAsync(
        string repositoryRoot,
        string host,
        CancellationToken cancellationToken = default)
    {
        var origin = await GetOriginUrlAsync(repositoryRoot, cancellationToken);
        var httpsUrl = TryConvertGitHubSshToHttps(origin, host);
        if (httpsUrl is null)
        {
            return new GitCommandResult(1, string.Empty, $"origin is not a GitHub SSH URL: {origin}");
        }

        return await RunAsync(repositoryRoot, new[] { "remote", "set-url", "origin", httpsUrl }, cancellationToken);
    }

    public async Task<string?> FindRepositoryRootAsync(string path, CancellationToken cancellationToken = default)
    {
        var workingDirectory = ToWorkingDirectory(path);
        if (workingDirectory is null)
        {
            return null;
        }

        var result = await RunAsync(
            workingDirectory,
            new[] { "rev-parse", "--show-toplevel" },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return null;
        }

        var root = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
    }

    public async Task<IReadOnlyList<GitChange>> GetStatusAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            repositoryRoot,
            new[] { "status", "--short", "--branch" },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Array.Empty<GitChange>();
        }

        return ParseStatus(result.StandardOutput);
    }

    public string FormatStatusOutput(string output)
    {
        return string.Join(
            Environment.NewLine,
            output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(FormatStatusLine));
    }

    public async Task<IReadOnlyList<GitHistoryEntry>> GetHistoryAsync(
        string repositoryRoot,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "log",
            "--max-count=120",
            "--date=short",
            "--pretty=format:%H%x09%an%x09%ad%x09%s",
        };
        AddPathspec(args, repositoryRoot, path);

        var result = await RunAsync(repositoryRoot, args, cancellationToken);
        if (!result.IsSuccess)
        {
            return Array.Empty<GitHistoryEntry>();
        }

        return result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 4))
            .Where(parts => parts.Length == 4)
            .Select(parts => new GitHistoryEntry(parts[0], parts[1], parts[2], parts[3]))
            .ToArray();
    }

    public async Task<IReadOnlyList<GitHistoryEntry>> GetPendingCommitsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var upstreamResult = await RunAsync(
            repositoryRoot,
            new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}" },
            cancellationToken);

        var args = new List<string>
        {
            "log",
            "--max-count=120",
            "--date=short",
            "--pretty=format:%H%x09%an%x09%ad%x09%s",
        };

        if (upstreamResult.IsSuccess && !string.IsNullOrWhiteSpace(upstreamResult.StandardOutput))
        {
            args.Add($"{upstreamResult.StandardOutput.Trim()}..HEAD");
        }
        else
        {
            args.Add("--branches");
            args.Add("--not");
            args.Add("--remotes");
        }

        var result = await RunAsync(repositoryRoot, args, cancellationToken);
        if (!result.IsSuccess)
        {
            return Array.Empty<GitHistoryEntry>();
        }

        return result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 4))
            .Where(parts => parts.Length == 4)
            .Select(parts => new GitHistoryEntry(parts[0], parts[1], parts[2], parts[3]))
            .ToArray();
    }

    public async Task<GitHistoryEntry?> GetCommitAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return null;
        }

        var result = await RunAsync(
            repositoryRoot,
            new[]
            {
                "show",
                "--quiet",
                "--date=short",
                "--pretty=format:%H%x09%an%x09%ad%x09%s",
                revision.Trim(),
            },
            cancellationToken);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        var parts = result.StandardOutput.Trim().Split('\t', 4);
        return parts.Length == 4
            ? new GitHistoryEntry(parts[0], parts[1], parts[2], parts[3])
            : null;
    }

    public async Task<IReadOnlyList<GitChange>> GetCommitChangesAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            repositoryRoot,
            new[] { "diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "-M", revision },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Array.Empty<GitChange>();
        }

        return ParseNameStatus(result.StandardOutput);
    }

    public static string? ToWorkingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        if (File.Exists(path))
        {
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }

        return null;
    }

    public static void AddPathspec(ICollection<string> arguments, string repositoryRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(repositoryRoot, fullPath);

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return;
        }

        arguments.Add("--");
        arguments.Add(relativePath.Replace(Path.DirectorySeparatorChar, '/'));
    }

    public static string? TryConvertGitHubSshToHttps(string remoteUrl, string host)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var normalizedHost = string.IsNullOrWhiteSpace(host) ? "github.com" : host.Trim();
        if (normalizedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            normalizedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedHost = new Uri(normalizedHost).Host;
        }

        normalizedHost = normalizedHost.TrimEnd('/');
        var scpPrefix = $"git@{normalizedHost}:";
        if (remoteUrl.StartsWith(scpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{normalizedHost}/{remoteUrl[scpPrefix.Length..]}";
        }

        var sshPrefix = $"ssh://git@{normalizedHost}/";
        if (remoteUrl.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{normalizedHost}/{remoteUrl[sshPrefix.Length..]}";
        }

        return null;
    }

    private static string FormatCredentialInput(string host, string username, string? password)
    {
        var builder = new StringBuilder()
            .AppendLine("protocol=https")
            .AppendLine($"host={host.Trim()}")
            .AppendLine($"username={username.Trim()}");

        if (!string.IsNullOrEmpty(password))
        {
            builder.AppendLine($"password={password}");
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static IReadOnlyList<GitChange> ParseStatus(string output)
    {
        var changes = new List<GitChange>();
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length < 4)
            {
                continue;
            }

            var rawStatus = line[..2];
            var status = rawStatus.Trim();
            var path = NormalizeStatusPath(line[3..].Trim());
            var details = GetStatusDetails(rawStatus);

            changes.Add(new GitChange(status, path, details));
        }

        return changes;
    }

    private static string FormatStatusLine(string line)
    {
        if (line == "## HEAD (no branch)")
        {
            return "## Detached HEAD (no branch)";
        }

        if (line.StartsWith("## ", StringComparison.Ordinal) || line.Length < 4)
        {
            return line;
        }

        return $"{line[..2]} {NormalizeStatusPath(line[3..].Trim())}";
    }

    private static IReadOnlyList<GitChange> ParseNameStatus(string output)
    {
        var changes = new List<GitChange>();
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            var status = parts[0].Trim();
            var path = status.StartsWith("R", StringComparison.Ordinal) && parts.Length >= 3
                ? $"{NormalizeGitPath(parts[1])} -> {NormalizeGitPath(parts[2])}"
                : NormalizeGitPath(parts[1]);

            changes.Add(new GitChange(status, path, GetNameStatusDetails(status)));
        }

        return changes;
    }

    private static string NormalizeStatusPath(string path)
    {
        const string renameMarker = " -> ";
        if (!path.Contains(renameMarker, StringComparison.Ordinal))
        {
            return NormalizeGitPath(path);
        }

        return string.Join(
            renameMarker,
            path.Split(new[] { renameMarker }, StringSplitOptions.None)
                .Select(NormalizeGitPath));
    }

    private static string NormalizeGitPath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return trimmed;
        }

        return UnescapeGitQuotedPath(trimmed[1..^1]);
    }

    private static string UnescapeGitQuotedPath(string path)
    {
        using var bytes = new MemoryStream(path.Length);
        for (var i = 0; i < path.Length; i++)
        {
            var current = path[i];
            if (current != '\\' || i + 1 >= path.Length)
            {
                WriteUtf8(bytes, current);
                continue;
            }

            var next = path[++i];
            if (next is >= '0' and <= '7')
            {
                var value = next - '0';
                var count = 1;
                while (count < 3 && i + 1 < path.Length && path[i + 1] is >= '0' and <= '7')
                {
                    value = (value * 8) + (path[++i] - '0');
                    count++;
                }

                bytes.WriteByte((byte)value);
                continue;
            }

            var unescaped = next switch
            {
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                _ => next,
            };
            WriteUtf8(bytes, unescaped);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void WriteUtf8(Stream stream, char value)
    {
        var buffer = Encoding.UTF8.GetBytes(value.ToString());
        stream.Write(buffer, 0, buffer.Length);
    }

    private static string GetNameStatusDetails(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Changed";
        }

        return status[0] switch
        {
            'A' => "Added",
            'C' => "Copied",
            'D' => "Deleted",
            'M' => "Modified",
            'R' => "Renamed",
            'T' => "Type changed",
            'U' => "Unmerged",
            'X' => "Unknown",
            _ => "Changed",
        };
    }

    private static string GetStatusDetails(string rawStatus)
    {
        if (rawStatus == "??")
        {
            return "Untracked";
        }

        if (rawStatus == "!!")
        {
            return "Ignored";
        }

        var index = rawStatus[0];
        var worktree = rawStatus[1];

        return (index, worktree) switch
        {
            ('M', 'M') => "Modified (staged and unstaged)",
            ('A', 'M') => "Added (modified)",
            ('M', _) => "Modified (staged)",
            ('A', _) => "Added",
            ('D', _) => "Deleted (staged)",
            ('R', _) => "Renamed",
            ('C', _) => "Copied",
            (_, 'M') => "Modified",
            (_, 'D') => "Deleted",
            _ => rawStatus.Trim() switch
            {
                "M" => "Modified",
                "A" => "Added",
                "D" => "Deleted",
                "R" => "Renamed",
                "C" => "Copied",
                _ => "Changed",
            },
        };
    }
}
