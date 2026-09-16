using System.Windows;
using System.Windows.Input;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf.Mvvm;
using MdPipe.Wpf.Resources;
using MdPipe.Wpf.Services;

namespace MdPipe.Wpf.ViewModels;

/// <summary>
/// The bar that appears when a newer MdPipe exists, and what happens if the user says yes. Knows
/// nothing about files, folders or engines.
/// </summary>
public sealed class UpdateNoticeViewModel : ObservableObject
{
    private readonly AppUpdateService _updates;
    private readonly IAppUpdateInstaller _installer;
    private readonly IDialogService _dialogs;
    private readonly IUpdateHost _host;
    private readonly string? _runningVersion;

    private AppUpdate? _update;
    private bool _dismissed;
    private bool _mightBeTheFix;

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
    /// Files, and anywhere else a portable executable can legitimately be.
    /// </summary>
    public bool CanInstall => Update is { DownloadUrl.Length: > 0 } && _installer.CanInstall;

    /// <summary>What the link offers to do, which depends on whether it can actually do it.</summary>
    public string ActionText => CanInstall ? Strings.UpdateInstall : Strings.UpdateGetIt;

    public string Message => Update is not { } update
        ? string.Empty
        : string.Format(Wording(update), update.Version, _runningVersion);

    /// <summary>
    /// Offering a newer version to somebody staring at a failed start is a different sentence from
    /// offering it to somebody whose copy is working.
    /// </summary>
    private string Wording(AppUpdate update) =>
        _mightBeTheFix ? Strings.UpdateMightFix
        : update.Critical ? Strings.UpdateCritical
        : Strings.UpdateAvailable;

    /// <summary>Works out whether there is anything worth saying, and how to say it.</summary>
    /// <param name="mightBeTheFix">The app could not start, so the newer release is being offered
    /// as a possible remedy rather than as an improvement.</param>
    public void Consider(AppRelease? release, bool mightBeTheFix = false)
    {
        _update = _updates.CheckFor(release, _runningVersion);
        _mightBeTheFix = mightBeTheFix;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Update));
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(Message));

        // WPF only re-asks a command whether it can run when told to. The notice can arrive after a
        // network call, once nothing else is going to trigger that, and the link stayed greyed out.
        Application.Current?.Dispatcher.Invoke(CommandManager.InvalidateRequerySuggested);
    }

    /// <summary>
    /// Replaces MdPipe with the newer release, having asked first. Where the executable cannot be
    /// written it opens the release page instead, without asking: the browser is doing the work.
    /// </summary>
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

    /// <summary>Hides the bar for this session, not for good.</summary>
    private void Dismiss()
    {
        _dismissed = true;
        Refresh();
    }
}
