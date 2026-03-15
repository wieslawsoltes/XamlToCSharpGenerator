namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class Program
{
    public static void Main(string[] args)
    {
        var (options, forwardedArguments) = ParseArguments(args);
        SourceGeneratedRuntimeXamlLoaderInstaller.Install(
            options.CompilerMode,
            options.PreviewWidth,
            options.PreviewHeight);
        global::Avalonia.DesignerSupport.Remote.RemoteDesignerEntryPoint.Main(forwardedArguments);
    }

    private static (PreviewHostOptions Options, string[] ForwardedArguments) ParseArguments(string[] args)
    {
        var forwardedArguments = new List<string>(args.Length);
        var compilerMode = PreviewCompilerMode.Avalonia;
        double? previewWidth = null;
        double? previewHeight = null;

        for (var index = 0; index < args.Length; index += 1)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--axsg-compiler-mode":
                    compilerMode = ParseCompilerMode(GetRequiredValue(args, ref index, argument));
                    break;
                case "--axsg-preview-width":
                    previewWidth = ParsePositiveDouble(GetRequiredValue(args, ref index, argument), argument);
                    break;
                case "--axsg-preview-height":
                    previewHeight = ParsePositiveDouble(GetRequiredValue(args, ref index, argument), argument);
                    break;
                default:
                    forwardedArguments.Add(argument);
                    break;
            }
        }

        return (
            new PreviewHostOptions(compilerMode, previewWidth, previewHeight),
            forwardedArguments.ToArray());
    }

    private static PreviewCompilerMode ParseCompilerMode(string value)
    {
        return string.Equals(value, "sourceGenerated", StringComparison.OrdinalIgnoreCase)
            ? PreviewCompilerMode.SourceGenerated
            : PreviewCompilerMode.Avalonia;
    }

    private static string GetRequiredValue(string[] args, ref int index, string argumentName)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException(argumentName + " requires a value.");
        }

        index += 1;
        return args[index];
    }

    private static double ParsePositiveDouble(string value, string argumentName)
    {
        if (!double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedValue) ||
            parsedValue <= 0)
        {
            throw new InvalidOperationException(argumentName + " must be a positive number.");
        }

        return parsedValue;
    }
}
