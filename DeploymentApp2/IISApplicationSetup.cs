using Microsoft.Web.Administration;

public static class IISApplicationSetup
{
    public static void CreateSiteAndApplications(string siteName, string sitePhysicalPath, string rootAppPoolName, Dictionary<string, string> apps)
    {
        using var manager = new ServerManager();

        // 1. Site oluştur
        var site = manager.Sites.FirstOrDefault(s => s.Name == siteName);
        if (site == null)
        {
            site = manager.Sites.Add(siteName, "http", $"*:80:{siteName.ToLowerInvariant()}", sitePhysicalPath);
            site.ApplicationDefaults.ApplicationPoolName = rootAppPoolName;
        }

        // 2. Root app pool kontrol
        var rootPool = manager.ApplicationPools.FirstOrDefault(p => p.Name == rootAppPoolName)
                       ?? manager.ApplicationPools.Add(rootAppPoolName);
        rootPool.ManagedRuntimeVersion = "v4.5";

        // 3. Uygulama listesi (path => appPoolName)
        foreach (var kv in apps)
        {
            string appPath = "/" + kv.Key.Trim('/');
            string appPhysicalPath = Path.Combine(sitePhysicalPath, "..", kv.Key); // root ile aynı seviyede olacak
            string appPool = kv.Value;

            Directory.CreateDirectory(appPhysicalPath);

            var pool = manager.ApplicationPools.FirstOrDefault(p => p.Name == appPool)
                       ?? manager.ApplicationPools.Add(appPool);
            pool.ManagedRuntimeVersion = "v4.5";

            var app = site.Applications.FirstOrDefault(a => a.Path == appPath);
            if (app == null)
            {
                app = site.Applications.Add(appPath, appPhysicalPath);
                app.ApplicationPoolName = appPool;
            }
        }

        manager.CommitChanges();
    }
}
