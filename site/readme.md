---
title: "XamlToCSharpGenerator"
layout: simple
og_type: website
---

<div class="axsg-hero">
  <div class="axsg-eyebrow"><i class="bi bi-code-square" aria-hidden="true"></i> Avalonia XAML Compiler and Tooling</div>
  <h1>XamlToCSharpGenerator</h1>
  <p class="lead"><strong>AXSG</strong> compiles Avalonia XAML into generated C#, adds compiled bindings, inline C#, hot reload, and ships the runtime, language service, and editor surfaces around that compiler.</p>
  <div class="axsg-hero-actions">
    <a class="btn btn-primary btn-lg" href="articles/getting-started/overview"><i class="bi bi-rocket-takeoff" aria-hidden="true"></i> Start Getting Started</a>
    <a class="btn btn-outline-secondary btn-lg" href="articles/reference/package-guides/"><i class="bi bi-box-seam" aria-hidden="true"></i> Browse Packages</a>
    <a class="btn btn-outline-secondary btn-lg" href="api"><i class="bi bi-braces-asterisk" aria-hidden="true"></i> Browse API</a>
  </div>
</div>

## Start Here

<div class="axsg-link-grid">
  <a class="axsg-link-card" href="articles/getting-started/overview">
    <span class="axsg-link-card-title"><i class="bi bi-signpost-split" aria-hidden="true"></i> Overview</span>
    <p>Choose the right install surface and understand the compiler/runtime split before you wire AXSG into an app.</p>
  </a>
  <a class="axsg-link-card" href="articles/getting-started/quickstart">
    <span class="axsg-link-card-title"><i class="bi bi-lightning-charge" aria-hidden="true"></i> Quickstart</span>
    <p>Install the standard package, enable source-generated views, and validate the first generated build.</p>
  </a>
  <a class="axsg-link-card" href="articles/xaml/csharp-expressions">
    <span class="axsg-link-card-title"><i class="bi bi-braces" aria-hidden="true"></i> C# Expressions</span>
    <p>Use shorthand expressions, interpolation, typed sources, and expression-aware tooling inside valid XAML.</p>
  </a>
  <a class="axsg-link-card" href="articles/xaml/inline-csharp">
    <span class="axsg-link-card-title"><i class="bi bi-filetype-cs" aria-hidden="true"></i> Inline C#</span>
    <p>Author `CSharp` markup, CDATA blocks, and inline event code while keeping the document valid XAML.</p>
  </a>
</div>

## Documentation Sections

<div class="axsg-link-grid axsg-link-grid--wide">
  <a class="axsg-link-card" href="articles/getting-started">
    <span class="axsg-link-card-title"><i class="bi bi-play-circle" aria-hidden="true"></i> Getting Started</span>
    <p>Installation, first integration, and quickstart paths for the standard AXSG app workflow.</p>
  </a>
  <a class="axsg-link-card" href="articles/concepts">
    <span class="axsg-link-card-title"><i class="bi bi-diagram-3" aria-hidden="true"></i> Concepts</span>
    <p>Compiler host, configuration precedence, generated artifacts, binding semantics, and tooling surface.</p>
  </a>
  <a class="axsg-link-card" href="articles/guides">
    <span class="axsg-link-card-title"><i class="bi bi-journal-code" aria-hidden="true"></i> Guides</span>
    <p>Task-oriented walkthroughs for packaging, release, hot reload, runtime operation, and deployment.</p>
  </a>
  <a class="axsg-link-card" href="articles/xaml">
    <span class="axsg-link-card-title"><i class="bi bi-filetype-xml" aria-hidden="true"></i> XAML Usage</span>
    <p>Compiled bindings, shorthand expressions, inline C#, templates, styles, control themes, and theme resources.</p>
  </a>
  <a class="axsg-link-card" href="articles/architecture">
    <span class="axsg-link-card-title"><i class="bi bi-cpu" aria-hidden="true"></i> Architecture</span>
    <p>Compiler pipeline internals, runtime/bootstrap behavior, and the language-service/editor stack.</p>
  </a>
  <a class="axsg-link-card" href="articles/advanced">
    <span class="axsg-link-card-title"><i class="bi bi-speedometer2" aria-hidden="true"></i> Advanced</span>
    <p>Performance work, testing strategy, docs/release plumbing, and custom framework-profile extension points.</p>
  </a>
  <a class="axsg-link-card" href="articles/reference">
    <span class="axsg-link-card-title"><i class="bi bi-collection" aria-hidden="true"></i> Reference</span>
    <p>Package guides, namespace entry points, configuration reference, artifact matrix, and API coverage maps.</p>
  </a>
  <a class="axsg-link-card" href="api">
    <span class="axsg-link-card-title"><i class="bi bi-braces-asterisk" aria-hidden="true"></i> API Reference</span>
    <p>Generated .NET API pages for the shipped compiler, runtime, language service, and editor assemblies.</p>
  </a>
</div>

## Shipped Artifact Families

<div class="axsg-link-grid axsg-link-grid--wide">
  <a class="axsg-link-card" href="articles/reference/xamltocsharpgenerator/">
    <span class="axsg-link-card-title"><i class="bi bi-box-seam" aria-hidden="true"></i> App and Build Packages</span>
    <p>`XamlToCSharpGenerator`, `Build`, and the standard app-facing install surface for generated Avalonia XAML.</p>
  </a>
  <a class="axsg-link-card" href="articles/reference/compiler/">
    <span class="axsg-link-card-title"><i class="bi bi-gear-wide-connected" aria-hidden="true"></i> Compiler Packages</span>
    <p>`Compiler`, `Core`, `Framework.Abstractions`, `Avalonia`, `NoUi`, and `Generator` for compiler composition.</p>
  </a>
  <a class="axsg-link-card" href="articles/reference/runtime/">
    <span class="axsg-link-card-title"><i class="bi bi-lightning" aria-hidden="true"></i> Runtime Packages</span>
    <p>`Runtime`, `Runtime.Core`, and `Runtime.Avalonia` for generated view bootstrap, registries, and hot reload.</p>
  </a>
  <a class="axsg-link-card" href="articles/reference/language-service/">
    <span class="axsg-link-card-title"><i class="bi bi-search" aria-hidden="true"></i> Tooling Packages</span>
    <p>`LanguageService`, `LanguageServer.Tool`, `Editor.Avalonia`, and the bundled VS Code extension.</p>
  </a>
</div>
