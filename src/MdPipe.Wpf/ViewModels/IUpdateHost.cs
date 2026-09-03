namespace MdPipe.Wpf.ViewModels;

/// <summary>
/// What the update notice needs from the window it sits in.
/// </summary>
/// <remarks>
/// Replacing the application is a whole-window affair: it takes a minute, it has to stop anything
/// else starting meanwhile, and it belongs in the same status line as everything else rather than in
/// a second one of its own. That is the entire coupling, so it is written down here instead of the
/// notice reaching into the view model around it.
/// </remarks>
public interface IUpdateHost
{
    /// <summary>Whether the window is already occupied with something else.</summary>
    bool IsBusy { get; }

    /// <summary>Claims the window for the duration of an update, or gives it back.</summary>
    void SetBusy(bool busy);

    /// <summary>Says something in the window's status line.</summary>
    void Report(string status);
}
