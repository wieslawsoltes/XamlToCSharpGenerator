---
title: "Custom Framework Profiles"
---

# Custom Framework Profiles

AXSG was structured so framework-specific logic is pluggable rather than embedded directly in the compiler host.

This is why the repo ships:

- `XamlToCSharpGenerator.Framework.Abstractions`
- `XamlToCSharpGenerator.Avalonia`
- `XamlToCSharpGenerator.NoUi`

Use these when you want to:

- add a new framework profile
- test the host with a reduced surface
- reuse the parsing/configuration pipeline outside Avalonia
