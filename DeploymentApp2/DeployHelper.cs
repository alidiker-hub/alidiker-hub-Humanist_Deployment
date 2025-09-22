using Microsoft.Web.Administration;
using System.Diagnostics;
using System.IO.Compression;

public static class DeployHelper
{
    public static void PublishProject(string projectPath, string outputPath, string? buildArgs = null)
    {
        Directory.CreateDirectory(outputPath);
        string args = $"publish \"{projectPath}\" -o \"{outputPath}\" {buildArgs ?? "-c Release"}";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new Exception("Yayınlama işlemi başarısız oldu.");
    }

    public static void CreateOrUpdateIISApp(string siteName, string appPath, string physicalPath, string appPoolName)
    {
        using var manager = new ServerManager();
        var site = manager.Sites.FirstOrDefault(s => s.Name == siteName);

        if (site == null)
        {
            string defaultPath = Path.Combine("C:\\inetpub\\wwwroot", siteName.Replace(" ", "_"));
            if (!Directory.Exists(defaultPath))
                Directory.CreateDirectory(defaultPath);

            site = manager.Sites.Add(siteName, "http", $"*:80:{siteName.ToLowerInvariant()}", defaultPath);
            site.ApplicationDefaults.ApplicationPoolName = appPoolName;
        }

        var appPool = manager.ApplicationPools.FirstOrDefault(p => p.Name == appPoolName);
        if (appPool == null)
        {
            appPool = manager.ApplicationPools.Add(appPoolName);
            appPool.ManagedRuntimeVersion = "v4.0";
        }

        var app = site.Applications.FirstOrDefault(a => a.Path == appPath);
        if (app == null)
        {
            if (!Directory.Exists(physicalPath))
                Directory.CreateDirectory(physicalPath);

            app = site.Applications.Add(appPath, physicalPath);
            app.ApplicationPoolName = appPoolName;
        }
        else
        {
            app.VirtualDirectories["/"].PhysicalPath = physicalPath;
            app.ApplicationPoolName = appPoolName;
        }

        manager.CommitChanges();
    }

    public static void PutAppOffline(string appPath, string siteName)
    {
        string path = GetPhysicalPath(appPath, siteName);
        File.WriteAllText(Path.Combine(path, "app_offline.htm"), "Deploy işlemi yapılıyor...");
    }

    public static void RemoveAppOffline(string appPath, string siteName)
    {
        string path = GetPhysicalPath(appPath, siteName);
        var offlinePath = Path.Combine(path, "app_offline.htm");
        if (File.Exists(offlinePath))
            File.Delete(offlinePath);
    }

    private static string GetPhysicalPath(string appPath, string siteName)
    {
        using var manager = new ServerManager();
        var site = manager.Sites.FirstOrDefault(s => s.Name == siteName)
                   ?? throw new Exception($"Site '{siteName}' bulunamadı.");

        var app = site.Applications.FirstOrDefault(a => a.Path == appPath)
                  ?? throw new Exception($"Uygulama '{appPath}' bulunamadı.");

        return app.VirtualDirectories["/"].PhysicalPath;
    }




    //    ExtractSelectedFilesFromZip("MyWebApp.zip", "C:\\deploy\\MyApp", entry =>
    //    entry.FullName.EndsWith(".dll") || entry.FullName.StartsWith("wwwroot/")
    //);


    public static void ExtractSelectedFilesFromZip(string zipPath, string targetFolder, Func<ZipArchiveEntry, bool> filter)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (!filter(entry))
                continue;

            string destinationPath = Path.Combine(targetFolder, entry.FullName);


            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }
    }



}
