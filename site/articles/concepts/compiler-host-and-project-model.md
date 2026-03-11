---
title: "Compiler Host and Project Model"
---

# Compiler Host and Project Model

AXSG is split into a framework-agnostic compiler host and framework-specific binder/emitter layers.

## Host responsibilities

The compiler host is responsible for:

- reading XAML and AdditionalFiles from Roslyn/MSBuild
- normalizing paths, links, include URIs, and target paths
- merging configuration files, MSBuild properties, and assembly metadata
- building the document graph for includes, transforms, and generated outputs
- dispatching documents to framework profiles

The main entry point is documented in the generated API for:

- <xref:XamlToCSharpGenerator.Compiler.XamlSourceGeneratorCompilerHost>

## Framework profile split

Framework-specific behavior is implemented in profile packages rather than the host itself.

Primary profile contracts:

- <xref:XamlToCSharpGenerator.Framework.Abstractions.IXamlFrameworkProfile>
- <xref:XamlToCSharpGenerator.Core.Abstractions.IXamlDocumentEnricher>
- <xref:XamlToCSharpGenerator.Core.Abstractions.IXamlSemanticBinder>
- <xref:XamlToCSharpGenerator.Core.Abstractions.IXamlCodeEmitter>

## Why this matters

This separation is what allows AXSG to ship:

- the Avalonia profile package
- the NoUi pilot profile
- the shared compiler host
- runtime/tooling packages that do not have to reimplement graph/configuration logic
