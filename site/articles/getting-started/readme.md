---
title: "Getting Started"
---

# Getting Started

This section is for first-time adopters and for people returning to the repo after the feature surface has grown. It explains what AXSG is, which artifacts to install, how to wire a project, and where to look next once the first build succeeds.

## Recommended path

1. Read [Why AXSG](overview/) to understand what you get beyond a normal XAML toolchain.
2. Read [Installation](installation/) to choose the right package or tool entry point.
3. Read [InitializeComponent and Loader Fallback](initializecomponent-and-loader-fallback/) if the app already has hand-written `AvaloniaXamlLoader.Load(this)` code-behind.
4. Read [Avalonia Name Generator and InitializeComponent Modes](../guides/avalonia-name-generator-and-initializecomponent-modes/) if you need the backend and compatibility matrix behind class-backed initialization.
5. Read [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/) if you want AXSG to preserve supported legacy loader calls during migration.
6. Follow [Quickstart](quickstart/) to get a project building with generated output.
7. Walk through [Samples and Feature Tour](samples-and-feature-tour/) to see the supported language and runtime features in one place.

## After the first successful build

- If you care about XAML authoring features, continue with [XAML](../xaml/).
- If you are working in VS Code, jump to [VS Code and Language Service](../guides/vscode-language-service/).
- If you are integrating AXSG into a larger build or package graph, use [Package Selection and Integration](../guides/package-selection-and-integration/).

- [Overview](overview/)
- [Installation](installation/)
- [InitializeComponent and Loader Fallback](initializecomponent-and-loader-fallback/)
- [Avalonia Name Generator and InitializeComponent Modes](../guides/avalonia-name-generator-and-initializecomponent-modes/)
- [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/)
- [Quickstart](quickstart/)
- [Samples and Feature Tour](samples-and-feature-tour/)
