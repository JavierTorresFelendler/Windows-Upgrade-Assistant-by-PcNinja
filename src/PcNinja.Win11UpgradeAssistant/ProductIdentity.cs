using System.Reflection;
using PcNinja.Win11Upgrade.Core;

namespace PcNinja.Win11UpgradeAssistant;

internal static class ProductIdentity
{
    internal const string Name = AppUpdateIdentity.Product;
    internal const string ExecutableName = AppUpdateIdentity.AssetName;
    internal static string BuildVersion => typeof(ProductIdentity).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    internal static string Version => BuildVersion.Split('+')[0];
}
