using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Models;
using MdPipe.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// The one piece of MdPipe that can leave somebody without a working executable, so it is tested
/// against a real HTTP server and real files on disk rather than against mocks of both.
/// </summary>
public sealed class AppUpdateInstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "mdpipe-update-tests", Guid.NewGuid().ToString("N"));

    private readonly HttpListener _server = new();
    private readonly string _prefix;

    private byte[] _served = "the new version"u8.ToArray();
    private string? _servedHash;
    private bool _publishHash = true;

    public AppUpdateInstallerTests()
    {
        Directory.CreateDirectory(_folder);

        // A real listener on a free port. Port 0 is not available to HttpListener, so this walks up
        // from a high one until it finds a port nobody has taken.
        var port = 8100;
        while (true)
        {
            try
            {
                _prefix = $"http://127.0.0.1:{port}/";
                _server.Prefixes.Clear();
                _server.Prefixes.Add(_prefix);
                _server.Start();
                break;
            }
            catch (HttpListenerException)
            {
                if (++port > 8200) throw;
            }
        }

        _ = Task.Run(ServeAsync);
    }

    private string ExePath => Path.Combine(_folder, "MdPipe.exe");
    private string DownloadUrl => _prefix + "MdPipe.exe";

    private async Task ServeAsync()
    {
        while (_server.IsListening)
        {
            HttpListenerContext context;
            try { context = await _server.GetContextAsync(); }
            catch (Exception) { return; }

            var path = context.Request.Url!.AbsolutePath;
            var response = context.Response;

            if (path.EndsWith(".sha256"))
            {
                if (!_publishHash)
                {
                    response.StatusCode = 404;
                }
                else
                {
                    var body = Encoding.ASCII.GetBytes(
                        (_servedHash ?? Convert.ToHexString(SHA256.HashData(_served))) + "  MdPipe.exe\n");
                    response.OutputStream.Write(body);
                }
            }
            else
            {
                response.ContentLength64 = _served.Length;
                response.OutputStream.Write(_served);
            }

            response.Close();
        }
    }

    private AppUpdateInstaller Sut() => new(
        NullLogger<AppUpdateInstaller>.Instance, new SingleClientFactory(), ExePath);

    private static AppUpdate Update(string downloadUrl) =>
        new("0.9.0", "https://example.invalid/releases", "notes", Critical: false, downloadUrl);

    [Fact]
    public async Task AVerifiedDownload_ReplacesTheExecutableAndKeepsTheOldOne()
    {
        File.WriteAllText(ExePath, "the running version");

        var launch = await Sut().InstallAsync(Update(DownloadUrl));

        launch.Should().Be(ExePath);
        File.ReadAllBytes(ExePath).Should().Equal(_served);
        File.Exists(ExePath + ".old").Should().BeTrue("the outgoing version is locked until this process ends");
        File.ReadAllText(ExePath + ".old").Should().Be("the running version");
    }

    [Fact]
    public async Task AHashThatDoesNotMatch_LeavesEverythingExactlyAsItWas()
    {
        // The case that matters. A corrupted or substituted download must not reach the disk.
        File.WriteAllText(ExePath, "the running version");
        _servedHash = new string('a', 64);

        var act = () => Sut().InstallAsync(Update(DownloadUrl));

        await act.Should().ThrowAsync<AppUpdateException>().WithMessage("*does not match*");
        File.ReadAllText(ExePath).Should().Be("the running version");
        File.Exists(ExePath + ".old").Should().BeFalse();
    }

    [Fact]
    public async Task NoPublishedHash_MeansNoUpdate()
    {
        // Rather than install something nobody checked. The browser at least brings SmartScreen.
        File.WriteAllText(ExePath, "the running version");
        _publishHash = false;

        var act = () => Sut().InstallAsync(Update(DownloadUrl));

        await act.Should().ThrowAsync<AppUpdateException>();
        File.ReadAllText(ExePath).Should().Be("the running version");
    }

    [Fact]
    public async Task AnUnreadableHashFile_MeansNoUpdate()
    {
        File.WriteAllText(ExePath, "the running version");
        _servedHash = "not a hash at all";

        var act = () => Sut().InstallAsync(Update(DownloadUrl));

        await act.Should().ThrowAsync<AppUpdateException>();
        File.ReadAllText(ExePath).Should().Be("the running version");
    }

    [Fact]
    public async Task WithNowhereToDownloadFrom_ItRefusesBeforeTouchingAnything()
    {
        File.WriteAllText(ExePath, "the running version");

        var act = () => Sut().InstallAsync(Update(downloadUrl: ""));

        await act.Should().ThrowAsync<AppUpdateException>();
        File.ReadAllText(ExePath).Should().Be("the running version");
    }

    [Fact]
    public async Task NothingIsLeftBehindWhenAnUpdateFails()
    {
        // A half-downloaded file sitting next to the executable forever would be its own small bug.
        File.WriteAllText(ExePath, "the running version");
        _servedHash = new string('b', 64);

        try { await Sut().InstallAsync(Update(DownloadUrl)); } catch (AppUpdateException) { }

        Directory.GetFiles(_folder).Should().ContainSingle().Which.Should().Be(ExePath);
    }

    [Fact]
    public async Task TheNextLaunchSweepsUpTheRetiredVersion()
    {
        File.WriteAllText(ExePath, "the running version");
        await Sut().InstallAsync(Update(DownloadUrl));
        File.Exists(ExePath + ".old").Should().BeTrue();

        // Nothing holds it open here, which is the situation on the launch after an update.
        Sut().CleanUpPreviousUpdate();

        File.Exists(ExePath + ".old").Should().BeFalse();
    }

    [Fact]
    public void AFolderItCannotWriteTo_IsRefusedUpFront()
    {
        var installer = new AppUpdateInstaller(
            NullLogger<AppUpdateInstaller>.Instance,
            new SingleClientFactory(),
            Path.Combine(_folder, "no-such-folder", "MdPipe.exe"));

        installer.CanInstall.Should().BeFalse();
    }

    [Fact]
    public void AWritableFolder_IsAccepted()
    {
        Sut().CanInstall.Should().BeTrue();
    }

    public void Dispose()
    {
        try { _server.Stop(); } catch (Exception) { }
        ((IDisposable)_server).Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (Exception) { }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
