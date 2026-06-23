using System;

namespace GitDesk.Models;

public sealed record GitHubSettings(
    string Host,
    string Username,
    string GitUserName,
    string GitUserEmail,
    bool HasStoredCredential)
{
    public static GitHubSettings Default { get; } = new("github.com", string.Empty, string.Empty, string.Empty, false);

    public GitHubSettings Normalized()
    {
        var host = NormalizeHost(Host);
        return this with
        {
            Host = string.IsNullOrWhiteSpace(host) ? "github.com" : host,
            Username = Username.Trim(),
            GitUserName = GitUserName.Trim(),
            GitUserEmail = GitUserEmail.Trim(),
        };
    }

    private static string NormalizeHost(string host)
    {
        var value = host.Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }
        }

        return value.TrimEnd('/');
    }
}
