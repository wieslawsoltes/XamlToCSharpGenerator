using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenStudioRemoteDesignServerTests
{
    [Fact]
    public async Task Ping_Command_Returns_Pong()
    {
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"ping\",\"requestId\":\"p1\"}");
            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal("ping", response.GetProperty("command").GetString());

            var payload = response.GetProperty("payload");
            Assert.True(payload.GetProperty("pong").GetBoolean());
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Unknown_Command_Returns_Error()
    {
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"unsupported\"}");
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal("unsupported", response.GetProperty("command").GetString());
            Assert.Contains("Unsupported command", response.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task NonObject_Payload_Returns_Validation_Error_Without_Dropping_Client()
    {
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            await writer.WriteLineAsync("{\"command\":\"selectDocument\",\"payload\":\"invalid\"}");
            var invalidLine = await reader.ReadLineAsync();
            Assert.False(string.IsNullOrWhiteSpace(invalidLine));
            using (var invalidJson = JsonDocument.Parse(invalidLine!))
            {
                var invalidResponse = invalidJson.RootElement;
                Assert.False(invalidResponse.GetProperty("ok").GetBoolean());
                Assert.Equal("selectdocument", invalidResponse.GetProperty("command").GetString());
                Assert.Contains("buildUri is required", invalidResponse.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            }

            // Ensure the same connection remains usable after malformed payload handling.
            await writer.WriteLineAsync("{\"command\":\"ping\",\"requestId\":\"afterInvalid\"}");
            var followUpLine = await reader.ReadLineAsync();
            Assert.False(string.IsNullOrWhiteSpace(followUpLine));
            using (var followUpJson = JsonDocument.Parse(followUpLine!))
            {
                var followUpResponse = followUpJson.RootElement;
                Assert.True(followUpResponse.GetProperty("ok").GetBoolean());
                Assert.Equal("ping", followUpResponse.GetProperty("command").GetString());
            }
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task GetStatus_Command_Returns_Remote_Metadata()
    {
        var port = AllocateTcpPort();
        var options = new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port,
            VncEndpoint = "vnc://127.0.0.1:5900"
        };

        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.Enable(options);
        var server = new XamlSourceGenStudioRemoteDesignServer(options);

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"getStatus\",\"requestId\":\"s1\"}");
            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal("getstatus", response.GetProperty("command").GetString());

            var payload = response.GetProperty("payload");
            Assert.True(payload.GetProperty("isEnabled").GetBoolean());
            Assert.True(payload.GetProperty("options").GetProperty("enableRemoteDesign").GetBoolean());

            var remote = payload.GetProperty("remote");
            Assert.True(remote.GetProperty("isEnabled").GetBoolean());
            Assert.True(remote.GetProperty("isListening").GetBoolean());
            Assert.Equal(port, remote.GetProperty("port").GetInt32());
            Assert.Equal("vnc://127.0.0.1:5900", remote.GetProperty("vncEndpoint").GetString());
        }
        finally
        {
            server.Stop();
            XamlSourceGenStudioManager.Disable();
        }
    }

    [Fact]
    public async Task GetWorkspace_Command_Returns_Expected_Payload_Shape()
    {
        var port = AllocateTcpPort();
        var options = new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        };

        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.Enable(options);
        var server = new XamlSourceGenStudioRemoteDesignServer(options);

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"getWorkspace\"}");
            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal("getworkspace", response.GetProperty("command").GetString());

            var payload = response.GetProperty("payload");
            Assert.True(payload.TryGetProperty("status", out _));
            Assert.True(payload.TryGetProperty("remote", out var remote));
            Assert.Equal(JsonValueKind.Object, remote.ValueKind);
            Assert.True(payload.TryGetProperty("documents", out var documents));
            Assert.Equal(JsonValueKind.Array, documents.ValueKind);
            Assert.True(payload.TryGetProperty("elements", out var elements));
            Assert.Equal(JsonValueKind.Array, elements.ValueKind);
            Assert.True(payload.TryGetProperty("properties", out var properties));
            Assert.Equal(JsonValueKind.Array, properties.ValueKind);
            Assert.True(payload.TryGetProperty("toolbox", out var toolbox));
            Assert.Equal(JsonValueKind.Array, toolbox.ValueKind);
        }
        finally
        {
            server.Stop();
            XamlSourceGenStudioManager.Disable();
        }
    }

    [Fact]
    public async Task GetWorkspace_Command_Includes_Remote_Metadata()
    {
        var port = AllocateTcpPort();
        var options = new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port,
            VncEndpoint = "vnc://127.0.0.1:5900"
        };

        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.Enable(options);
        var server = new XamlSourceGenStudioRemoteDesignServer(options);

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"getWorkspace\"}");
            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal("getworkspace", response.GetProperty("command").GetString());

            var remote = response.GetProperty("payload").GetProperty("remote");
            Assert.True(remote.GetProperty("isEnabled").GetBoolean());
            Assert.True(remote.GetProperty("isListening").GetBoolean());
            Assert.Equal("127.0.0.1", remote.GetProperty("host").GetString());
            Assert.Equal(port, remote.GetProperty("port").GetInt32());
            Assert.True(remote.GetProperty("activeClientCount").GetInt32() >= 0);
            Assert.Equal("vnc://127.0.0.1:5900", remote.GetProperty("vncEndpoint").GetString());
        }
        finally
        {
            server.Stop();
            XamlSourceGenStudioManager.Disable();
        }
    }

    [Fact]
    public async Task GetWorkspace_Trimmed_Request_Metadata_Is_Accepted()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <TextBlock Text=""Hello"" />
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/WorkspaceTrim.axaml";
        var port = AllocateTcpPort();
        var options = new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        };

        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.Enable(options);
        var server = new XamlSourceGenStudioRemoteDesignServer(options);

        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(
                client,
                "{\"command\":\"getWorkspace\",\"requestId\":\"  r1  \",\"payload\":{\"buildUri\":\"  " + buildUri + "  \",\"search\":\"   \"}}");
            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal("r1", response.GetProperty("requestId").GetString());
            Assert.Equal(buildUri, response.GetProperty("payload").GetProperty("activeBuildUri").GetString());
        }
        finally
        {
            server.Stop();
            ResetRuntimeState();
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public async Task SelectDocument_Without_BuildUri_Returns_Error()
    {
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"selectDocument\"}");
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal("selectdocument", response.GetProperty("command").GetString());
            Assert.Contains("buildUri is required", response.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task SelectDocument_Unknown_BuildUri_Returns_Error()
    {
        ResetRuntimeState();
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"selectDocument\",\"payload\":{\"buildUri\":\"avares://tests/does-not-exist.axaml\"}}");
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal("selectdocument", response.GetProperty("command").GetString());
            Assert.Contains("No registered document", response.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.Stop();
            ResetRuntimeState();
        }
    }

    [Fact]
    public async Task SelectDocument_Trimmed_BuildUri_Is_Accepted()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" />");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/TrimmedDoc.axaml";
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            server.Start();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(
                client,
                "{\"command\":\"selectDocument\",\"payload\":{\"buildUri\":\"  " + buildUri + "  \"}}");

            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal(buildUri, response.GetProperty("payload").GetProperty("activeBuildUri").GetString());
        }
        finally
        {
            server.Stop();
            ResetRuntimeState();
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public async Task SelectElement_Without_ElementId_Returns_Error()
    {
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"selectElement\"}");
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal("selectelement", response.GetProperty("command").GetString());
            Assert.Contains("elementId is required", response.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task SelectElement_Unknown_BuildUri_Returns_Error()
    {
        ResetRuntimeState();
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"selectElement\",\"payload\":{\"buildUri\":\"avares://tests/does-not-exist.axaml\",\"elementId\":\"0\"}}");
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal("selectelement", response.GetProperty("command").GetString());
            Assert.Contains("No registered document", response.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.Stop();
            ResetRuntimeState();
        }
    }

    [Fact]
    public async Task SelectElement_Unknown_ElementId_Returns_Error()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel x:Name=""RootPanel"">
    <TextBlock x:Name=""TitleText"" Text=""Hello"" />
  </StackPanel>
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/UnknownElement.axaml";
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var response = await SendRequestAsync(client, "{\"command\":\"selectElement\",\"payload\":{\"buildUri\":\"" + buildUri + "\",\"elementId\":\"0/0/999\"}}");
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal("selectelement", response.GetProperty("command").GetString());
            Assert.Contains("No element with id", response.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.Stop();
            ResetRuntimeState();
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public async Task SelectElement_Trimmed_ElementId_Is_Accepted()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel x:Name=""RootPanel"">
    <TextBlock x:Name=""TitleText"" Text=""Hello"" />
  </StackPanel>
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/TrimmedElement.axaml";
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            server.Start();

            using var selectDocumentClient = new TcpClient();
            await selectDocumentClient.ConnectAsync(IPAddress.Loopback, port);
            var selectDocumentResponse = await SendRequestAsync(
                selectDocumentClient,
                "{\"command\":\"selectDocument\",\"payload\":{\"buildUri\":\"" + buildUri + "\"}}");
            Assert.True(selectDocumentResponse.GetProperty("ok").GetBoolean());

            var elements = selectDocumentResponse.GetProperty("payload").GetProperty("elements");
            var titleTextElementId = FindElementIdByXamlName(elements, "TitleText");
            Assert.False(string.IsNullOrWhiteSpace(titleTextElementId));

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var response = await SendRequestAsync(
                client,
                "{\"command\":\"selectElement\",\"payload\":{\"buildUri\":\"  " + buildUri + "  \",\"elementId\":\"  " + titleTextElementId + "  \"}}");

            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal(titleTextElementId, response.GetProperty("payload").GetProperty("selectedElementId").GetString());
        }
        finally
        {
            server.Stop();
            ResetRuntimeState();
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public async Task ApplyDocumentText_Missing_Fields_Returns_Validation_Error()
    {
        var port = AllocateTcpPort();
        var server = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        });

        try
        {
            server.Start();

            using var clientForMissingBuildUri = new TcpClient();
            await clientForMissingBuildUri.ConnectAsync(IPAddress.Loopback, port);

            var responseMissingBuildUri = await SendRequestAsync(
                clientForMissingBuildUri,
                "{\"command\":\"applyDocumentText\",\"payload\":{\"xamlText\":\"<UserControl />\"}}");
            Assert.False(responseMissingBuildUri.GetProperty("ok").GetBoolean());
            Assert.Equal("applydocumenttext", responseMissingBuildUri.GetProperty("command").GetString());
            Assert.Contains("buildUri is required", responseMissingBuildUri.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

            using var clientForMissingXaml = new TcpClient();
            await clientForMissingXaml.ConnectAsync(IPAddress.Loopback, port);

            var responseMissingXaml = await SendRequestAsync(
                clientForMissingXaml,
                "{\"command\":\"applyDocumentText\",\"payload\":{\"buildUri\":\"avares://sample.axaml\"}}");
            Assert.False(responseMissingXaml.GetProperty("ok").GetBoolean());
            Assert.Equal("applydocumenttext", responseMissingXaml.GetProperty("command").GetString());
            Assert.Contains("xamlText is required", responseMissingXaml.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Remote_Workflow_Selects_And_Applies_Document_Text()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel x:Name=""RootPanel"">
    <TextBlock x:Name=""TitleText"" Text=""Hello"" />
  </StackPanel>
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/RemoteWorkflow.axaml";
        var port = AllocateTcpPort();
        var options = new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port,
            PersistChangesToSource = true,
            WaitMode = SourceGenStudioWaitMode.None
        };
        var server = new XamlSourceGenStudioRemoteDesignServer(options);

        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenStudioManager.Enable(options);
            server.Start();

            using var selectDocumentClient = new TcpClient();
            await selectDocumentClient.ConnectAsync(IPAddress.Loopback, port);
            var selectDocumentResponse = await SendRequestAsync(
                selectDocumentClient,
                "{\"command\":\"selectDocument\",\"payload\":{\"buildUri\":\"" + buildUri + "\"}}");
            Assert.True(selectDocumentResponse.GetProperty("ok").GetBoolean());

            var workspacePayload = selectDocumentResponse.GetProperty("payload");
            Assert.Equal(buildUri, workspacePayload.GetProperty("activeBuildUri").GetString());
            Assert.True(workspacePayload.TryGetProperty("elements", out var elements));

            var titleTextElementId = FindElementIdByXamlName(elements, "TitleText");
            Assert.False(string.IsNullOrWhiteSpace(titleTextElementId));

            using var selectElementClient = new TcpClient();
            await selectElementClient.ConnectAsync(IPAddress.Loopback, port);
            var selectElementResponse = await SendRequestAsync(
                selectElementClient,
                "{\"command\":\"selectElement\",\"payload\":{\"buildUri\":\"" + buildUri + "\",\"elementId\":\"" + titleTextElementId + "\"}}");
            Assert.True(selectElementResponse.GetProperty("ok").GetBoolean());
            Assert.Equal(
                titleTextElementId,
                selectElementResponse.GetProperty("payload").GetProperty("selectedElementId").GetString());

            const string updatedText = "<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><StackPanel x:Name=\"RootPanel\"><TextBlock x:Name=\"TitleText\" Text=\"Updated\" /></StackPanel></Window>";
            using var applyClient = new TcpClient();
            await applyClient.ConnectAsync(IPAddress.Loopback, port);
            var applyResponse = await SendRequestAsync(
                applyClient,
                "{\"command\":\"applyDocumentText\",\"payload\":{\"buildUri\":\"" + buildUri + "\",\"xamlText\":\"" + EscapeJson(updatedText) + "\"}}");
            Assert.True(applyResponse.GetProperty("ok").GetBoolean());

            var applyPayload = applyResponse.GetProperty("payload").GetProperty("applyResult");
            Assert.True(applyPayload.GetProperty("succeeded").GetBoolean());

            var persistedText = File.ReadAllText(sourcePath);
            Assert.Contains("Updated", persistedText, StringComparison.Ordinal);
        }
        finally
        {
            server.Stop();
            ResetRuntimeState();
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void Start_DoesNotThrow_When_Port_Is_In_Use_And_Publishes_Error_Status()
    {
        var port = AllocateTcpPort();
        using var blocker = new TcpListener(IPAddress.Loopback, port);
        blocker.Start();

        var options = new SourceGenStudioOptions
        {
            EnableRemoteDesign = true,
            RemoteHost = "127.0.0.1",
            RemotePort = port
        };

        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.Enable(options);
        var server = new XamlSourceGenStudioRemoteDesignServer(options);

        try
        {
            var exception = Record.Exception(server.Start);
            Assert.Null(exception);
            Assert.False(server.IsStarted);

            var snapshot = XamlSourceGenStudioManager.GetStatusSnapshot();
            Assert.True(snapshot.Remote.IsEnabled);
            Assert.False(snapshot.Remote.IsListening);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Remote.LastError));
        }
        finally
        {
            server.Stop();
            XamlSourceGenStudioManager.Disable();
        }
    }

    private static async Task<JsonElement> SendRequestAsync(TcpClient client, string requestJson)
    {
        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        await writer.WriteLineAsync(requestJson);
        var responseLine = await reader.ReadLineAsync();
        Assert.False(string.IsNullOrWhiteSpace(responseLine));

        using var responseJson = JsonDocument.Parse(responseLine!);
        return responseJson.RootElement.Clone();
    }

    private static string? FindElementIdByXamlName(JsonElement elements, string xamlName)
    {
        if (elements.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (element.TryGetProperty("xamlName", out var xamlNameProperty) &&
                string.Equals(xamlNameProperty.GetString(), xamlName, StringComparison.Ordinal))
            {
                return element.GetProperty("id").GetString();
            }

            if (!element.TryGetProperty("children", out var children))
            {
                continue;
            }

            var match = FindElementIdByXamlName(children, xamlName);
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    private static string EscapeJson(string value)
    {
        var encoded = JsonSerializer.Serialize(value);
        return encoded.Length >= 2 ? encoded.Substring(1, encoded.Length - 2) : value;
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "AXSG-RemoteDesign-" + Guid.NewGuid().ToString("N") + ".axaml");
        File.WriteAllText(path, content.Trim());
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void ResetRuntimeState()
    {
        XamlSourceGenStudioManager.Disable();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }

    private static int AllocateTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var port = endpoint.Port;
        listener.Stop();
        return port;
    }

    private sealed class HotDesignTarget;
}
