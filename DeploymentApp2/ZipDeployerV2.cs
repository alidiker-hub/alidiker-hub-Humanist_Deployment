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
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, true);
            Directory.CreateDirectory(versionDir);

            using var zip = ZipFile.OpenRead(ctx.ZipPath);
            ctx.Logger.LogInformation("📁 ZIP contains {EntryCount} entries", zip.Entries.Count);

            // Kökten mi, alt klasörden mi?
            var sub = ctx.Manifest.PhysicalPathInZip;
            if (string.IsNullOrWhiteSpace(sub) || sub == "." || sub == "/")
                await ExtractRootAsync(zip, versionDir, ctx, ct);
            else
                await ExtractFromSubAsync(zip, sub, versionDir, ctx, ct);

            ctx.ExtractedVersionDir = versionDir;

            var fileCount = Directory.GetFiles(versionDir, "*", SearchOption.AllDirectories).Length;
            var dirCount = Directory.GetDirectories(versionDir, "*", SearchOption.AllDirectories).Length;
            ctx.Logger.LogInformation("✅ Extraction completed: {Files} files, {Dirs} directories", fileCount, dirCount);
            ctx.Log(PlanActionType.DeployContentAtomicSwitch, $"extract -> {versionDir}");
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError(ex, "❌ ZIP extraction failed");
            if (_extractedVersionDir is not null && Directory.Exists(_extractedVersionDir))
            {
                ctx.Logger.LogWarning("⚠️ Keeping directory for rollback: {Dir}", _extractedVersionDir);
                ctx.ExtractedVersionDir = _extractedVersionDir;
            }
            throw;
        }
    }

    public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
    {
        if (ctx.DryRun || _extractedVersionDir is null) return;
        try
        {
            if (Directory.Exists(_extractedVersionDir))
            {
                Directory.Delete(_extractedVersionDir, true);
                ctx.Logger.LogInformation("🗑️ Rollback: Deleted {Dir}", _extractedVersionDir);
            }
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError(ex, "❌ Rollback failed: {Dir}", _extractedVersionDir);
        }
        await Task.CompletedTask;
    }

    // ---- helpers ----

    private static string StripPaketler(string path)
    {
        var p = path.Replace('/', '\\').TrimStart('\\');
        return p.StartsWith("paketler\\", StringComparison.OrdinalIgnoreCase) ? p.Substring("paketler\\".Length) : p;
    }

    private async Task ExtractRootAsync(ZipArchive zip, string target, DeployContext ctx, CancellationToken ct)
    {
        int extracted = 0, skipped = 0;

        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // klasör entry
            if (entry.FullName.EndsWith("/") || string.IsNullOrEmpty(entry.Name))
            {
                skipped++; continue;
            }

            var full = entry.FullName.Replace('/', '\\');

            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // ‘paketler\’ varsa at ve kalan alt yolu koru
                var rel = StripPaketler(full);
                var relDir = Path.GetDirectoryName(rel) ?? "";
                var zipName = Path.GetFileNameWithoutExtension(entry.Name);
                var innerTarget = Path.Combine(target, relDir, zipName);

                Directory.CreateDirectory(innerTarget);
                await ExtractInnerZipAsync(entry, innerTarget, ctx, ct);
                extracted++; continue;
            }

            // ZIP olmayan ve paketler\ altı → atla (paketler klasörü oluşmasın)
            if (full.StartsWith("paketler\\", StringComparison.OrdinalIgnoreCase))
            {
                skipped++; continue;
            }

            var dest = Path.Combine(target, full);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            extracted++;
        }

        ctx.Logger.LogInformation("✅ Root extraction: {Extracted} files (skipped: {Skipped})", extracted, skipped);
    }

    private async Task ExtractFromSubAsync(ZipArchive zip, string folderPath, string target, DeployContext ctx, CancellationToken ct)
    {
        var basePath = folderPath.Replace('/', '\\').Trim('\\', '/');
        var prefix = basePath + "\\";
        var altPrefix = basePath + "/";

        int extracted = 0, skipped = 0;

        foreach (var entry in zip.Entries.Where(e =>
                     e.FullName.Replace('/', '\\').StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                     e.FullName.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();

            var full = entry.FullName.Replace('/', '\\');
            var rel = full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(prefix.Length)
                : entry.FullName.Substring(altPrefix.Length).Replace('/', '\\');

            if (string.IsNullOrEmpty(rel) || string.IsNullOrEmpty(entry.Name))
                continue;

            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                rel = StripPaketler(rel);
                var relDir = Path.GetDirectoryName(rel) ?? "";
                var zipName = Path.GetFileNameWithoutExtension(entry.Name);
                var innerTarget = Path.Combine(target, relDir, zipName);

                Directory.CreateDirectory(innerTarget);
                await ExtractInnerZipAsync(entry, innerTarget, ctx, ct);
                extracted++; continue;
            }

            if (rel.StartsWith("paketler\\", StringComparison.OrdinalIgnoreCase))
            {
                skipped++; continue;
            }

            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            extracted++;
        }

        ctx.Logger.LogInformation("✅ Sub extraction: {Extracted} files from '{Folder}' (skipped: {Skipped})", extracted, folderPath, skipped);
    }

    private async Task ExtractInnerZipAsync(ZipArchiveEntry zipEntry, string targetDir, DeployContext ctx, CancellationToken ct)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            using (var ts = File.Open(tmp, FileMode.Create, FileAccess.Write))
            using (var es = zipEntry.Open())
                await es.CopyToAsync(ts, ct);

            using var inner = ZipFile.OpenRead(tmp);
            foreach (var e in inner.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(e.Name)) continue;

                var dest = Path.Combine(targetDir, e.FullName.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest, overwrite: true);
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        }
    }
}
