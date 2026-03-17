using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using XamlToCSharpGenerator.PreviewerHost.Protocol;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

using KeyEventMessage = global::Avalonia.Remote.Protocol.Input.KeyEventMessage;
using RemoteKey = global::Avalonia.Remote.Protocol.Input.Key;
using RemoteInputModifiers = global::Avalonia.Remote.Protocol.Input.InputModifiers;
using RemotePhysicalKey = global::Avalonia.Remote.Protocol.Input.PhysicalKey;
using TextInputEventMessage = global::Avalonia.Remote.Protocol.Input.TextInputEventMessage;

public sealed class AvaloniaDesignerTransportTests
{
    [Fact]
    public async Task SendInitialClientBootstrapAsync_Sends_RenderInfo_And_ViewportAllocation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;

        await using var transport = new AvaloniaDesignerTransport(server.GetStream());
        await transport.SendInitialClientBootstrapAsync(null, null, null, CancellationToken.None);

        using var clientStream = client.GetStream();
        var renderInfo = await ReadMessageAsync(clientStream);
        var viewportAllocated = await ReadMessageAsync(clientStream);

        Assert.Equal(AvaloniaDesignerMessageGuids.ClientRenderInfo, renderInfo.MessageType);
        Assert.Equal(AvaloniaDesignerTransport.DefaultDpi, Assert.IsType<double>(renderInfo.Document["DpiX"]));
        Assert.Equal(AvaloniaDesignerTransport.DefaultDpi, Assert.IsType<double>(renderInfo.Document["DpiY"]));

        Assert.Equal(AvaloniaDesignerMessageGuids.ClientViewportAllocated, viewportAllocated.MessageType);
        Assert.Equal(AvaloniaDesignerTransport.DefaultViewportWidth, Assert.IsType<double>(viewportAllocated.Document["Width"]));
        Assert.Equal(AvaloniaDesignerTransport.DefaultViewportHeight, Assert.IsType<double>(viewportAllocated.Document["Height"]));
        Assert.Equal(AvaloniaDesignerTransport.DefaultDpi, Assert.IsType<double>(viewportAllocated.Document["DpiX"]));
        Assert.Equal(AvaloniaDesignerTransport.DefaultDpi, Assert.IsType<double>(viewportAllocated.Document["DpiY"]));
    }

    [Fact]
    public async Task SendInitialClientBootstrapAsync_Uses_Custom_Viewport_Size()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;

        await using var transport = new AvaloniaDesignerTransport(server.GetStream());
        await transport.SendInitialClientBootstrapAsync(640, 480, null, CancellationToken.None);

        using var clientStream = client.GetStream();
        _ = await ReadMessageAsync(clientStream);
        var viewportAllocated = await ReadMessageAsync(clientStream);

        Assert.Equal(640d, Assert.IsType<double>(viewportAllocated.Document["Width"]));
        Assert.Equal(480d, Assert.IsType<double>(viewportAllocated.Document["Height"]));
    }

    [Fact]
    public async Task SendInitialClientBootstrapAsync_Uses_Custom_Viewport_Scale_For_Dpi()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;

        await using var transport = new AvaloniaDesignerTransport(server.GetStream());
        await transport.SendInitialClientBootstrapAsync(640, 480, 1.5, CancellationToken.None);

        using var clientStream = client.GetStream();
        var renderInfo = await ReadMessageAsync(clientStream);
        var viewportAllocated = await ReadMessageAsync(clientStream);

        Assert.Equal(144d, Assert.IsType<double>(renderInfo.Document["DpiX"]));
        Assert.Equal(144d, Assert.IsType<double>(renderInfo.Document["DpiY"]));
        Assert.Equal(144d, Assert.IsType<double>(viewportAllocated.Document["DpiX"]));
        Assert.Equal(144d, Assert.IsType<double>(viewportAllocated.Document["DpiY"]));
    }

    [Fact]
    public async Task SendKeyEventAsync_Sends_Avalonia_Remote_Key_Message()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;

        await using var transport = new AvaloniaDesignerTransport(server.GetStream());
        await transport.SendKeyEventAsync(
            new KeyEventMessage
            {
                IsDown = true,
                Key = RemoteKey.A,
                PhysicalKey = RemotePhysicalKey.Q,
                KeySymbol = "a",
                Modifiers = [RemoteInputModifiers.Control, RemoteInputModifiers.Shift]
            },
            CancellationToken.None);

        using var clientStream = client.GetStream();
        (Guid messageType, IReadOnlyDictionary<string, object?> document) = await ReadMessageAsync(clientStream);

        Assert.Equal(AvaloniaDesignerMessageGuids.KeyEvent, messageType);
        Assert.True(Assert.IsType<bool>(document["IsDown"]));
        Assert.Equal((int)RemoteKey.A, Assert.IsType<int>(document["Key"]));
        Assert.Equal((int)RemotePhysicalKey.Q, Assert.IsType<int>(document["PhysicalKey"]));
        Assert.Equal("a", Assert.IsType<string>(document["KeySymbol"]));
        int[] modifiers = Assert.IsType<object?[]>(document["Modifiers"]).Select(static value => Assert.IsType<int>(value)).ToArray();
        Assert.True(modifiers.SequenceEqual([(int)RemoteInputModifiers.Control, (int)RemoteInputModifiers.Shift]));
    }

    [Fact]
    public async Task SendTextInputEventAsync_Sends_Avalonia_Remote_Text_Message()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;

        await using var transport = new AvaloniaDesignerTransport(server.GetStream());
        await transport.SendTextInputEventAsync(
            new TextInputEventMessage
            {
                Text = "x",
                Modifiers = [RemoteInputModifiers.Windows]
            },
            CancellationToken.None);

        using var clientStream = client.GetStream();
        (Guid messageType, IReadOnlyDictionary<string, object?> document) = await ReadMessageAsync(clientStream);

        Assert.Equal(AvaloniaDesignerMessageGuids.TextInputEvent, messageType);
        Assert.Equal("x", Assert.IsType<string>(document["Text"]));
        int[] modifiers = Assert.IsType<object?[]>(document["Modifiers"]).Select(static value => Assert.IsType<int>(value)).ToArray();
        Assert.True(modifiers.SequenceEqual([(int)RemoteInputModifiers.Windows]));
    }

    private static async Task<(Guid MessageType, IReadOnlyDictionary<string, object?> Document)> ReadMessageAsync(
        NetworkStream stream)
    {
        (Guid messageType, byte[] payload) = await ReadRawMessageAsync(stream);
        return (
            messageType,
            MinimalBson.DeserializeDocument(payload));
    }

    private static async Task<(Guid MessageType, byte[] Payload)> ReadRawMessageAsync(NetworkStream stream)
    {
        var header = new byte[20];
        await ReadExactAsync(stream, header);

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await ReadExactAsync(stream, payload);
        }

        return (new Guid(header.AsSpan(4, 16)), payload);
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            offset += bytesRead;
        }
    }
}
