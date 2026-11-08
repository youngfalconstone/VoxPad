using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;
using VoxPad.Core.Logging;
using VoxPad.Core.Remote;

namespace VoxPad.App.Services;

/// <summary>What the pipe server needs from the application; implemented by the main view model and invoked on the UI thread.</summary>
public interface IRemoteTarget
{
    Protocol.Response Handle(Protocol.Request request);
}

/// <summary>Named-pipe Remote Control API server (\\.\pipe\VoxPadRemote). Local, current-user only; one JSON line per request.</summary>
public sealed class RemoteControlServer : IDisposable
{
    private readonly IRemoteTarget _target;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();

    public RemoteControlServer(IRemoteTarget target, Dispatcher dispatcher)
    {
        _target = target; _dispatcher = dispatcher;
    }

    public void Start()
    {
        for (int i = 0; i < 2; i++) _ = AcceptLoop(); // a couple of concurrent listeners so plugins never see "pipe busy"
        Log.Info("Remote control pipe listening: " + Protocol.PipeName);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Protocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token);
                await Serve(server);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { /* client went away mid-request: normal for one-shot clients */ }
            catch (Exception ex) { Log.Warn("Remote pipe: " + ex.Message); await Task.Delay(500); }
        }
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        while (pipe.IsConnected && !_cts.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(_cts.Token); } catch { break; }
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            Protocol.Response response;
            try
            {
                var req = Protocol.Parse(line);
                response = await _dispatcher.InvokeAsync(() => _target.Handle(req));
            }
            catch (Exception ex) { response = Protocol.Fail(ex.Message); }
            try { await writer.WriteLineAsync(Protocol.Serialize(response)); } catch { break; }
        }
    }

    public void Dispose() => _cts.Cancel();
}
