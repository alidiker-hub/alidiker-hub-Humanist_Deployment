using System.IO.Compression;
using Microsoft.Web.Administration;

public static class ZipDeployer
{
    public static void DeployAllZips(string zipFolderPath, string siteName, string appPoolName, string basePhysicalPath)
    {
        using var manager = new ServerManager();

        string siteRootPath = Path.Combine(basePhysicalPath, siteName);
        Directory.CreateDirectory(siteRootPath);

        var site = manager.Sites.FirstOrDefault(s => s.Name == siteName);
        if (site == null)
        {
            site = manager.Sites.Add(siteName, "http", $"*:80:{siteName.ToLowerInvariant()}", siteRootPath);
            site.ApplicationDefaults.ApplicationPoolName = appPoolName;
        }

        var appPool = manager.ApplicationPools.FirstOrDefault(p => p.Name == appPoolName);
        if (appPool == null)
        {
            appPool = manager.ApplicationPools.Add(appPoolName);
            appPool.ManagedRuntimeVersion = "v4.0";
        }

        foreach (var zipFile in Directory.GetFiles(zipFolderPath, "*.zip"))
        {
            string appName = Path.GetFileNameWithoutExtension(zipFile);
            string appPath = "/" + appName;
            string appPhysicalPath = Path.Combine(siteRootPath, appName); // 🔄 site dizini altında oluştur

            Console.WriteLine($"📦 Deploying '{zipFile}' → '{siteName}{appPath}'");

            var app = site.Applications.FirstOrDefault(a => a.Path == appPath);
            if (app == null)
            {
                Directory.CreateDirectory(appPhysicalPath);
                app = site.Applications.Add(appPath, appPhysicalPath);
                app.ApplicationPoolName = appPoolName;
            }

            foreach (var file in Directory.GetFiles(appPhysicalPath, "*", SearchOption.AllDirectories))
                File.Delete(file);
            foreach (var dir in Directory.GetDirectories(appPhysicalPath, "*", SearchOption.AllDirectories))
                Directory.Delete(dir, true);

            ZipFile.ExtractToDirectory(zipFile, appPhysicalPath, overwriteFiles: true);

            Console.WriteLine($"✅ '{appName}' deployed to {appPhysicalPath}");
        }

        manager.CommitChanges();
    }

}
