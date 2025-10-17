using Humanist.Deployer;
using System.IO.Compression;

public sealed class ExtractZipStep : IDeployStep
{
    public string Name => "ExtractZip";
    private string? _extractedVersionDir;

    public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
    {
        var versionDir = Path.Combine(ctx.BaseDeployDir, $"{ctx.Manifest.SiteName}_{DateTime.UtcNow:yyyyMMddHHmmss}");
        _extractedVersionDir = versionDir;

        ctx.Logger.LogInformation("📦 Extracting zip to: {VersionDir}", versionDir);

        if (ctx.DryRun)
        {
            ctx.ExtractedVersionDir = versionDir;
            ctx.Log(PlanActionType.DeployContentAtomicSwitch, $"(dry-run) extract -> {versionDir}");
            return;
        }

        try
        {
            if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true);
            Directory.CreateDirectory(versionDir);

            using var zip = ZipFile.OpenRead(ctx.ZipPath);
            ctx.Logger.LogInformation("📁 ZIP contains {EntryCount} entries", zip.Entries.Count);

            await AnalyzeZipStructure(zip, ctx);

            // 1) Root / Subpath extraction
            if (string.IsNullOrWhiteSpace(ctx.Manifest.PhysicalPathInZip) ||
                ctx.Manifest.PhysicalPathInZip == "." || ctx.Manifest.PhysicalPathInZip == "/")
            {
                await ExtractRootContentAsync(zip, versionDir, ctx, ct);
            }
            else
            {
                await ExtractSpecificFolderAsync(zip, ctx.Manifest.PhysicalPathInZip, versionDir, ctx, ct);
            }

            // 2) Child applications (mevcut davranış korunur)
            if (ctx.Manifest.Applications?.Count > 0)
            {
                foreach (var app in ctx.Manifest.Applications)
                    await ExtractApplicationAsync(zip, app, versionDir, ctx, ct);
            }

            // 3) URL Rewrite (opsiyonel)
            await ExtractUrlRewriteConfigAsync(zip, versionDir, ctx);

            ctx.ExtractedVersionDir = versionDir;

            var extractedFiles = Directory.GetFiles(versionDir, "*", SearchOption.AllDirectories).Length;
            var extractedDirs = Directory.GetDirectories(versionDir, "*", SearchOption.AllDirectories).Length;
            ctx.Logger.LogInformation("✅ Extraction completed: {FileCount} files, {DirCount} directories in {Directory}",
                extractedFiles, extractedDirs, versionDir);

            ctx.Log(PlanActionType.DeployContentAtomicSwitch, $"Extracted {extractedFiles} files to {versionDir}");
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError(ex, "❌ ZIP extraction failed");
            if (Directory.Exists(_extractedVersionDir))
            {
                ctx.Logger.LogWarning("⚠️ Keeping directory for rollback: {Directory}", _extractedVersionDir);
                ctx.ExtractedVersionDir = _extractedVersionDir;
            }
            throw;
        }
    }

    public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
    {
        var list = new List<string>();
        if (!ctx.DryRun && _extractedVersionDir is not null && Directory.Exists(_extractedVersionDir))
            list.Add(_extractedVersionDir);
        if (!ctx.DryRun && ctx.ExtractedVersionDir is not null &&
            Directory.Exists(ctx.ExtractedVersionDir) &&
            !list.Contains(ctx.ExtractedVersionDir))
            list.Add(ctx.ExtractedVersionDir);

        foreach (var dir in list)
        {
            try
            {
                ctx.Logger.LogInformation("🗑️ Rollback: Deleting extracted directory {Directory}", dir);
                Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                ctx.Logger.LogError(ex, "❌ Rollback failed: {Directory}", dir);
            }
        }
        await Task.CompletedTask;
    }

    private async Task AnalyzeZipStructure(ZipArchive zip, DeployContext ctx)
    {
        var entriesByFolder = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .GroupBy(e => Path.GetDirectoryName(e.FullName.Replace('/', '\\')) ?? "")
            .OrderBy(g => g.Key)
            .ToList();

        ctx.Logger.LogDebug("📊 ZIP Structure Analysis:");
        foreach (var group in entriesByFolder.Take(10))
        {
            ctx.Logger.LogDebug("   📁 {Folder}: {FileCount} files",
                string.IsNullOrEmpty(group.Key) ? "(root)" : group.Key, group.Count());
        }
        if (entriesByFolder.Count > 10)
            ctx.Logger.LogDebug("   ... and {More} more folders", entriesByFolder.Count - 10);
    }

    // ---- CORE FIX: 'paketler\' üst klasörünü oluşturmadan ZIP'leri alt yolunu koruyarak çıkar ----
    private static string StripPaketlerPrefix(string path)
    {
        var p = path.Replace('/', '\\').TrimStart('\\');
        return p.StartsWith("paketler\\", StringComparison.OrdinalIgnoreCase) ? p.Substring("paketler\\".Length) : p;
    }

    private async Task ExtractRootContentAsync(ZipArchive zip, string target, DeployContext ctx, CancellationToken ct)
    {
        int extractedCount = 0, skippedCount = 0;

        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // Klasör entry’leri atla
            if (entry.FullName.EndsWith("/") || string.IsNullOrEmpty(entry.Name))
            {
                skippedCount++;
                continue;
            }

            var fullPath = entry.FullName.Replace('/', '\\');

            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // 'paketler\' varsa strip et ve kalan alt yolu koru → <relDir>\<ZipName>
                var relative = StripPaketlerPrefix(fullPath);
                var relDir = Path.GetDirectoryName(relative) ?? string.Empty;
                var zipName = Path.GetFileNameWithoutExtension(entry.Name);
                var innerTarget = Path.Combine(target, relDir, zipName);

                if (!Directory.Exists(innerTarget))
                    Directory.CreateDirectory(innerTarget);

                await ExtractInnerZipAsync(entry, innerTarget, ctx, ct);
                extractedCount++;
                continue;
            }

            // ZIP olmayan ve 'paketler\' altında ise atla → 'paketler' klasörü hiç oluşmasın
            if (fullPath.StartsWith("paketler\\", StringComparison.OrdinalIgnoreCase))
            {
                skippedCount++;
                continue;
            }

            // Normal dosya: tam yolu ile çıkar
            var destPath = Path.Combine(target, fullPath);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);
            extractedCount++;
        }

        ctx.Logger.LogInformation("✅ Root extraction: {Extracted} files, {Skipped} skipped", extractedCount, skippedCount);
    }

    private async Task ExtractSpecificFolderAsync(ZipArchive zip, string folderPath, string target, DeployContext ctx, CancellationToken ct)
    {
        var normalizedFolder = folderPath.Replace('/', '\\').Trim('\\', '/');
        var prefix = normalizedFolder + "\\";
        var altPrefix = normalizedFolder + "/";

        var entries = zip.Entries.Where(e =>
            e.FullName.Replace('/', '\\').StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int extractedCount = 0, skipped = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = entry.FullName.Replace('/', '\\');
            string relative =
                fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? fullPath.Substring(prefix.Length)
                    : entry.FullName.Substring(altPrefix.Length).Replace('/', '\\');

            if (string.IsNullOrEmpty(relative) || string.IsNullOrEmpty(entry.Name))
                continue;

            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // subpath altındaki zip → aynı mantık: paketler prefix varsa strip, sonra <relDir>\<ZipName>
                relative = StripPaketlerPrefix(relative);
                var relDir = Path.GetDirectoryName(relative) ?? string.Empty;
                var zipName = Path.GetFileNameWithoutExtension(entry.Name);
                var innerTarget = Path.Combine(target, relDir, zipName);

                if (!Directory.Exists(innerTarget))
                    Directory.CreateDirectory(innerTarget);

                await ExtractInnerZipAsync(entry, innerTarget, ctx, ct);
                extractedCount++;
            }
            else
            {
                // ZIP olmayan ve 'paketler\' altında kalacaksa atla
                if (relative.StartsWith("paketler\\", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                var destFile = Path.Combine(target, relative);
                var destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destFile, overwrite: true);
                extractedCount++;
            }
        }

        ctx.Logger.LogInformation("✅ Folder extraction: {Extracted} files, {Skipped} skipped from '{Folder}'",
            extractedCount, skipped, folderPath);
    }

    private async Task ExtractApplicationAsync(ZipArchive zip, ApplicationDef app, string baseDir, DeployContext ctx, CancellationToken ct)
    {
        var appTarget = PathUtil.CombineUnder(baseDir, app.Path);
        var appSourcePath = app.PhysicalPathInZip?.Replace('/', '\\').Trim('\\', '/') ?? string.Empty;

        if (Directory.Exists(appTarget) && Directory.EnumerateFileSystemEntries(appTarget).Any())
        {
            ctx.Logger.LogInformation("↪️ Skipping app '{AppPath}' (already materialized).", app.Path);
            return;
        }

        if (string.IsNullOrWhiteSpace(appSourcePath))
        {
            ctx.Logger.LogWarning("⚠️ No physical path specified for application: {AppPath}", app.Path);
            return;
        }

        if (!Directory.Exists(appTarget))
            Directory.CreateDirectory(appTarget);

        await ExtractSpecificFolderAsync(zip, appSourcePath, appTarget, ctx, ct);
    }

    private async Task ExtractUrlRewriteConfigAsync(ZipArchive zip, string target, DeployContext ctx)
    {
        if (ctx.Manifest.Features?.UrlRewriteImport is string rewriteRel && !string.IsNullOrWhiteSpace(rewriteRel))
        {
            var normalizedRewritePath = rewriteRel.Replace('/', '\\').TrimStart('\\');
            var entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals(normalizedRewritePath, StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Replace('/', '\\').Equals(normalizedRewritePath, StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
            {
                var dest = Path.Combine(target, normalizedRewritePath);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(dest, overwrite: true);
                ctx.Logger.LogInformation("✅ Extracted URL Rewrite config: {ConfigPath}", normalizedRewritePath);
            }
            else
            {
                ctx.Logger.LogWarning("⚠️ URL Rewrite config not found: {ConfigPath}", normalizedRewritePath);
            }
        }
    }

    private async Task ExtractInnerZipAsync(ZipArchiveEntry zipEntry, string targetDir, DeployContext ctx, CancellationToken ct)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            using (var tempStream = File.Open(tempFile, FileMode.Create, FileAccess.Write))
            using (var entryStream = zipEntry.Open())
                await entryStream.CopyToAsync(tempStream, ct);

            using var innerZip = ZipFile.OpenRead(tempFile);

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            foreach (var innerEntry in innerZip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(innerEntry.Name)) continue;

                var innerDest = Path.Combine(targetDir, innerEntry.FullName.Replace('/', '\\'));
                var innerDestDir = Path.GetDirectoryName(innerDest);
                if (!string.IsNullOrEmpty(innerDestDir) && !Directory.Exists(innerDestDir))
                    Directory.CreateDirectory(innerDestDir);

                innerEntry.ExtractToFile(innerDest, overwrite: true);
            }
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* ignore */ }
        }
    }
}
