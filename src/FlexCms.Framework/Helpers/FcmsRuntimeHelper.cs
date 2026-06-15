using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Environment / runtime detection. Used by diagnostics endpoints, deployment
/// scripts, and any code that needs to branch on the host OS or build flavor.
/// </summary>
public static class FcmsRuntimeHelper
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux   => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS   => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsDebuggerAttached => Debugger.IsAttached;

    /// <summary>
    /// True when the entry assembly was compiled in DEBUG configuration.
    /// Use sparingly — prefer <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.IsDevelopment"/>
    /// for runtime decisions, this is for diagnostics only.
    /// </summary>
    public static bool IsDebugBuild => Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<DebuggableAttribute>()
        ?.IsJITTrackingEnabled == true;

    /// <summary>True when the entry assembly was compiled in RELEASE configuration.</summary>
    public static bool IsReleaseBuild => !IsDebugBuild;

    /// <summary>
    /// The .NET runtime description (e.g. <c>".NET 10.0.0"</c>). Convenient for
    /// admin "About" panels and exception reports.
    /// </summary>
    public static string FrameworkDescription => RuntimeInformation.FrameworkDescription;

    /// <summary>Operating system description (e.g. <c>"Microsoft Windows 11"</c>).</summary>
    public static string OsDescription => RuntimeInformation.OSDescription;

    /// <summary>Process architecture (X86 / X64 / Arm / Arm64).</summary>
    public static string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>
    /// Returns <c>"Windows"</c> / <c>"Linux"</c> / <c>"macOS"</c> / <c>"Other"</c>.
    /// </summary>
    public static string OsShortName
        => IsWindows ? "Windows"
         : IsLinux ? "Linux"
         : IsMacOS ? "macOS"
         : "Other";

    /// <summary>
    /// Returns the version of <paramref name="assembly"/> (defaults to the
    /// entry assembly). Falls back to <c>"unknown"</c> when the version can't
    /// be determined.
    /// </summary>
    public static string GetAssemblyVersion(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly();
        var name = assembly?.GetName();
        return name?.Version?.ToString() ?? "unknown";
    }
}
