# 123) VS Code Previewer Keyboard Input Plan (2026-03-17)

## Goal

Enable keyboard input in the VS Code AXSG previewer so the inline loopback preview path can drive focused Avalonia controls, not just pointer and wheel input.

The target scenarios are:

1. key down and key up for navigation, focus movement, shortcuts, and command gestures;
2. text input for standard typing into `TextBox`-style controls;
3. modifier-aware input (`Ctrl`, `Shift`, `Alt`, `Meta`) routed through the existing preview helper session.

## Root Cause

The current VS Code preview pipeline is split into two different transports:

1. the browser-side inline loopback client connects to Avalonia's HTML preview websocket for rendered frames and mouse/wheel input;
2. the AXSG helper host connects to the Avalonia designer TCP/BSON transport for start/update/hot-reload coordination.

Mouse works today because Avalonia's HTML websocket transport explicitly parses:

1. `pointer-pressed`;
2. `pointer-released`;
3. `pointer-moved`;
4. `scroll`.

Keyboard does not work because that HTML websocket transport does not parse key or text input messages, even though Avalonia's lower-level remote protocol already defines:

1. `KeyEventMessage`;
2. `TextInputEventMessage`.

So parity cannot be achieved by adding more browser websocket messages alone. The missing bridge is between the webview and the helper host.

## Strategy

Do not fork or patch Avalonia's HTML transport.

Instead, route keyboard input through the transport we already own:

1. capture keyboard events inside the VS Code webview canvas client;
2. post normalized keyboard payloads back to the extension host;
3. forward them to the AXSG preview helper through a new helper command;
4. translate those payloads into Avalonia remote `KeyEventMessage` and `TextInputEventMessage`;
5. dispatch them over the existing designer TCP/BSON channel.

This keeps rendering unchanged while using the real Avalonia input protocol for keyboard delivery.

## Design

### A. Add an explicit preview-input helper command

Extend the preview host protocol with a new `input` command and a validated request payload that supports:

1. key events: `isDown`, `key`, `code`, `location`, `keySymbol`, modifiers;
2. text events: `text`, modifiers.

The command must be a no-op success when a browser event cannot be mapped to a usable Avalonia key instead of failing the whole preview session.

### B. Translate DOM keyboard metadata to Avalonia keyboard messages

Add a dedicated mapper in the preview host that converts browser keyboard payloads into Avalonia remote protocol objects.

Mapping rules:

1. use DOM `code` to recover `PhysicalKey` whenever possible;
2. use DOM `key` for logical `Key` where that preserves layout-sensitive typing;
3. fall back to code-based mapping for punctuation, numpad, modifiers, and non-text keys;
4. send `TextInputEventMessage` only for real text-producing input;
5. keep modifier state as the Avalonia remote-protocol modifier array.

### C. Send keyboard input through the existing designer transport

Extend `AvaloniaDesignerTransport` with dedicated send methods for:

1. `KeyEventMessage`;
2. `TextInputEventMessage`.

Use Avalonia's remote protocol BSON serializer for these new input messages so array payloads such as modifier lists stay protocol-accurate.

### D. Capture keyboard input in the inline loopback webview client

Enhance the preview canvas webview script so that:

1. the canvas keeps keyboard focus after pointer interaction;
2. `keydown` posts a key event payload and, when appropriate, a text-input payload;
3. `keyup` posts a key event payload;
4. default browser handling is suppressed for forwarded keys so the preview receives the interaction instead of the webview chrome.

Keep the external iframe preview path unchanged.

### E. Keep the browser-side normalization testable

Move the keyboard payload-shaping rules into reusable helper functions under the VS Code extension helper module so they can be unit-tested without spinning up a webview.

The helper coverage should prove:

1. printable keys emit both key and text payloads;
2. control chords emit only key payloads;
3. modifier metadata is preserved;
4. non-printing navigation keys do not emit text payloads.

## Files To Change

### Preview host protocol and routing

1. `src/XamlToCSharpGenerator.RemoteProtocol/Preview/AxsgPreviewHostPayloads.cs`
2. `src/XamlToCSharpGenerator.RemoteProtocol/Preview/AxsgPreviewHostProtocol.cs`
3. `src/XamlToCSharpGenerator.PreviewerHost/PreviewHostCommandRouter.cs`
4. `src/XamlToCSharpGenerator.PreviewerHost/PreviewSession.cs`

### Designer transport and key mapping

5. `src/XamlToCSharpGenerator.PreviewerHost/Protocol/AvaloniaDesignerTransport.cs`
6. `src/XamlToCSharpGenerator.PreviewerHost/Protocol/AvaloniaDesignerMessages.cs`
7. `src/XamlToCSharpGenerator.PreviewerHost/XamlToCSharpGenerator.PreviewerHost.csproj`
8. new preview-host key-mapping helper file(s) under `src/XamlToCSharpGenerator.PreviewerHost/`

### VS Code extension and webview

9. `tools/vscode/axsg-language-server/preview-webview-helpers.js`
10. `tools/vscode/axsg-language-server/preview-support.js`

### Tests

11. `tests/XamlToCSharpGenerator.Tests/PreviewerHost/AxsgPreviewHostProtocolTests.cs`
12. `tests/XamlToCSharpGenerator.Tests/PreviewerHost/PreviewHostCommandRouterTests.cs`
13. `tests/XamlToCSharpGenerator.Tests/PreviewerHost/AvaloniaDesignerTransportTests.cs`
14. new preview-host mapper tests under `tests/XamlToCSharpGenerator.Tests/PreviewerHost/`
15. `tools/vscode/axsg-language-server/test/preview-webview-helpers.test.js`

## Validation Plan

1. run the focused preview-host unit tests covering protocol parsing, command routing, transport serialization, and keyboard mapping;
2. run the VS Code extension Node tests covering the browser payload normalization helpers;
3. run a broader preview-host test slice to catch regressions around the helper session surface.

## Acceptance Criteria

The work is complete when all of the following are true:

1. clicking the inline VS Code preview canvas gives it focus and subsequent keyboard input reaches the previewed Avalonia tree;
2. printable typing produces `TextInput` behavior in focused text controls instead of only raw key transitions;
3. navigation keys and modifier chords are forwarded with stable logical and physical key metadata;
4. unsupported or unmappable browser keys do not crash or destabilize the preview host;
5. the new protocol, transport, mapper, and webview helper behavior are covered by regression tests.
