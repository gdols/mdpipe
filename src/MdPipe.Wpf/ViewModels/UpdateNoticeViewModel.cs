using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf.Mvvm;
using MdPipe.Wpf.Resources;
using MdPipe.Wpf.Services;

namespace MdPipe.Wpf.ViewModels;

/// <summary>
/// The bar that appears when a newer MdPipe exists, and what happens if the user says yes.
/// </summary>
/// <remarks>
/// Its own class because it shares nothing with converting documents beyond the window it appears
/// in. It knows what release the repository named, whether this copy can replace itself where it
/// stands, and how to do that; it knows nothing about files, folders or engines.
/// </remarks>
public sealed class UpdateNoticeViewModel : ObservableObject
{
    private readonly AppUpdateService _updates;
    private readonly IAppUpdateInstaller _installer;
    private readonly IDialogService _dialogs;
    private readonly IUpdateHost _host;
    private readonly string? _runningVersion;

    private AppUpdate? _update;
    private bool _dismissed;

    public UpdateNoticeViewModel(
        AppUpdateService updates,
        IAppUpdateInstaller installer,
        IDialogService dialogs,
        IUpdateHost host,
        string? runningVersion)
    {
        _updates = updates;
        _installer = installer;
        _dialogs = dialogs;
        _host = host;
        _runningVersion = runningVersion;

        InstallCommand = new RelayCommand(async () => await InstallAsync(), () => HasNotice && !_host.IsBusy);
        DismissCommand = new RelayCommand(Dismiss, () => HasNotice);
    }

    public RelayCommand InstallCommand { get; }
    public RelayCommand DismissCommand { get; }

    /// <summary>The release to offer, or null when there is nothing to say or it was waved away.</summary>
    public AppUpdate? Update => _dismissed ? null : _update;

    public bool HasNotice => Update is not null;

    /// <summary>
    /// Whether MdPipe can replace itself where it stands. False on read-only media, under Program
    /// Files, and anywhere else its own folder cannot be written to, which is an ordinary place for
    /// a portable executable to be.
    /// </summary>
    public bool CanInstall => Update is { DownloadUrl.Length: > 0 } && _installer.CanInstall;

    /// <summary>What the link offers to do, which depends on whether it can actually do it.</summary>
    public string ActionText => CanInstall ? Strings.UpdateInstall : Strings.UpdateGetIt;

    public string Message => Update is not { } update
        ? string.Empty
        : string.Format(
            update.Critical ? Strings.UpdateCritical : Strings.UpdateAvailable,
            update.Version, _runningVersion);

    /// <summary>
    /// Takes what the repository said about the newest release and works out whether to say anything.
    /// </summary>
    public void Consider(AppRelease? release)
    {
        _update = _updates.CheckFor(release, _runningVersion);
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Update));
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(Message));
    }

    /// <summary>
    /// Replaces MdPipe with the newer release, having asked first.
    /// </summary>
    /// <remarks>
    /// Where the executable cannot be written, this opens the release page rather than failing at
    /// the last step, and without asking anything: there is nothing to confirm when the browser is
    /// doing the work.
    /// </remarks>
    private async Task InstallAsync()
    {
        if (Update is not { } update) return;

        if (!CanInstall)
        {
            _dialogs.OpenLink(update.ReleaseUrl);
            return;
        }

        if (!_dialogs.Confirm(
                string.Format(Strings.UpdateConfirmBody, update.Version), Strings.UpdateConfirmTitle))
            return;

        _host.SetBusy(true);
        try
        {
            var progress = new Progress<string>(_host.Report);
            var newExecutable = await _installer.InstallAsync(update, progress);

            _host.Report(Strings.UpdateRestarting);
            _dialogs.RestartWith(newExecutable);
        }
        catch (AppUpdateException ex)
        {
            // Nothing was changed on the way to here, so offering the manual route is honest.
            _host.Report(Strings.UpdateFailedStatus);
            if (_dialogs.Confirm(
                    string.Format(Strings.UpdateFailedBody, ex.Message), Strings.UpdateFailedTitle))
                _dialogs.OpenLink(update.ReleaseUrl);
        }
        finally
        {
            _host.SetBusy(false);
        }
    }

    /// <summary>Hides the bar for this session only.</summary>
    /// <remarks>
    /// Not for good. Nagging on every launch is rude, and forgetting entirely means the people this
    /// exists for never hear about the fix again.
    /// </remarks>
    private void Dismiss()
    {
        _dismissed = true;
        Refresh();
    }
}
