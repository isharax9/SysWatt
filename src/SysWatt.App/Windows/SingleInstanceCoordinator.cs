using System.IO.Pipes;

namespace SysWatt.App.Windows;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\SysWatt-0F48330C-A9CE-401C-9F63-E696C96CE64B";
    private const string PipeName = "SysWatt.Activation.0F48330C";
    private readonly Mutex _mutex;
    private CancellationTokenSource? _serverCancellation;

    public bool IsPrimary { get; }

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        IsPrimary = created;
    }

    public async Task SignalPrimaryAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
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

    private static async Task ListenAsync(Action activate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
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
