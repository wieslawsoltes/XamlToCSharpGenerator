# AXSG Language Service Performance Plan (2026-03-04)

## Goal
Improve end-to-end responsiveness of completion/hover/definition/references/semantic tokens without reducing language-service fidelity.

## Scope
- In-scope: `/src/XamlToCSharpGenerator.LanguageService`, `/src/XamlToCSharpGenerator.LanguageServer`, related tests.
- Out-of-scope: compiler semantic changes, protocol feature additions, reducing diagnostics coverage.

## Current Hotspots
1. Repeated analysis reuse issues:
- Analysis cache keyed only by `uri+version` can mix requests with different options (`IncludeCompilationDiagnostics`, `IncludeSemanticDiagnostics`, `WorkspaceRoot`).
- Causes avoidable re-analysis churn and incorrect cache reuse under mixed request profiles.

2. Repeated namespace-prefix map allocations:
- Completion/hover/definition/references all rebuild prefix maps from parsed document on each request.

3. Semantic token recomputation:
- Semantic tokens are fully re-tokenized for repeated requests at same document version.

4. Cross-file reference scanning:
- Reference service still does broad scanning for CLR symbol references; partial mitigations exist (text prefilter + source cache), but indexing remains future work.

## Performance Strategy
| Phase | Focus | Expected impact | Risk |
| --- | --- | --- | --- |
| A | Correct/fast analysis cache partitioning by option profile | Faster mixed request flows, avoids cache poisoning | Low |
| B | Share precomputed prefix-map in analysis result | Lower per-request allocations | Low |
| C | Document-version semantic token cache | Faster semantic token requests | Low |
| D | Reference index (symbol -> source ranges) over cached XAML sources | Major speedup for repeated references | Medium |
| E | Optional LSP-level request result cache with short TTL | Smoother editor UX for repeated same-position queries | Medium |

## Phase A-C Implementation Notes
- Add analysis cache key: `(uri, workspaceRoot, includeCompilationDiagnostics, includeSemanticDiagnostics)`.
- Extend `XamlAnalysisResult` to carry immutable `PrefixMap`.
- Consume `analysis.PrefixMap` in completion/hover/definition/reference services.
- Add semantic token cache key `(uri, version)` in `XamlLanguageServiceEngine`.
- Invalidate all per-uri caches on open/update/close.

## Guard Rails
- Preserve existing diagnostics/completion/definition/reference behavior.
- Add tests for mixed option profiles and cache invalidation correctness.
- Keep path deterministic; no heuristic fallbacks.

## Validation Matrix
1. Build
- `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/XamlToCSharpGenerator.LanguageService.csproj`
- `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj`

2. Tests
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlLanguageServiceEngineTests"`
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~LspServerIntegrationTests"`

## Completion Criteria
- Option-profile cache correctness validated by tests.
- Prefix-map rebuild removed from request hot paths.
- Semantic token repeated calls for unchanged doc serve from cache.
- No regression in existing LS integration tests.
