using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Infrastructure.Manifest;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// Covers how the remote manifest fetch reports failure, which decides whether the fallback chain
/// underneath it ever gets a turn.
/// </summary>
public sealed class GitHubManifestProviderTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _serverLifetime = new();

    public GitHubManifestProviderTests() => _listener.Start();

    /// <summary>
    /// Accepts the connection and then says nothing, which is what a hung proxy, a captive portal or
    /// an SSL-inspecting firewall looks like from the client side. Holding the sockets matters: a
    /// closed connection would be a plain request failure and would miss the point.
    /// </summary>
    private Uri StartSilentServer()
    {
        var held = new List<TcpClient>();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_serverLifetime.IsCancellationRequested)
                    held.Add(await _listener.AcceptTcpClientAsync(_serverLifetime.Token));
            }
            catch (OperationCanceledException) { }
            finally
            {
                foreach (var client in held) client.Dispose();
            }
        });

        return new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/manifest.json");
    }

    [Fact]
    public async Task WhenTheServerAcceptsAndNeverAnswers_ReportsAManifestFailure()
    {
        // HttpClient signals its own timeout with a TaskCanceledException, not an HttpRequestException.
        // That used to travel straight past FallbackManifestProvider, so the baked-in baseline never
        // ran and MdPipe refused to start on a machine where the engine was installed and fine.
        using var http = new HttpClient { BaseAddress = StartSilentServer(), Timeout = TimeSpan.FromMilliseconds(300) };
        var sut = new GitHubManifestProvider(http, NullLogger<GitHubManifestProvider>.Instance);

        var act = () => sut.GetManifestAsync();

        await act.Should().ThrowAsync<ManifestException>();
    }

    [Fact]
    public async Task WhenTheCallerCancels_TheCancellationIsNotDisguisedAsAFailure()
    {
        // The other half of the same catch: a user pressing Ctrl+C is not a broken network, and
        // turning it into a ManifestException would send MdPipe off to install things anyway.
        using var http = new HttpClient { BaseAddress = StartSilentServer(), Timeout = TimeSpan.FromSeconds(30) };
        var sut = new GitHubManifestProvider(http, NullLogger<GitHubManifestProvider>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var act = () => sut.GetManifestAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WhenNothingIsListening_ReportsAManifestFailure()
    {
        // The case that always worked, kept so the new catch can't quietly replace the old one.
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _listener.Stop();

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/manifest.json") };
        var sut = new GitHubManifestProvider(http, NullLogger<GitHubManifestProvider>.Instance);

        var act = () => sut.GetManifestAsync();

        await act.Should().ThrowAsync<ManifestException>();
    }

    public void Dispose()
    {
        _serverLifetime.Cancel();
        try { _listener.Stop(); } catch (SocketException) { }
        _serverLifetime.Dispose();
    }
}
