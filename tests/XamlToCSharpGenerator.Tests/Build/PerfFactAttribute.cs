using System;

namespace XamlToCSharpGenerator.Tests.Build;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PerfFactAttribute : FactAttribute
{
    private const int DefaultPerfTestTimeoutMilliseconds = 900_000;

    public PerfFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = "Performance harness is disabled. Set AXSG_RUN_PERF_TESTS=true to enable.";
            return;
        }

        Timeout = ReadTimeoutMilliseconds("AXSG_PERF_TEST_TIMEOUT_MS", DefaultPerfTestTimeoutMilliseconds);
    }

    private static int ReadTimeoutMilliseconds(string environmentVariable, int fallbackValue)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallbackValue;
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallbackValue;
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AXSG_RUN_PERF_TESTS");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
