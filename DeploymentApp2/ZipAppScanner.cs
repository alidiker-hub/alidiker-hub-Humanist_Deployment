using System;
using System.Collections.Generic;
using System.IO;

public static class ZipAppScanner
{
    public static Dictionary<string, string> GetApplicationsFromZipNames(string zipFolderPath)
    {
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var zipFile in Directory.GetFiles(zipFolderPath, "*.zip"))
        {
            string zipName = Path.GetFileNameWithoutExtension(zipFile);
            if (zipName.Equals("HUMANIST", StringComparison.OrdinalIgnoreCase))
                continue;

            apps[zipName] = zipName; // AppPool name = app name = zip name
        }

        return apps;
    }
}
