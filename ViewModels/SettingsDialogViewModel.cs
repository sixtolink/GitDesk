using GitDesk.Models;

namespace GitDesk.ViewModels;

public sealed class SettingsDialogViewModel : ObservableObject
{
    private string _host;
    private string _username;
    private string _gitUserName;
    private string _gitUserEmail;
    private string _token = string.Empty;
    private string _currentOriginUrl;
    private string _errorText = string.Empty;
    private bool _hasStoredCredential;
    private bool _configureGitIdentity = true;
    private bool _saveCredential = true;
    private bool _removeStoredCredential;
    private bool _convertOriginToHttps;

    public SettingsDialogViewModel(GitHubSettings settings, string currentOriginUrl)
    {
        var normalized = settings.Normalized();
        _host = normalized.Host;
        _username = normalized.Username;
        _gitUserName = normalized.GitUserName;
        _gitUserEmail = normalized.GitUserEmail;
        _hasStoredCredential = normalized.HasStoredCredential;
        _currentOriginUrl = currentOriginUrl;
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string GitUserName
    {
        get => _gitUserName;
        set => SetProperty(ref _gitUserName, value);
    }

    public string GitUserEmail
    {
        get => _gitUserEmail;
        set => SetProperty(ref _gitUserEmail, value);
    }

    public string Token
    {
        get => _token;
        set => SetProperty(ref _token, value);
    }

    public string CurrentOriginUrl
    {
        get => _currentOriginUrl;
        set => SetProperty(ref _currentOriginUrl, value);
    }

    public bool HasStoredCredential
    {
        get => _hasStoredCredential;
        set => SetProperty(ref _hasStoredCredential, value);
    }

    public bool ConfigureGitIdentity
    {
        get => _configureGitIdentity;
        set => SetProperty(ref _configureGitIdentity, value);
    }

    public bool SaveCredential
    {
        get => _saveCredential;
        set => SetProperty(ref _saveCredential, value);
    }

    public bool RemoveStoredCredential
    {
        get => _removeStoredCredential;
        set => SetProperty(ref _removeStoredCredential, value);
    }

    public bool ConvertOriginToHttps
    {
        get => _convertOriginToHttps;
        set => SetProperty(ref _convertOriginToHttps, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string StoredCredentialText => HasStoredCredential
        ? "A credential was saved through Git Credential Manager."
        : "No credential has been saved by GitDesk.";

    public GitHubSettings ToSettings(bool hasStoredCredential)
    {
        return new GitHubSettings(Host, Username, GitUserName, GitUserEmail, hasStoredCredential).Normalized();
    }

    public bool Validate()
    {
        var normalized = new GitHubSettings(Host, Username, GitUserName, GitUserEmail, HasStoredCredential).Normalized();
        if (string.IsNullOrWhiteSpace(normalized.Host))
        {
            ErrorText = "GitHub host is empty.";
            return false;
        }

        if ((SaveCredential && !string.IsNullOrWhiteSpace(Token)) || RemoveStoredCredential)
        {
            if (string.IsNullOrWhiteSpace(normalized.Username))
            {
                ErrorText = "Username is required for credential changes.";
                return false;
            }
        }

        ErrorText = string.Empty;
        Host = normalized.Host;
        Username = normalized.Username;
        GitUserName = normalized.GitUserName;
        GitUserEmail = normalized.GitUserEmail;
        return true;
    }
}
