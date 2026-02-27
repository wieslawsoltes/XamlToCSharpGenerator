using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class RemoteSocketTransportTests
{
    [Fact]
    public void Capabilities_Require_Explicit_Port_For_Tcp_Scheme()
    {
        var originalEndpoint = Environment.GetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, "tcp://127.0.0.1");

            var transport = new RemoteSocketTransport();
            var capabilities = transport.Capabilities;

            Assert.False(capabilities.IsSupported);
            Assert.Contains("Invalid", capabilities.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, originalEndpoint);
        }
    }

    [Fact]
    public void Capabilities_Accept_Explicit_Tcp_Port()
    {
        var originalEndpoint = Environment.GetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, "tcp://127.0.0.1:4040");

            var transport = new RemoteSocketTransport();
            var capabilities = transport.Capabilities;

            Assert.True(capabilities.IsSupported);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, originalEndpoint);
        }
    }

    [Fact]
    public void Capabilities_Accept_WebSocket_Endpoint_Url()
    {
        var originalEndpoint = Environment.GetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, "ws://127.0.0.1:4040/axsg");

            var transport = new RemoteSocketTransport();
            var capabilities = transport.Capabilities;

            Assert.True(capabilities.IsSupported);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, originalEndpoint);
        }
    }

    [Fact]
    public async Task Handshake_Receives_Apply_Request_And_Publishes_Ack()
    {
        var originalEndpoint = Environment.GetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestReceived = new ManualResetEventSlim(initialState: false);
        var ackReceived = new ManualResetEventSlim(initialState: false);
        SourceGenHotReloadRemoteUpdateRequest? observedRequest = null;
        string? ackPayload = null;

        try
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, "tcp://127.0.0.1:" + port);

            var transport = new RemoteSocketTransport();
            transport.RemoteUpdateReceived += request =>
            {
                observedRequest = request;
                requestReceived.Set();
                transport.PublishRemoteUpdateResult(
                    new SourceGenHotReloadRemoteUpdateResult(
                        OperationId: request.OperationId,
                        RequestId: request.RequestId,
                        CorrelationId: request.CorrelationId,
                        State: SourceGenStudioOperationState.Succeeded,
                        IsSuccess: true,
                        Message: "Applied."));
            };

            var serverTask = Task.Run(() =>
            {
                using var socket = listener.AcceptTcpClient();
                using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
                {
                    AutoFlush = true
                };

                _ = reader.ReadLine();
                _ = reader.ReadLine();

                writer.WriteLine("{\"messageType\":\"apply\",\"operationId\":7,\"requestId\":\"req-7\",\"correlationId\":17,\"typeNames\":[\"Demo.Type\"]}");
                ackPayload = reader.ReadLine();
                ackReceived.Set();
            });

            var handshake = transport.StartHandshake(TimeSpan.FromSeconds(2));
            Assert.True(handshake.IsSuccess);
            Assert.True(requestReceived.Wait(3000));
            Assert.True(ackReceived.Wait(3000));
            await serverTask.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.NotNull(observedRequest);
            Assert.Equal(7L, observedRequest!.OperationId);
            Assert.Equal("req-7", observedRequest.RequestId);
            Assert.Equal(17L, observedRequest.CorrelationId);
            Assert.Contains("\"messageType\":\"ack\"", ackPayload, StringComparison.Ordinal);
            Assert.Contains("\"operationId\":7", ackPayload, StringComparison.Ordinal);

            transport.Stop();
        }
        finally
        {
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, originalEndpoint);
        }
    }
}
