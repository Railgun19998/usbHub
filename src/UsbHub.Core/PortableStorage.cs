namespace UsbHub.Core;

public static class PortableStorage
{
    public static string Prepare(string programDirectory, string legacyDirectory, string? overrideDirectory = null)
    {
        var destination = Path.GetFullPath(overrideDirectory ?? programDirectory);
        Directory.CreateDirectory(destination);
        if (overrideDirectory is null && !string.Equals(destination.TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(legacyDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            foreach (var name in new[] { "settings.json", "scan-state.json" })
            {
                var source = Path.Combine(legacyDirectory, name);
                var target = Path.Combine(destination, name);
                if (!File.Exists(source) || File.Exists(target)) continue;
                try { File.Copy(source, target, false); }
                catch (IOException) when (File.Exists(target)) { }
            }
        }
        var settings = Path.Combine(destination, "settings.json");
        if (!File.Exists(settings)) new AppSettings().Save(settings);
        return destination;
    }
}
