namespace MdPipe.Wpf.ViewModels;

/// <summary>
/// What the update notice needs from the window it sits in. Updating takes over the whole window for
/// a minute, and it reports into the same status line as everything else.
/// </summary>
public interface IUpdateHost
{
    bool IsBusy { get; }
    void SetBusy(bool busy);
    void Report(string status);
}
