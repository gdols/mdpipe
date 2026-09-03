using System.Globalization;
using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf.Resources;
using MdPipe.Wpf.Services;
using MdPipe.Wpf.ViewModels;

namespace MdPipe.Wpf.Tests;

/// <summary>
/// The bar that offers a newer MdPipe, and what happens when somebody clicks it.
/// </summary>
/// <remarks>
/// These used to live in the view model's tests, alongside converting documents, because the notice
/// lived there too. Nothing about it involves files, folders or engines, and testing it no longer
/// requires standing up any of that.
/// </remarks>
[Collection("ui-strings")]
public sealed class UpdateNoticeViewModelTests : IDisposable
{
    private const string RunningVersion = "0.6.1";

    private readonly CultureInfo? _previousCulture = Strings.Culture;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeInstaller _installer = new();
    private readonly FakeHost _host = new();

    public UpdateNoticeViewModelTests() => Strings.Culture = new CultureInfo("en");

    public void Dispose() => Strings.Culture = _previousCulture;

    private UpdateNoticeViewModel Sut() => new(
        new AppUpdateService(new VersionGateService()), _installer, _dialogs, _host, RunningVersion);

    private static AppRelease Release(string version, string criticalBelow = "", bool downloadable = true) =>
        new(version, "https://example.invalid/releases",
            criticalBelow, "notes", downloadable ? "https://example.invalid/MdPipe.exe" : "");

    // --- what it says -----------------------------------------------------------------------

    [Fact]
    public void WithANewerRelease_ThereIsSomethingToSay()
    {
        var sut = Sut();

        sut.Consider(Release("0.9.0"));

        sut.HasNotice.Should().BeTrue();
        sut.Message.Should().Contain("0.9.0").And.Contain(RunningVersion);
    }

    [Fact]
    public void RunningTheNewestRelease_ThereIsNothingToSay()
    {
        var sut = Sut();

        sut.Consider(Release(RunningVersion));

        sut.HasNotice.Should().BeFalse();
        sut.Message.Should().BeEmpty();
    }

    [Fact]
    public void WithNothingFromTheRepository_ThereIsNothingToSay()
    {
        // Either an older manifest with no release block, or GitHub being unreachable.
        var sut = Sut();

        sut.Consider(null);

        sut.HasNotice.Should().BeFalse();
    }

    [Fact]
    public void AVersionOldEnoughToHaveAKnownProblem_GetsTheBlunterWording()
    {
        var sut = Sut();

        sut.Consider(Release("0.9.0", criticalBelow: "0.7.0"));

        sut.Update!.Critical.Should().BeTrue();
        sut.Message.Should().Contain("known problem");
    }

    [Fact]
    public void WhenItCanReplaceItself_TheLinkOffersToDoTheWork()
    {
        var sut = Sut();

        sut.Consider(Release("0.9.0"));

        sut.CanInstall.Should().BeTrue();
        sut.ActionText.Should().Be(Strings.UpdateInstall);
    }

    [Fact]
    public void WhenItCannotReplaceItself_TheLinkOnlyOffersThePage()
    {
        _installer.CanInstall = false;
        var sut = Sut();

        sut.Consider(Release("0.9.0"));

        sut.CanInstall.Should().BeFalse();
        sut.ActionText.Should().Be(Strings.UpdateGetIt);
    }

    [Fact]
    public void Dismissing_HidesItForThisSession()
    {
        var sut = Sut();
        sut.Consider(Release("0.9.0"));

        sut.DismissCommand.Execute(null);

        sut.HasNotice.Should().BeFalse();
    }

    // --- what it does -----------------------------------------------------------------------

    [Fact]
    public void ItAsksBeforeReplacingAnything_AndNoMeansNo()
    {
        _dialogs.ConfirmAnswer = false;
        var sut = Sut();
        sut.Consider(Release("0.9.0"));

        sut.InstallCommand.Execute(null);

        _dialogs.Confirmations.Should().ContainSingle();
        _installer.Installed.Should().BeEmpty();
        _dialogs.RestartedWith.Should().BeNull();
    }

    [Fact]
    public async Task SayingYes_InstallsAndRestarts()
    {
        var sut = Sut();
        sut.Consider(Release("0.9.0"));

        sut.InstallCommand.Execute(null);
        for (var i = 0; i < 200 && _dialogs.RestartedWith is null; i++) await Task.Delay(10);

        _installer.Installed.Should().ContainSingle().Which.Version.Should().Be("0.9.0");
        _dialogs.RestartedWith.Should().NotBeNull();
        _host.Busy.Should().BeFalse("the window is given back afterwards");
    }

    [Fact]
    public void WhereItCannotWriteItself_ItOpensThePageWithoutAsking()
    {
        // A portable executable on read-only media or under Program Files. There is nothing to
        // confirm when the browser is doing the work.
        _installer.CanInstall = false;
        var sut = Sut();
        sut.Consider(Release("0.9.0"));

        sut.InstallCommand.Execute(null);

        _dialogs.Links.Should().ContainSingle().Which.Should().Be("https://example.invalid/releases");
        _dialogs.Confirmations.Should().BeEmpty();
        _installer.Installed.Should().BeEmpty();
    }

    [Fact]
    public void WithNoDownloadAddress_ItAlsoJustOpensThePage()
    {
        var sut = Sut();
        sut.Consider(Release("0.9.0", downloadable: false));

        sut.InstallCommand.Execute(null);

        _dialogs.Links.Should().ContainSingle();
        _installer.Installed.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenItFails_NothingBreaksAndTheManualRouteIsOffered()
    {
        _installer.ThrowOnInstall = new AppUpdateException("the checksum did not match");
        var sut = Sut();
        sut.Consider(Release("0.9.0"));

        sut.InstallCommand.Execute(null);
        for (var i = 0; i < 200 && _host.Busy; i++) await Task.Delay(10);

        _dialogs.RestartedWith.Should().BeNull();
        _dialogs.Links.Should().ContainSingle("the second question offers the download page");
        _host.Reported.Should().Contain(Strings.UpdateFailedStatus);
        _host.Busy.Should().BeFalse("a failed update must not leave the window stuck");
    }

    [Fact]
    public void WhileTheWindowIsBusyWithSomethingElse_TheLinkIsNotOffered()
    {
        _host.Busy = true;
        var sut = Sut();
        sut.Consider(Release("0.9.0"));

        sut.InstallCommand.CanExecute(null).Should().BeFalse();
    }

    // --- doubles ----------------------------------------------------------------------------

    private sealed class FakeHost : IUpdateHost
    {
        public bool Busy { get; set; }
        public List<string> Reported { get; } = [];

        bool IUpdateHost.IsBusy => Busy;
        public void SetBusy(bool busy) => Busy = busy;
        public void Report(string status) => Reported.Add(status);
    }

    private sealed class FakeInstaller : IAppUpdateInstaller
    {
        public bool CanInstall { get; set; } = true;
        public Exception? ThrowOnInstall { get; set; }
        public List<AppUpdate> Installed { get; } = [];

        public Task<string> InstallAsync(
            AppUpdate update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOnInstall is not null) return Task.FromException<string>(ThrowOnInstall);

            Installed.Add(update);
            return Task.FromResult(@"C:\somewhere\MdPipe.exe");
        }

        public void CleanUpPreviousUpdate() { }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmAnswer { get; set; } = true;
        public List<(string Message, string Title)> Confirmations { get; } = [];
        public List<string> Links { get; } = [];
        public string? RestartedWith { get; private set; }

        public bool Confirm(string message, string title)
        {
            Confirmations.Add((message, title));
            return ConfirmAnswer;
        }

        public void OpenLink(string url) => Links.Add(url);
        public void RestartWith(string executablePath) => RestartedWith = executablePath;
        public void ShowMessage(string message, string title, DialogKind kind) { }
        public string? PickFolder(string title) => null;
        public void OpenFolder(string path) { }
    }
}
