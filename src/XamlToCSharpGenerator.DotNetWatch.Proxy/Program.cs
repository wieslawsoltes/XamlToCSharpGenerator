using System.Buffers;
using System.IO.Pipes;
using System.Text;

using var trace = ProxyTraceLogger.Create();

var pipeName = ParsePipeName(args);
if (string.IsNullOrWhiteSpace(pipeName))
{
    trace.Log("Missing --pipe-name argument.");
    Console.Error.WriteLine("Missing --pipe-name <name>.");
    return 1;
}

trace.Log("Starting dotnet-watch proxy. Pipe='" + pipeName + "', pid=" + Environment.ProcessId + ".");

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
    trace.Log("Cancellation requested from console.");
};

try
{
    using var pipe = new NamedPipeClientStream(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous);

    trace.Log("Connecting to named pipe '" + pipeName + "'.");
    await pipe.ConnectAsync(cancellation.Token).ConfigureAwait(false);
    trace.Log("Connected to named pipe '" + pipeName + "'.");
    await WriteInitializationResponseAsync(pipe, cancellation.Token).ConfigureAwait(false);
    trace.Log("Initialization response sent.");

    while (!cancellation.IsCancellationRequested)
    {
        var requestType = await ReadByteAsync(pipe, cancellation.Token).ConfigureAwait(false);
        trace.Log("Received request type " + requestType + ".");
        switch (requestType)
        {
            case 1:
                await SkipManagedCodeUpdateRequestAsync(pipe, cancellation.Token).ConfigureAwait(false);
                await WriteUpdateResponseAsync(pipe, success: true, cancellation.Token).ConfigureAwait(false);
                break;

            case 2:
                await SkipStaticAssetUpdateRequestAsync(pipe, cancellation.Token).ConfigureAwait(false);
                await WriteUpdateResponseAsync(pipe, success: true, cancellation.Token).ConfigureAwait(false);
                break;

            case 3:
                // Initial updates applied.
                break;

            default:
                trace.Log("Received unknown request type " + requestType + "; shutting down proxy.");
                return 0;
        }
    }
}
catch (OperationCanceledException)
{
    trace.Log("Proxy canceled.");
    return 0;
}
catch (EndOfStreamException)
{
    trace.Log("Pipe disconnected.");
    return 0;
}
catch (Exception ex)
{
    trace.Log("Proxy failed: " + ex);
    return 0;
}

trace.Log("Proxy exiting normally.");
return 0;

static string? ParsePipeName(string[] commandLineArguments)
{
    for (var index = 0; index < commandLineArguments.Length; index++)
    {
        if (!string.Equals(commandLineArguments[index], "--pipe-name", StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= commandLineArguments.Length)
        {
            return null;
        }

        return commandLineArguments[index + 1];
    }

    return commandLineArguments.Length == 1 ? commandLineArguments[0] : null;
}

static async Task WriteInitializationResponseAsync(Stream stream, CancellationToken cancellationToken)
{
    await WriteByteAsync(stream, 0, cancellationToken).ConfigureAwait(false);
    await WriteStringAsync(stream, string.Empty, cancellationToken).ConfigureAwait(false);
    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
}

static async Task WriteUpdateResponseAsync(Stream stream, bool success, CancellationToken cancellationToken)
{
    await WriteByteAsync(stream, success ? (byte)1 : (byte)0, cancellationToken).ConfigureAwait(false);
    await WriteInt32Async(stream, 0, cancellationToken).ConfigureAwait(false);
    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
}

static async Task SkipManagedCodeUpdateRequestAsync(Stream stream, CancellationToken cancellationToken)
{
    var version = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
    if (version != 4)
    {
        throw new NotSupportedException("Unsupported managed update request version " + version + ".");
    }

    var updateCount = await ReadInt32Async(stream, cancellationToken).ConfigureAwait(false);
    for (var i = 0; i < updateCount; i++)
    {
        await SkipBytesAsync(stream, 16, cancellationToken).ConfigureAwait(false);
        await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false);
        await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false);
        await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false);
        await SkipInt32ArrayAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    _ = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
}

static async Task SkipStaticAssetUpdateRequestAsync(Stream stream, CancellationToken cancellationToken)
{
    var version = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
    if (version != 2)
    {
        throw new NotSupportedException("Unsupported static asset update request version " + version + ".");
    }

    _ = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
    _ = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
    _ = await ReadStringAsync(stream, cancellationToken).ConfigureAwait(false);
    await SkipByteArrayAsync(stream, cancellationToken).ConfigureAwait(false);
    _ = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
}

static async Task SkipByteArrayAsync(Stream stream, CancellationToken cancellationToken)
{
    var length = await ReadInt32Async(stream, cancellationToken).ConfigureAwait(false);
    if (length < 0)
    {
        throw new InvalidDataException("Negative byte array length.");
    }

    await SkipBytesAsync(stream, length, cancellationToken).ConfigureAwait(false);
}

static async Task SkipInt32ArrayAsync(Stream stream, CancellationToken cancellationToken)
{
    var count = await ReadInt32Async(stream, cancellationToken).ConfigureAwait(false);
    if (count < 0)
    {
        throw new InvalidDataException("Negative integer array length.");
    }

    await SkipBytesAsync(stream, checked(count * sizeof(int)), cancellationToken).ConfigureAwait(false);
}

static async Task SkipBytesAsync(Stream stream, int count, CancellationToken cancellationToken)
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

static async Task<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
{
    var buffer = new byte[1];
    await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
    return buffer[0];
}

static async Task<int> ReadInt32Async(Stream stream, CancellationToken cancellationToken)
{
    var buffer = new byte[4];
    await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
    return BitConverter.ToInt32(buffer, 0);
}

static async Task<string> ReadStringAsync(Stream stream, CancellationToken cancellationToken)
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

static async Task<int> Read7BitEncodedIntAsync(Stream stream, CancellationToken cancellationToken)
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

static async Task WriteByteAsync(Stream stream, byte value, CancellationToken cancellationToken)
{
    var buffer = new[] { value };
    await stream.WriteAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
}

static async Task WriteInt32Async(Stream stream, int value, CancellationToken cancellationToken)
{
    var buffer = BitConverter.GetBytes(value);
    await stream.WriteAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
}

static async Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    await Write7BitEncodedIntAsync(stream, bytes.Length, cancellationToken).ConfigureAwait(false);
    if (bytes.Length > 0)
    {
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}

static async Task Write7BitEncodedIntAsync(Stream stream, int value, CancellationToken cancellationToken)
{
    var remaining = unchecked((uint)value);
    while (remaining > 0x7F)
    {
        await WriteByteAsync(stream, (byte)(remaining | 0x80), cancellationToken).ConfigureAwait(false);
        remaining >>= 7;
    }

    await WriteByteAsync(stream, (byte)remaining, cancellationToken).ConfigureAwait(false);
}

internal sealed class ProxyTraceLogger : IDisposable
{
    private readonly bool _enabled;
    private readonly object _sync = new();
    private readonly StreamWriter? _fileWriter;

    private ProxyTraceLogger(bool enabled, StreamWriter? fileWriter)
    {
        _enabled = enabled;
        _fileWriter = fileWriter;
    }

    public static ProxyTraceLogger Create()
    {
        var traceEnabled = IsTrue(Environment.GetEnvironmentVariable("AXSG_DOTNET_WATCH_PROXY_TRACE")) ||
                           IsTrue(Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_TRACE"));
        var logPath = Environment.GetEnvironmentVariable("AXSG_DOTNET_WATCH_PROXY_LOG");

        StreamWriter? fileWriter = null;
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            try
            {
                var directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                fileWriter = new StreamWriter(File.Open(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };
            }
            catch
            {
                fileWriter = null;
            }
        }

        return new ProxyTraceLogger(traceEnabled || fileWriter is not null, fileWriter);
    }

    public void Log(string message)
    {
        if (!_enabled)
        {
            return;
        }

        var line = "[" + DateTime.UtcNow.ToString("O") + "] " + message;
        lock (_sync)
        {
            if (_fileWriter is null)
            {
                Console.Error.WriteLine(line);
                return;
            }

            _fileWriter.WriteLine(line);
            Console.Error.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _fileWriter?.Dispose();
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
