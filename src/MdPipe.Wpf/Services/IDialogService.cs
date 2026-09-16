namespace MdPipe.Wpf.Services;

public enum DialogKind { Information, Warning, Error }

/// <summary>
/// Everything the view model needs from Windows itself. Behind an interface so tests can check what
/// was said instead of hanging on a modal box nobody is there to dismiss.
/// </summary>
public interface IDialogService
{
    void ShowMessage(string message, string title, DialogKind kind);

    /// <returns>True if the user agreed.</returns>
    bool Confirm(string message, string title);

    void OpenLink(string url);

    /// <summary>Starts the given executable and closes this one, in that order.</summary>
    void RestartWith(string executablePath);

    /// <returns>The chosen folder, or null if the user backed out.</returns>
    string? PickFolder(string title);

    void OpenFolder(string path);
}
