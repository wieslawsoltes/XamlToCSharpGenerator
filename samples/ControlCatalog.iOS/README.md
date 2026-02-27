# ControlCatalog.iOS - SourceGen Hot Reload

This sample is the iOS validation target for AXSG hot reload.

## Run on simulator with `dotnet watch`

```bash
cd /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.iOS
AXSG_HOTRELOAD_TRACE=1 dotnet watch ./ControlCatalog.iOS.csproj
```

## Optional remote endpoint override

Use when you want explicit remote transport instead of auto behavior.

```bash
AXSG_HOTRELOAD_TRACE=1 \
AXSG_HOTRELOAD_TRANSPORT_MODE=RemoteOnly \
AXSG_HOTRELOAD_REMOTE_ENDPOINT=tcp://127.0.0.1:45820 \
dotnet watch ./ControlCatalog.iOS.csproj
```

## Device workflow baseline

1. Configure endpoint in project or environment:
   - `AvaloniaSourceGenHotReloadRemoteEndpoint`
   - or `AXSG_HOTRELOAD_REMOTE_ENDPOINT`
2. Set transport mode to `RemoteOnly` for deterministic device behavior.
3. Ensure phone and host are on the same network and endpoint is reachable.

## Reference

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/docs/hot-reload-ios.md`
