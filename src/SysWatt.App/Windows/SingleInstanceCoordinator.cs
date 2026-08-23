using System.IO.Pipes;

namespace SysWatt.App.Windows;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _serverCancellation;

    public bool IsPrimary { get; }

    public SingleInstanceCoordinator(string? discriminator = null)
    {
        var suffix = string.IsNullOrWhiteSpace(discriminator) ? string.Empty : $".{discriminator}";
        _pipeName = $"SysWatt.Activation.0F48330C{suffix}";
        _mutex = new Mutex(true, $"Local\\SysWatt-0F48330C-A9CE-401C-9F63-E696C96CE64B{suffix}", out var created);
        IsPrimary = created;
    }

    public async Task SignalPrimaryAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            await client.ConnectAsync(1000);
            await client.WriteAsync(new byte[] { 1 });
        }
        catch (TimeoutException) { }
    }

    public void StartListening(Action activate)
    {
        if (!IsPrimary) return;
        _serverCancellation = new CancellationTokenSource();
        _ = ListenAsync(activate, _serverCancellation.Token);
    }

    private async Task ListenAsync(Action activate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[1];
                if (await server.ReadAsync(buffer, cancellationToken) > 0) activate();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    public void Dispose()
    {
        _serverCancellation?.Cancel();
        _serverCancellation?.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
