using System;
using System.IO;

public static class DeploymentPipeline
{
    public static void ExecuteFullDeployment(string zipFolderPath, string basePath, string siteName, string rootAppPool)
    {
        Console.WriteLine("🚀 1. Zip dosyaları çıkarılıyor...");
        //HumanistZipDeployer.Deploy(zipFolderPath, basePath);

        string sitePath = Path.Combine(basePath, "HUMANIST");

        Console.WriteLine("🔍 2. Zip isimlerinden app listesi çıkarılıyor...");
        var apps = ZipAppScanner.GetApplicationsFromZipNames(zipFolderPath);

        Console.WriteLine("🌐 3. IIS Site ve Application'lar kuruluyor...");
        IISApplicationSetup.CreateSiteAndApplications(siteName, sitePath, rootAppPool, apps);

        Console.WriteLine("✅ Tamamlandı!");
    }
}
