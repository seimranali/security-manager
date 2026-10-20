using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Domain.Models;

namespace SecurityManager.Scanner.Infrastructure.Scanning;

/// <summary>TCP connect scanner with bounded concurrency and opportunistic banner grabbing.</summary>
public sealed class TcpPortScanner(IOptions<ScannerOptions> options) : IPortScanner
{
    private readonly ScannerOptions _options = options.Value;

    public async Task<IReadOnlyList<PortProbeResult>> ScanAsync(string host, IReadOnlyList<int> ports, CancellationToken ct = default)
    {
        var address = await ResolveAsync(host, ct);
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentProbes));

        var tasks = ports.Select(async port =>
        {
            await gate.WaitAsync(ct);
            try { return await ProbeAsync(address, port, ct); }
            finally { gate.Release(); }
        });

        var results = await Task.WhenAll(tasks);
        return results.OrderBy(r => r.Port).ToList();
    }

    private static async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
               ?? addresses.FirstOrDefault()
               ?? throw new InvalidOperationException($"Could not resolve host '{host}'.");
    }

    private async Task<PortProbeResult> ProbeAsync(IPAddress address, int port, CancellationToken ct)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_options.ConnectTimeoutMs);

        try
        {
            await client.ConnectAsync(address, port, connectCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PortProbeResult(port, false);
        }
        catch (SocketException)
        {
            return new PortProbeResult(port, false);
        }

        return new PortProbeResult(port, true, await GrabBannerAsync(client, ct));
    }

    private async Task<string?> GrabBannerAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(_options.BannerTimeoutMs);
            var buffer = new byte[256];
            var read = await client.GetStream().ReadAsync(buffer, readCts.Token);
            if (read <= 0) return null;

            var text = Encoding.ASCII.GetString(buffer, 0, read);
            var printable = new string(text.Where(c => c >= 32 && c < 127).ToArray()).Trim();
            return printable.Length == 0 ? null : printable.Length > 200 ? printable[..200] : printable;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            return null;
        }
    }
}
