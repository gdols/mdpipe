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

    /// <returns>The chosen folder, or null if the user backed out.</returns>
    string? PickFolder(string title);

    void OpenFolder(string path);
}
