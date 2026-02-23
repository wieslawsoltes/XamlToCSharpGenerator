using System;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGenCrudSample;

internal static class ExceptionLogging
{
    private static int _globalHandlersInstalled;

    public static void InstallGlobalHandlers()
    {
        if (Interlocked.Exchange(ref _globalHandlersInstalled, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                WriteException(
                    "AppDomain.CurrentDomain.UnhandledException",
                    exception,
                    isTerminating: eventArgs.IsTerminating);
                return;
            }

            Console.Error.WriteLine(
                $"[{DateTimeOffset.Now:O}] [AppDomain.CurrentDomain.UnhandledException] " +
                $"Non-exception object: {eventArgs.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            WriteException("TaskScheduler.UnobservedTaskException", eventArgs.Exception, isTerminating: false);
        };
    }

    private static void WriteException(string source, Exception exception, bool isTerminating)
    {
        Console.Error.WriteLine(
            $"[{DateTimeOffset.Now:O}] [{source}] " +
            $"Terminating={isTerminating} " +
            $"{exception.GetType().FullName}: {exception.Message}");
        Console.Error.WriteLine(exception);
    }
}
