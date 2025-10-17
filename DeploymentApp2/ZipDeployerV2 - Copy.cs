//using System.IO.Compression;
//using Microsoft.Web.Administration;

//public static class ZipDeployerV2
//{
//    /// <summary>
//    /// zipFolderPath: *.zip dosyalarının olduğu klasör (örn. content\admin içi)
//    /// siteName: hedef IIS site adı
//    /// appPoolName: kullanılacak AppPool
//    /// basePhysicalPath: site kökü (örn. D:\Sites\Humanist.Web)
//    /// appPrefix: zip isimlerine eklenecek path prefix (örn. "/admin" → "/admin/HXCORE")
//    /// </summary>
//    public static void DeployAllZips(string zipFolderPath, string siteName, string appPoolName, string basePhysicalPath, string appPrefix = "")
//    {
//        using var manager = new ServerManager();

//        // 1) AppPool ensure
//        var appPool = manager.ApplicationPools.FirstOrDefault(p => p.Name == appPoolName)
//                      ?? manager.ApplicationPools.Add(appPoolName);
//        appPool.ManagedRuntimeVersion = "v4.0";
//        appPool.ManagedPipelineMode = ManagedPipelineMode.Integrated;
//        appPool.AutoStart = true;

//        // 2) Site ensure
//        Directory.CreateDirectory(basePhysicalPath);
//        var site = manager.Sites.FirstOrDefault(s => s.Name == siteName);
//        if (site == null)
//        {
//            // host header site adına göre veriliyordu; istersen dışarıdan parametrele
//            site = manager.Sites.Add(siteName, "http", $"*:80:{siteName.ToLowerInvariant()}", basePhysicalPath);
//            site.ApplicationDefaults.ApplicationPoolName = appPoolName;
//            site.ServerAutoStart = true;
//        }
//        else
//        {
//            site.Applications["/"].VirtualDirectories["/"].PhysicalPath = basePhysicalPath;
//            site.ApplicationDefaults.ApplicationPoolName = appPoolName;
//        }

//        // 3) Her zip → staging’e çıkar → robocopy ile app dizinine MIR
//        foreach (var zipFile in Directory.GetFiles(zipFolderPath, "*.zip"))
//        {
//            var appName = Path.GetFileNameWithoutExtension(zipFile);
//            var appPath = CombineVirtualPath(appPrefix, "/" + appName);   // örn: "/admin/HXCORE"
//            var appPhysicalPath = Path.Combine(basePhysicalPath, appPrefix.Trim('/').Replace('/', '\\'), appName);
//            Directory.CreateDirectory(appPhysicalPath);

//            Console.WriteLine($"📦 Deploying '{zipFile}' → '{siteName}{appPath}'");

//            var app = site.Applications.FirstOrDefault(a => a.Path.Equals(appPath, StringComparison.OrdinalIgnoreCase));
//            if (app == null)
//            {
//                app = site.Applications.Add(appPath, appPhysicalPath);
//                app.ApplicationPoolName = appPoolName;
//            }
//            else
//            {
//                app.VirtualDirectories["/"].PhysicalPath = appPhysicalPath;
//                app.ApplicationPoolName = appPoolName;
//            }

//            // Staging: zip'i geçici klasöre aç
//            var staging = appPhysicalPath + ".__staging_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
//            Directory.CreateDirectory(staging);

//            ZipFile.ExtractToDirectory(zipFile, staging, overwriteFiles: true);

//            // Atomic-ish: robocopy /MIR staging -> physical
//            RobocopyMirror(staging, appPhysicalPath);

//            // Temizlik
//            TryDelete(staging);

//            Console.WriteLine($"✅ '{appName}' deployed to {appPhysicalPath}");
//        }

//        manager.CommitChanges();
//    }

//    private static void RobocopyMirror(string src, string dst)
//    {
//        Directory.CreateDirectory(dst);
//        var args = $"\"{src}\" \"{dst}\" /MIR /NFL /NDL /NJH /NJS /R:1 /W:1";
//        var psi = new System.Diagnostics.ProcessStartInfo("robocopy", args)
//        { CreateNoWindow = true, UseShellExecute = false };
//        var p = System.Diagnostics.Process.Start(psi)!; p.WaitForExit();
//    }

//    private static void TryDelete(string path)
//    {
//        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
//        catch { /* best effort */ }
//    }

//    private static string CombineVirtualPath(string prefix, string app)
//    {
//        prefix = prefix?.Trim() ?? "";
//        if (string.IsNullOrEmpty(prefix) || prefix == "/") return app;
//        return "/" + (prefix.Trim('/')) + app;
//    }
//}
