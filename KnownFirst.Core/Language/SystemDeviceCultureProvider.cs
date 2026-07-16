using System.Globalization;

namespace KnownFirst.Core.Language;

public sealed class SystemDeviceCultureProvider : IDeviceCultureProvider
{
    public string GetDeviceCultureName() => CultureInfo.InstalledUICulture.Name;
}
