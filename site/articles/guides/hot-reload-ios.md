---
title: "iOS Hot Reload"
---

# iOS Hot Reload Guide (AXSG + `dotnet watch`)

This guide covers SourceGen hot reload setup for iOS simulator and physical devices.

## 1) Prerequisites

1. iOS workload is installed.
2. Xcode and simulator are installed.
3. Project uses:
   - `<AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>`
   - `<AvaloniaSourceGenHotReloadEnabled>true</AvaloniaSourceGenHotReloadEnabled>`
4. Run from Debug configuration.

## 2) Simulator quickstart

```bash
cd /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.iOS
AXSG_HOTRELOAD_TRACE=1 dotnet watch ./ControlCatalog.iOS.csproj
```

Expected startup signals:

1. `dotnet watch 🔨 Build succeeded`
2. `xcrun simctl launch ... Avalonia.ControlCatalog`
3. App process id line, for example: `Avalonia.ControlCatalog: 96930`

During iOS watch runs, AXSG build targets forward/derive:

1. `AXSG_HOTRELOAD_TRANSPORT_MODE`
2. `AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS`
3. `AXSG_HOTRELOAD_REMOTE_ENDPOINT` (when configured or auto-selected for simulator)

## 3) Device quickstart

For physical devices, set an explicit remote endpoint reachable from device.

```xml
<PropertyGroup>
  <AvaloniaSourceGenIosHotReloadTransportMode>RemoteOnly</AvaloniaSourceGenIosHotReloadTransportMode>
  <AvaloniaSourceGenHotReloadRemoteEndpoint>tcp://192.168.1.10:45820</AvaloniaSourceGenHotReloadRemoteEndpoint>
  <AvaloniaSourceGenHotReloadRemoteRequireExplicitDeviceEndpoint>true</AvaloniaSourceGenHotReloadRemoteRequireExplicitDeviceEndpoint>
</PropertyGroup>
```

Then run:

```bash
AXSG_HOTRELOAD_TRACE=1 dotnet watch ./ControlCatalog.iOS.csproj
```

Requirements:

1. Device and host are on the same network.
2. Endpoint host/IP is reachable from device.
3. Endpoint service accepts AXSG remote protocol payloads.

## 4) Key configuration surface

MSBuild properties:

1. `AvaloniaSourceGenIosHotReloadEnabled` (`true` by default for Debug iOS)
2. `AvaloniaSourceGenIosHotReloadUseInterpreter` (`true` by default for Debug iOS)
3. `AvaloniaSourceGenIosHotReloadTransportMode` (`Auto|MetadataOnly|RemoteOnly`)
4. `AvaloniaSourceGenIosHotReloadHandshakeTimeoutMs` (default `3000`)
5. `AvaloniaSourceGenHotReloadRemoteEndpoint`
6. `AvaloniaSourceGenHotReloadRemotePort` (default `45820`)
7. `AvaloniaSourceGenHotReloadRemoteAutoSimulatorEndpointEnabled` (default `true`)
8. `AvaloniaSourceGenHotReloadRemoteRequireExplicitDeviceEndpoint` (default `true`)
9. `AvaloniaSourceGenIosHotReloadForwardStartupHooks` (default `false`)
10. `AvaloniaSourceGenIosHotReloadForwardWatchEnvironment` (default `true`)

Environment overrides:

1. `AXSG_HOTRELOAD_TRACE=1`
2. `AXSG_HOTRELOAD_TRANSPORT_MODE=Auto|MetadataOnly|RemoteOnly`
3. `AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS=<milliseconds>`
4. `AXSG_HOTRELOAD_REMOTE_ENDPOINT=<endpoint>`

## 5) Remote endpoint formats

Accepted `AXSG_HOTRELOAD_REMOTE_ENDPOINT` formats:

1. `host:port`
2. `tcp://host:port`
3. `ws://host:port/path`
4. `wss://host:port/path`

Port is required for all formats.

## 6) Troubleshooting matrix

| Startup signal | Meaning | Action |
| --- | --- | --- |
| `Waiting for application to connect to pipe ...` | Metadata hot reload channel not established yet | Keep `TransportMode=Auto` for simulator fallback or set explicit remote endpoint and `RemoteOnly`. |
| `[AXSG.HotReload.iOS] Physical-device remote fallback requires a reachable host endpoint.` | Device run without endpoint while endpoint is required | Set `AvaloniaSourceGenHotReloadRemoteEndpoint` (or `AXSG_HOTRELOAD_REMOTE_ENDPOINT`) to reachable host:port. |
| `Invalid AXSG_HOTRELOAD_REMOTE_ENDPOINT value ...` | Endpoint format rejected | Use one of: `host:port`, `tcp://host:port`, `ws://host:port/path`, `wss://host:port/path`. |
| `No precompiled XAML found for avares://...` during startup | Include resolved before source-generated registry entry became available or stale outputs | Clean `bin/obj`, rebuild, and ensure referenced project is part of build graph; if recurring, capture `AXSG_HOTRELOAD_TRACE=1` logs. |
| `Failed to load AOT module ... while running in aot-only mode` | Debug build/runtime mismatch or stale AOT artifacts | Ensure Debug iOS with interpreter enabled, clean `bin/obj`, rebuild/redeploy simulator app. |
| `Microsoft.iOS: Socket error while connecting to IDE on 127.0.0.1:10000: Connection refused` | IDE debugging channel not attached | Usually non-blocking for `dotnet watch`; if needed, start from IDE debugger instead of pure CLI run. |

## 7) Sample project

See:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.iOS/README.md`
