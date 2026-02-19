# Hot Reload Error-Resilience Analysis and Plan

Date: 2026-02-19

## Problem
During `dotnet watch` + hot reload, users often save intermediate invalid AXAML while typing.
Current behavior reports hard generator errors and stops source emission for the file, which can block hot reload updates and destabilize edit loops.

## Observed patterns in other frameworks
1. .NET `dotnet watch`:
   - Hot reload/watch provides explicit session signals (`DotNetWatchBuild`, `DOTNET_WATCH`).
   - Unsupported/failed changes are not applied until a valid update is available.
2. React Native Fast Refresh:
   - Runtime keeps running with syntax/runtime edit errors, then resumes once edits are fixed.
3. Flutter Hot Reload:
   - Compilation errors during hot reload do not crash app state; the invalid update is rejected until fixed.
4. Avalonia.Markup.Declarative (local analysis):
   - Uses metadata update callbacks and resilient runtime reload flow, avoiding app teardown on bad updates.

## Design decision
Use a source-generator-side "last known good" strategy during hot reload/watch sessions:
1. Detect hot reload/watch mode with:
   - `build_property.DotNetWatchBuild == true` OR
   - environment variable `DOTNET_WATCH=1`.
2. On parse/bind/emit failure in that mode:
   - reuse cached last successfully generated source for that AXAML file,
   - downgrade generator error diagnostics to warnings for that edit,
   - emit explicit warning that fallback source is being reused.
3. Outside hot reload/watch mode:
   - keep strict current behavior (errors stay errors; no fallback reuse).

## Contracts added
1. New MSBuild property:
   - `AvaloniaSourceGenHotReloadErrorResilienceEnabled` (default `true`).
2. New diagnostic:
   - `AXSG0700` (`HotReloadFallbackUsed`, Warning).

## Implementation plan
1. Extend `GeneratorOptions` with watch/resilience flags.
2. Extend build-transitive props to expose new property and `DotNetWatchBuild` to analyzer config.
3. Add generator cache keyed by `(AssemblyName, FilePath, TargetPath)` storing `(HintName, Source)`.
4. Update parse pipeline to preserve `XamlFileInput` alongside parse diagnostics.
5. Add fallback path for parse/bind/emit failures in hot reload mode.
6. Add warning demotion mode for diagnostics in hot reload mode.
7. Add regression tests for:
   - fallback reuse when XAML becomes temporarily invalid,
   - strict mode when resilience is disabled.
