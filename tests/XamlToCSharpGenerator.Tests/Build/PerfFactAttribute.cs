using System;

namespace XamlToCSharpGenerator.Tests.Build;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = "Performance harness is disabled. Set AXSG_RUN_PERF_TESTS=true to enable.";
        }
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AXSG_RUN_PERF_TESTS");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
