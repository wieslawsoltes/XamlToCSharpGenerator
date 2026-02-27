using System;
using System.Buffers;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class DotNetWatchNamedPipeBridge : IDisposable
{
    internal const string PipeDirectoryEnvVarName = "AXSG_DOTNET_WATCH_PIPE_DIRECTORY";

    private readonly string _pipeName;
    private readonly Action<string> _trace;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly object _sync = new();
    private Task? _pumpTask;
    private bool _started;

    public DotNetWatchNamedPipeBridge(string pipeName, Action<string> trace)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name must be provided.", nameof(pipeName));
        }

        _pipeName = pipeName.Trim();
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _pumpTask = Task.Run(() => RunAsync(_cancellationTokenSource.Token));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
        }

        try
        {
            _pumpTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Best effort shutdown.
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _trace("Dotnet-watch bridge connecting to named pipe '" + _pipeName + "'.");
                using var pipe = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                await WriteInitializationResponseAsync(pipe, cancellationToken).ConfigureAwait(false);
                _trace("Dotnet-watch bridge connected.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    var requestType = await ReadByteAsync(pipe, cancellationToken).ConfigureAwait(false);
                    switch (requestType)
                    {
                        case 1:
                            await SkipManagedCodeUpdateRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
                            await WriteUpdateResponseAsync(pipe, success: true, cancellationToken).ConfigureAwait(false);
                            break;

                        case 2:
                            await SkipStaticAssetUpdateRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
                            await WriteUpdateResponseAsync(pipe, success: true, cancellationToken).ConfigureAwait(false);
                            break;

                        case 3:
                            // Initial updates completed. No response required.
                            break;

                        default:
                            _trace("Dotnet-watch bridge received unknown request type " + requestType + ".");
                            return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
                break;
            }
            catch (EndOfStreamException)
            {
                _trace("Dotnet-watch bridge disconnected.");
            }
            catch (IOException exception)
            {
                _trace("Dotnet-watch bridge IO failure: " + exception.Message);
            }
            catch (Exception exception)
            {
                _trace("Dotnet-watch bridge failure: " + exception.Message);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        var unixSocketPath = TryResolveUnixSocketPath();
        if (!string.IsNullOrWhiteSpace(unixSocketPath))
        {
            _trace("Dotnet-watch bridge using unix socket path '" + unixSocketPath + "'.");
            return await ConnectUnixSocketAsync(unixSocketPath!, cancellationToken).ConfigureAwait(false);
        }

        var namedPipeStream = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await namedPipeStream.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return namedPipeStream;
        }
        catch
        {
            namedPipeStream.Dispose();
            throw;
        }
    }

    private string? TryResolveUnixSocketPath()
    {
        if (!OperatingSystem.IsIOS() &&
            !OperatingSystem.IsTvOS() &&
            !OperatingSystem.IsAndroid())
        {
            return null;
        }

        var pipeDirectory = Environment.GetEnvironmentVariable(PipeDirectoryEnvVarName);
        if (string.IsNullOrWhiteSpace(pipeDirectory))
        {
            return null;
        }

        var socketPath = Path.Combine(pipeDirectory.Trim(), "CoreFxPipe_" + _pipeName);
        if (socketPath.Length > 104)
        {
            _trace("Dotnet-watch bridge unix socket path is too long (" + socketPath.Length + "): " + socketPath);
            return null;
        }

        return socketPath;
    }

    private static async Task<Stream> ConnectUnixSocketAsync(string socketPath, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            var endPoint = new UnixDomainSocketEndPoint(socketPath);
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task WriteInitializationResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        // ResponseType.InitializationResponse
        await WriteByteAsync(stream, 1, cancellationToken).ConfigureAwait(false);
        // ClientInitializationResponse.Version
        await WriteByteAsync(stream, 0, cancellationToken).ConfigureAwait(false);
        await WriteStringAsync(stream, ResolveCapabilities(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteUpdateResponseAsync(Stream stream, bool success, CancellationToken cancellationToken)
    {
        // ResponseType.UpdateResponse
        await WriteByteAsync(stream, 2, cancellationToken).ConfigureAwait(false);
        await WriteByteAsync(stream, success ? (byte)1 : (byte)0, cancellationToken).ConfigureAwait(false);
        await WriteInt32Async(stream, 0, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SkipManagedCodeUpdateRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var version = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        if (version != 4)
        {
            throw new NotSupportedException("Unsupported managed update request version " + version + ".");
        }

        var updateCount = await ReadInt32Async(stream, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < updateCount; i++)
        {
            await SkipBytesAsync(stream, 16, cancellationToken).ConfigureAwait(false); // Guid
            await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false); // Metadata
            await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false); // IL
            await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false); // PDB
            await SkipInt32ArrayAsync(stream, cancellationToken).ConfigureAwait(false); // Updated types
        }

        // ResponseLoggingLevel
        _ = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SkipStaticAssetUpdateRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var version = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        if (version != 2)
        {
            throw new NotSupportedException("Unsupported static asset update request version " + version + ".");
        }

        _ = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
        _ = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false); // bool
        _ = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
        await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false);
        _ = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false); // ResponseLoggingLevel
    }

    private static string ResolveCapabilities() => string.Empty;

    private static async Task SkipByteArrayAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = await ReadInt32Async(stream, cancellationToken).ConfigureAwait(false);
        if (length < 0)
        {
            throw new InvalidDataException("Negative byte array length.");
        }

        await SkipBytesAsync(stream, length, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SkipInt32ArrayAsync(Stream stream, CancellationToken cancellationToken)
    {
        var count = await ReadInt32Async(stream, cancellationToken).ConfigureAwait(false);
        if (count < 0)
        {
            throw new InvalidDataException("Negative integer array length.");
        }

        await SkipBytesAsync(stream, checked(count * sizeof(int)), cancellationToken).ConfigureAwait(false);
    }

    private static async Task SkipBytesAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(count, 4096));
        try
        {
            var remaining = count;
            while (remaining > 0)
            {
                var toRead = Math.Min(remaining, buffer.Length);
                await stream.ReadExactlyAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                remaining -= toRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return buffer[0];
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4];
        await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return BitConverter.ToInt32(buffer, 0);
    }

    private static async Task<string> ReadStringAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = await Read7BitEncodedIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (length < 0)
        {
            throw new InvalidDataException("Negative string length.");
        }

        var buffer = new byte[length];
        if (length > 0)
        {
            await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task<int> Read7BitEncodedIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = 0;
        var shift = 0;

        while (shift < 35)
        {
            var currentByte = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
            result |= (currentByte & 0x7F) << shift;
            if ((currentByte & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }

        throw new InvalidDataException("Malformed 7-bit encoded integer.");
    }

    private static async Task WriteByteAsync(Stream stream, byte value, CancellationToken cancellationToken)
    {
        var buffer = new[] { value };
        await stream.WriteAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteInt32Async(Stream stream, int value, CancellationToken cancellationToken)
    {
        var buffer = BitConverter.GetBytes(value);
        await stream.WriteAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        await Write7BitEncodedIntAsync(stream, bytes.Length, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task Write7BitEncodedIntAsync(Stream stream, int value, CancellationToken cancellationToken)
    {
        var remaining = unchecked((uint)value);
        while (remaining > 0x7F)
        {
            await WriteByteAsync(stream, (byte)(remaining | 0x80), cancellationToken).ConfigureAwait(false);
            remaining >>= 7;
        }

        await WriteByteAsync(stream, (byte)remaining, cancellationToken).ConfigureAwait(false);
    }
}
