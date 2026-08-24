namespace MdPipe.Wpf.Services;

public enum DialogKind { Information, Warning, Error }

/// <summary>
/// Everything the view model needs from Windows itself: message boxes, the folder picker, and opening
/// a folder in Explorer.
/// </summary>
/// <remarks>
/// Behind an interface for two reasons. Tests can assert that the right thing was said instead of
/// hanging forever on a modal box nobody is there to dismiss, and the view model stops reaching
/// straight into WPF and the shell to get its work done.
/// </remarks>
public interface IDialogService
{
    void ShowMessage(string message, string title, DialogKind kind);

    /// <returns>True if the user agreed. Used for the one thing MdPipe does that it should never
    /// do without being asked: replace itself.</returns>
    bool Confirm(string message, string title);

    /// <summary>Opens a link in whatever the machine uses for links.</summary>
    void OpenLink(string url);

    /// <summary>Starts the given executable and closes this one, in that order.</summary>
    void RestartWith(string executablePath);

    /// <returns>The chosen folder, or null if the user backed out.</returns>
    string? PickFolder(string title);

    void OpenFolder(string path);
}
