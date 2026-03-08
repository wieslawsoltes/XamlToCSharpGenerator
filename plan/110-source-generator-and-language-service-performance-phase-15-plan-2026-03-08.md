# Phase 15 - Compiler Host Configuration Precedence Parsing

## Goal
Reduce setup overhead in `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` by removing `Split`/`Substring`/`Replace` churn from configuration precedence parsing while preserving precedence semantics and diagnostics.

## Hotspot
The remaining setup path in `ResolveConfigurationSourcePrecedence(...)` still used:
- `string.Split(...)` over mixed delimiters
- `Substring(...).Trim()` for every key/value pair
- `Replace("_", string.Empty).Replace("-", string.Empty)` in key normalization

This path runs on every configuration snapshot and is pure text tokenization, so it is a good fit for `ReadOnlySpan<char>` scanners that stay netstandard2.0-compatible.

## Optimization Matrix
| Area | Method | Baseline cost | Change | Expected win | Test gate |
| --- | --- | --- | --- | --- | --- |
| Compiler host setup | `ResolveConfigurationSourcePrecedence(...)` | `Split` + substring-heavy parsing | single-pass span tokenizer | lower allocations, faster snapshot setup | direct unit tests + benchmark |
| Compiler host setup | `NormalizeConfigurationPrecedenceKey(...)` | `Trim().Replace().Replace()` | span normalization into stack/pooled buffer | lower allocations on valid path | unit tests |
| Compiler host setup | integer parsing | `int.TryParse(string)` after substring | manual invariant span parser | avoid value substring allocation | unit tests |

## Implementation
Accepted:
- added internal overload `ResolveConfigurationSourcePrecedence(string?, Builder)` for direct testing and benchmarking
- rewrote segment parsing to scan mixed delimiters (`;`, `,`, `\r`, `\n`) in one pass
- reused existing span `TrimWhitespace(...)`
- added `TryParseInvariantInt32(ReadOnlySpan<char>, out int)`
- rewrote precedence key normalization to span-based normalization using stackalloc/ArrayPool
- widened nested `ConfigurationPrecedenceKey` and `ConfigurationSourcePrecedence` visibility to `internal` so tests can validate the contract directly

## Tests
Added:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostConfigurationPrecedenceTests.cs`
  - mixed delimiters and key aliases
  - invalid segments preserve valid values and emit warnings

Extended:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`
  - `CompilerHost_ConfigurationPrecedenceParsing_Outperforms_Baseline`

## Benchmark result
Command:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHost_ConfigurationPrecedenceParsing_Outperforms_Baseline" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

Measured best-of-5:
- baseline: `526.75 ms`, `1,721,600,040 B`
- optimized: `378.66 ms`, `598,400,040 B`
- elapsed: `28.1%` faster
- allocations: `65.2%` lower

## Validation
Focused correctness:
```bash
dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHostConfigurationPrecedenceTests|FullyQualifiedName~CompilerHostConfigurationIntegrationTests" --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Perf slice:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHost_ConfigurationPrecedenceParsing_Outperforms_Baseline" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

Decision: keep. The improvement is well above noise and the semantics are directly test-covered.

Full perf suite:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```
- `24` passed

Full suite:
```bash
dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```
- `1168` passed
- `25` skipped
- `0` failed
- duration `5 m 4 s`
