using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageServer.Protocol;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class LspMessageWriterTests
{
    [Fact]
    public async Task WriteAsync_DoesNotPassCallerCancellationToTransportWrites()
    {
        using var stream = new RecordingStream();
        var writer = new LspMessageWriter(stream);
        using var cancellation = new CancellationTokenSource();

        await writer.WriteAsync(new { jsonrpc = "2.0", result = "ok" }, cancellation.Token);

        Assert.Equal(2, stream.WriteCallCount);
        Assert.All(stream.WriteCancellationTokens, static token => Assert.False(token.CanBeCanceled));
        Assert.False(stream.FlushCancellationToken.CanBeCanceled);
    }

    private sealed class RecordingStream : Stream
    {
        private readonly MemoryStream _inner = new();

        public int WriteCallCount { get; private set; }

        public List<CancellationToken> WriteCancellationTokens { get; } = new();

        public CancellationToken FlushCancellationToken { get; private set; }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCancellationToken = cancellationToken;
            return _inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            WriteCallCount++;
            WriteCancellationTokens.Add(cancellationToken);
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCallCount++;
            WriteCancellationTokens.Add(cancellationToken);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
