using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Client;

public sealed class ClaudeClient : IDisposable
{
    private const string ClientNotConnectedMessagePrefix = "Client not connected. Call ";
    private const string ClientNotConnectedMessageSuffix = " first.";

    private readonly ClaudeOptions _options;
    private readonly bool _autoStart;
    private readonly ConnectionState _connectionState;

    public ClaudeClient(ClaudeClientOptions? options = null)
        : this(options, null)
    {
    }

    public ClaudeClient(ClaudeOptions options)
        : this(CreateClientOptions(options), null)
    {
    }

    internal ClaudeClient(ClaudeClientOptions? options, ClaudeExec? exec)
    {
        var resolvedOptions = options ?? new ClaudeClientOptions();
        _options = resolvedOptions.ClaudeOptions ?? new ClaudeOptions();
        _autoStart = resolvedOptions.AutoStart;
        _connectionState = new ConnectionState(exec);
    }

    public ClaudeClientState State => _connectionState.GetSnapshot();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connectionState.Start(CreateExec);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connectionState.Stop();
        return Task.CompletedTask;
    }

    public ClaudeThread StartThread(ThreadOptions? options = null)
    {
        var exec = GetOrCreateExec();
        return new ClaudeThread(exec, _options, options ?? new ThreadOptions());
    }

    public ClaudeThread ResumeThread(string id, ThreadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var exec = GetOrCreateExec();
        return new ClaudeThread(exec, _options, options ?? new ThreadOptions(), id);
    }

    public ClaudeCliMetadata GetCliMetadata()
    {
        var executablePath = ClaudeCliLocator.FindClaudePath(_options.ClaudeExecutablePath);
        return ClaudeCliMetadataReader.Read(executablePath);
    }

    public ClaudeCliUpdateStatus GetCliUpdateStatus()
    {
        var executablePath = ClaudeCliLocator.FindClaudePath(_options.ClaudeExecutablePath);
        return ClaudeCliMetadataReader.ReadUpdateStatus(executablePath);
    }

    public void Dispose() => _connectionState.Dispose();

    private ClaudeExec GetOrCreateExec() => _connectionState.GetOrCreate(_autoStart, CreateExec);

    private ClaudeExec CreateExec()
    {
        return new ClaudeExec(
            _options.ClaudeExecutablePath,
            _options.EnvironmentVariables,
            _options.Settings,
            _options.Logger);
    }

    private static ClaudeClientOptions CreateClientOptions(ClaudeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ClaudeClientOptions
        {
            ClaudeOptions = options,
            AutoStart = true,
        };
    }

    private sealed class ConnectionState
    {
        private readonly Lock _gate = new();
        private ClaudeExec? _exec;
        private bool _disposed;

        internal ConnectionState(ClaudeExec? exec)
        {
            _exec = exec;
        }

        internal ClaudeClientState GetSnapshot()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return ClaudeClientState.Disposed;
                }

                return _exec is null
                    ? ClaudeClientState.Disconnected
                    : ClaudeClientState.Connected;
            }
        }

        internal void Start(Func<ClaudeExec> execFactory)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _exec ??= execFactory();
            }
        }

        internal void Stop()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _exec = null;
            }
        }

        internal ClaudeExec GetOrCreate(bool autoStart, Func<ClaudeExec> execFactory)
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (_exec is not null)
                {
                    return _exec;
                }

                if (!autoStart)
                {
                    throw new InvalidOperationException(
                        string.Concat(ClientNotConnectedMessagePrefix, nameof(StartAsync), ClientNotConnectedMessageSuffix));
                }

                _exec = execFactory();
                return _exec;
            }
        }

        internal void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _exec = null;
                _disposed = true;
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(ClaudeClient));
    }
}
