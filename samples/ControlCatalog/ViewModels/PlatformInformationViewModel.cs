using System.Runtime.InteropServices;
using MiniMvvm;

namespace ControlCatalog.ViewModels;

public class PlatformInformationViewModel : ViewModelBase
{
    public PlatformInformationViewModel()
    {
        /*  NOTE:
        *   ------------
        *   The below API is not meant to be used in production Apps. 
        *   If you need to consume this info, please use:
        *      - OperatingSystem ( https://learn.microsoft.com/en-us/dotnet/api/system.operatingsystem | if .NET 5 or greater)
        *      - or RuntimeInformation ( https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.runtimeinformation )
        */
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER")))
        {
            PlatformInfo = "Platform: Browser";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID")) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS")))
        {
            PlatformInfo = "Platform: Mobile (native)";
        }
        else
        {
            PlatformInfo = "Platform: Desktop (native)";
        }
    }
    
    public string? PlatformInfo { get; }
}
