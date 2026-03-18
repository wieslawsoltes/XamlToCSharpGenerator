namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class Program
{
    public static void Main(string[] args)
    {
        var (options, forwardedArguments) = ParseArguments(args);
        PreviewHostRuntimeState.Configure(options);
        TryInitializePreviewHotDesignRuntime(options);
        SourceGeneratedRuntimeXamlLoaderInstaller.Install(
            options.CompilerMode,
            options.PreviewWidth,
            options.PreviewHeight);
        SourceGeneratedPreviewMarkupRuntimeInstaller.Install();
        global::Avalonia.DesignerSupport.Remote.RemoteDesignerEntryPoint.Main(forwardedArguments);
    }

    private static void TryInitializePreviewHotDesignRuntime(PreviewHostOptions options)
    {
        try
        {
            Type? installerType = Type.GetType(
                "XamlToCSharpGenerator.Runtime.AxsgPreviewHotDesignRuntimeInstaller, XamlToCSharpGenerator.Runtime.Avalonia",
                throwOnError: false);
            if (installerType is null)
            {
                Console.WriteLine("[AXSG preview] Preview Hot Design runtime install skipped: installer type is unavailable.");
                return;
            }

            System.Reflection.MethodInfo? initializeMethod = installerType.GetMethod(
                "Initialize",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                binder: null,
                [typeof(string), typeof(int?)],
                modifiers: null);
            if (initializeMethod is null)
            {
                Console.WriteLine("[AXSG preview] Preview Hot Design runtime install skipped: Initialize method is unavailable.");
                return;
            }

            initializeMethod.Invoke(null, [options.DesignHost, options.DesignPort]);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AXSG preview] Preview Hot Design runtime install skipped: " + ex.Message);
        }
    }

    private static (PreviewHostOptions Options, string[] ForwardedArguments) ParseArguments(string[] args)
    {
        var forwardedArguments = new List<string>(args.Length);
        var compilerMode = PreviewCompilerMode.Avalonia;
        double? previewWidth = null;
        double? previewHeight = null;
        string? sourceAssemblyPath = null;
        string? sourceFilePath = null;
        string? xamlFileProjectPath = null;
        string? designHost = null;
        int? designPort = null;

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
                case "--axsg-source-assembly":
                    sourceAssemblyPath = Path.GetFullPath(GetRequiredValue(args, ref index, argument));
                    break;
                case "--axsg-source-file":
                    sourceFilePath = Path.GetFullPath(GetRequiredValue(args, ref index, argument));
                    break;
                case "--axsg-xaml-project-path":
                    xamlFileProjectPath = GetRequiredValue(args, ref index, argument);
                    break;
                case "--axsg-design-host":
                    designHost = GetRequiredValue(args, ref index, argument);
                    break;
                case "--axsg-design-port":
                    designPort = ParsePositiveInt32(GetRequiredValue(args, ref index, argument), argument);
                    break;
                default:
                    forwardedArguments.Add(argument);
                    break;
            }
        }

        return (
            new PreviewHostOptions(
                compilerMode,
                previewWidth,
                previewHeight,
                sourceAssemblyPath,
                sourceFilePath,
                xamlFileProjectPath,
                designHost,
                designPort),
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

    private static int ParsePositiveInt32(string value, string argumentName)
    {
        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedValue) ||
            parsedValue <= 0)
        {
            throw new InvalidOperationException(argumentName + " must be a positive integer.");
        }

        return parsedValue;
    }
}
