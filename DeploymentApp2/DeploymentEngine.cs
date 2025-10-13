// DeploymentEngine.cs
// .NET 9 / Blazor ServerInteractive / Microsoft.Web.Administration
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Web.Administration;

namespace Contoso.Deployer
{
    #region Manifest Models
    public sealed class SiteManifest
    {
        [JsonPropertyName("siteName")] public string SiteName { get; set; } = default!;
        [JsonPropertyName("physicalPath")] public string PhysicalPathInZip { get; set; } = "content\\web";
        [JsonPropertyName("bindings")] public List<BindingDef> Bindings { get; set; } = new();
        [JsonPropertyName("appPool")] public AppPoolDef AppPool { get; set; } = new();
        [JsonPropertyName("applications")] public List<ApplicationDef> Applications { get; set; } = new();
        [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }
        [JsonPropertyName("acl")] public List<AclDef>? Acls { get; set; }
        [JsonPropertyName("features")] public FeaturesDef? Features { get; set; }
        [JsonPropertyName("blueGreen")] public BlueGreenDef? BlueGreen { get; set; }

        public static SiteManifest FromJson(string json)
            => JsonSerializer.Deserialize<SiteManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public sealed class BindingDef
    {
        [JsonPropertyName("protocol")] public string Protocol { get; set; } = "http"; // http | https
        [JsonPropertyName("ip")] public string Ip { get; set; } = "*";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("certThumbprint")] public string? CertThumbprint { get; set; }
        [JsonPropertyName("certStore")] public string CertStore { get; set; } = "My";
    }

    public sealed class AppPoolDef
    {
        [JsonPropertyName("name")] public string Name { get; set; } = default!;
        [JsonPropertyName("runtimeVersion")] public string RuntimeVersion { get; set; } = "v4.0";
        [JsonPropertyName("pipelineMode")] public string PipelineMode { get; set; } = "Integrated"; // Classic | Integrated
        [JsonPropertyName("identity")] public AppPoolIdentityDef Identity { get; set; } = new();
        [JsonPropertyName("recycle")] public AppPoolRecycleDef Recycle { get; set; } = new();
        [JsonPropertyName("enable32Bit")] public bool Enable32Bit { get; set; } = false;
        [JsonPropertyName("autoStart")] public bool AutoStart { get; set; } = true;
        [JsonPropertyName("alwaysRunning")] public bool AlwaysRunning { get; set; } = false;
    }

    public sealed class AppPoolIdentityDef
    {
        // ApplicationPoolIdentity | LocalSystem | LocalService | NetworkService | SpecificUser
        [JsonPropertyName("type")] public string Type { get; set; } = "ApplicationPoolIdentity";
        [JsonPropertyName("user")] public string? User { get; set; }
        [JsonPropertyName("password")] public string? Password { get; set; }
    }

    public sealed class AppPoolRecycleDef
    {
        [JsonPropertyName("time")] public string Time { get; set; } = "00:00:00"; // 0 => off
        [JsonPropertyName("privateMemoryMB")] public int PrivateMemoryMB { get; set; } = 0;
    }

    public sealed class ApplicationDef
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "/api";
        [JsonPropertyName("physicalPath")] public string PhysicalPathInZip { get; set; } = "content\\api";
    }

    public sealed class AclDef
    {
        [JsonPropertyName("path")] public string Path { get; set; } = default!;
        [JsonPropertyName("identity")] public string Identity { get; set; } = "IIS_IUSRS";
        [JsonPropertyName("rights")] public string Rights { get; set; } = "M"; // F | M | R
    }

    public sealed class FeaturesDef
    {
        [JsonPropertyName("urlRewriteImport")] public string? UrlRewriteImport { get; set; }
        [JsonPropertyName("requestFiltering")] public RequestFilteringDef? RequestFiltering { get; set; }
        [JsonPropertyName("compression")] public CompressionDef? Compression { get; set; }
    }

    public sealed class RequestFilteringDef
    {
        [JsonPropertyName("maxAllowedContentLength")] public long? MaxAllowedContentLength { get; set; }
    }

    public sealed class CompressionDef
    {
        [JsonPropertyName("static")] public bool Static { get; set; } = true;
        [JsonPropertyName("dynamic")] public bool Dynamic { get; set; } = true;
    }

    // 🔵🟢 Blue/Green – genişletilmiş
    public sealed class BlueGreenDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;

        // Slot adları/sonekleri
        [JsonPropertyName("activeSuffix")] public string ActiveSuffix { get; set; } = "_A";
        [JsonPropertyName("slotSuffix")] public string SlotSuffix { get; set; } = "_B"; // passive

        // Health-check
        [JsonPropertyName("healthUrls")] public List<string>? HealthUrls { get; set; }
        [JsonPropertyName("healthTimeoutSeconds")] public int HealthTimeoutSeconds { get; set; } = 15;
        [JsonPropertyName("healthAttempts")] public int HealthAttempts { get; set; } = 10;

        // Swap davranışı
        // Varsayılan: önce https, sonra http
        [JsonPropertyName("swapProtocolOrder")] public List<string> SwapProtocolOrder { get; set; } = new() { "https", "http" };
        [JsonPropertyName("stopActiveBeforeSwap")] public bool StopActiveBeforeSwap { get; set; } = false;
        // Rollback için aktifteki bindingleri silmeden kopyalamak istersen (geriye dönmesi kolaylaşır)
        [JsonPropertyName("preserveOldBindingsForRollback")] public bool PreserveOldBindingsForRollback { get; set; } = false;
    }
    #endregion

    #region Plan & Context
    public enum PlanActionType
    {
        Noop, Backup,
        EnsureAppPool, EnsureSite, EnsureApplications, EnsureBindings,
        SetEnv, ApplyAcl, ConfigureFeatures,
        DeployContentAtomicSwitch, DeployBlueGreenMirror,
        Recycle, StartSite, HealthCheck, SwapBindings
    }
    public sealed record PlanAction(PlanActionType Type, string Description);

    public sealed class DeployContext
    {
        public string ZipPath { get; init; } = default!;
        public SiteManifest Manifest { get; init; } = default!;
        public string BaseDeployDir { get; init; } = default!;
        public bool DryRun { get; init; }
        public ILogger Logger { get; init; } = default!;

        public string? ExtractedVersionDir { get; set; }   // D:\Sites\Contoso_yyyyMMddHHmmss
        public string SitePhysicalPath { get; set; } = ""; // atomic: = ExtractedVersionDir; blue/green: slot kökü
        public bool UseBlueGreen => Manifest.BlueGreen?.Enabled == true;

        public readonly List<PlanAction> Executed = new();
        public string IisRoot => Environment.GetFolderPath(Environment.SpecialFolder.System).Replace("\\System32", "") + "\\inetpub";

        public void Log(PlanActionType t, string msg)
        {
            Logger.LogInformation("[{Type}] {Msg}", t, msg);
            Executed.Add(new PlanAction(t, msg));
        }
    }
    #endregion

    #region Atomic Pipeline infra
    public interface IDeployStep
    {
        string Name { get; }
        Task ExecuteAsync(DeployContext ctx, CancellationToken ct);
        Task RollbackAsync(DeployContext ctx, CancellationToken ct);
    }

    public sealed class AtomicScopeStep : IDeployStep
    {
        public string Name { get; }
        private readonly IReadOnlyList<IDeployStep> _inner;
        private readonly Stack<IDeployStep> _executed = new();

        public AtomicScopeStep(string scopeName, IEnumerable<IDeployStep> inner)
        {
            Name = $"Scope:{scopeName}";
            _inner = inner.ToList();
        }

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Log(PlanActionType.Noop, $"-- Begin {Name}");
            try
            {
                foreach (var s in _inner)
                {
                    await s.ExecuteAsync(ctx, ct);
                    _executed.Push(s);
                }
                ctx.Log(PlanActionType.Noop, $"-- Commit {Name}");
            }
            catch
            {
                ctx.Log(PlanActionType.Noop, $"-- Rollback {Name}");
                while (_executed.Count > 0)
                {
                    var s = _executed.Pop();
                    try { await s.RollbackAsync(ctx, ct); } catch { }
                }
                throw;
            }
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            while (_executed.Count > 0)
            {
                var s = _executed.Pop();
                try { await s.RollbackAsync(ctx, ct); } catch { }
            }
            await Task.CompletedTask;
        }
    }

    public sealed class PipelineEngine
    {
        private readonly ILogger _logger;
        public PipelineEngine(ILogger logger) => _logger = logger;

        public async Task RunAsync(IEnumerable<IDeployStep> steps, DeployContext ctx, CancellationToken ct)
        {
            var executed = new Stack<IDeployStep>();
            try
            {
                foreach (var s in steps)
                {
                    _logger.LogInformation("▶ {Step}", s.Name);
                    await s.ExecuteAsync(ctx, ct);
                    executed.Push(s);
                }
                _logger.LogInformation("✅ Deployment pipeline completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Pipeline failed – starting rollback");
                while (executed.Count > 0)
                {
                    var s = executed.Pop();
                    try
                    {
                        _logger.LogWarning("↩ Rollback {Step}", s.Name);
                        await s.RollbackAsync(ctx, ct);
                    }
                    catch (Exception rex)
                    {
                        _logger.LogError(rex, "Rollback error at {Step}", s.Name);
                    }
                }
                throw;
            }
        }
    }
    #endregion

    #region Steps
    // 0) ZIP -> versiyon klasörü (atomic switch için)
    public sealed class ExtractZipStep : IDeployStep
    {
        public string Name => "ExtractZip";
        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var versionDir = Path.Combine(ctx.BaseDeployDir, $"{ctx.Manifest.SiteName}_{DateTime.UtcNow:yyyyMMddHHmmss}");
            if (ctx.DryRun)
            {
                ctx.ExtractedVersionDir = versionDir;
                ctx.Log(PlanActionType.DeployContentAtomicSwitch, $"(dry-run) extract -> {versionDir}");
                return;
            }

            Directory.CreateDirectory(versionDir);
            using var zip = ZipFile.OpenRead(ctx.ZipPath);

            ExtractFolder(zip, ctx.Manifest.PhysicalPathInZip, versionDir);

            foreach (var app in ctx.Manifest.Applications)
            {
                var sub = Path.Combine(versionDir, SanitizeApp(app.Path));
                ExtractFolder(zip, app.PhysicalPathInZip, sub);
            }

            // Opsiyonel: features.urlRewriteImport dosyasını da versiyon klasörüne çıkar
            if (ctx.Manifest.Features?.UrlRewriteImport is string rewriteRel)
            {
                var norm = rewriteRel.Replace('/', '\\').TrimStart('\\');
                var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals(norm, StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    var dest = Path.Combine(versionDir, norm);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }

            ctx.ExtractedVersionDir = versionDir;
            ctx.Log(PlanActionType.DeployContentAtomicSwitch, $"extracted -> {versionDir}");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            if (!ctx.DryRun && ctx.ExtractedVersionDir is not null && Directory.Exists(ctx.ExtractedVersionDir))
            {
                try { Directory.Delete(ctx.ExtractedVersionDir, true); } catch { }
            }
            await Task.CompletedTask;
        }

        private static void ExtractFolder(ZipArchive zip, string subPathInZip, string target)
        {
            var prefix = subPathInZip.TrimEnd('\\') + "\\";
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var rel = e.FullName.Substring(prefix.Length);
                var dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (!string.IsNullOrEmpty(e.Name)) e.ExtractToFile(dest, overwrite: true);
            }
        }
        private static string SanitizeApp(string p) => p.Trim('/').Replace('/', '_').Replace('\\', '_');
    }

    // 1) AppPool upsert
    public sealed class EnsureAppPoolStep : IDeployStep
    {
        public string Name => "EnsureAppPool";
        private (string runtime, string pipeline, bool enable32, ProcessModelIdentityType idType, string? user, string? pwd, bool auto, StartMode startMode)? _old;

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            using var sm = new ServerManager();
            var def = ctx.Manifest.AppPool;
            var pool = sm.ApplicationPools.FirstOrDefault(p => p.Name == def.Name) ?? sm.ApplicationPools.Add(def.Name);

            _old = (pool.ManagedRuntimeVersion, pool.ManagedPipelineMode.ToString(), pool.Enable32BitAppOnWin64,
                    pool.ProcessModel.IdentityType, pool.ProcessModel.UserName, pool.ProcessModel.Password,
                    pool.AutoStart, pool.StartMode);

            if (!ctx.DryRun)
            {
                pool.ManagedRuntimeVersion = def.RuntimeVersion;
                pool.ManagedPipelineMode = def.PipelineMode.Equals("Classic", StringComparison.OrdinalIgnoreCase)
                    ? ManagedPipelineMode.Classic : ManagedPipelineMode.Integrated;
                pool.Enable32BitAppOnWin64 = def.Enable32Bit;

                pool.AutoStart = def.AutoStart;
                pool.StartMode = def.AlwaysRunning ? StartMode.AlwaysRunning : StartMode.OnDemand;

                switch (def.Identity.Type)
                {
                    case "LocalSystem": pool.ProcessModel.IdentityType = ProcessModelIdentityType.LocalSystem; break;
                    case "LocalService": pool.ProcessModel.IdentityType = ProcessModelIdentityType.LocalService; break;
                    case "NetworkService": pool.ProcessModel.IdentityType = ProcessModelIdentityType.NetworkService; break;
                    case "SpecificUser":
                        pool.ProcessModel.IdentityType = ProcessModelIdentityType.SpecificUser;
                        pool.ProcessModel.UserName = def.Identity.User ?? "";
                        pool.ProcessModel.Password = def.Identity.Password ?? "";
                        break;
                    default: pool.ProcessModel.IdentityType = ProcessModelIdentityType.ApplicationPoolIdentity; break;
                }

                if (TimeSpan.TryParse(def.Recycle.Time, out var t) && t > TimeSpan.Zero)
                    pool.Recycling.PeriodicRestart.Time = t;
                if (def.Recycle.PrivateMemoryMB > 0)
                    pool.Recycling.PeriodicRestart.PrivateMemory = def.Recycle.PrivateMemoryMB * 1024;

                sm.CommitChanges();
            }
            ctx.Log(PlanActionType.EnsureAppPool, $"{def.Name} ensured");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            if (ctx.DryRun || _old is null) return;
            using var sm = new ServerManager();
            var pool = sm.ApplicationPools.FirstOrDefault(p => p.Name == ctx.Manifest.AppPool.Name);
            if (pool is null) return;

            var o = _old.Value;
            pool.ManagedRuntimeVersion = o.runtime;
            pool.ManagedPipelineMode = o.pipeline.Equals("Classic", StringComparison.OrdinalIgnoreCase)
                ? ManagedPipelineMode.Classic : ManagedPipelineMode.Integrated;
            pool.Enable32BitAppOnWin64 = o.enable32;
            pool.ProcessModel.IdentityType = o.idType;
            pool.ProcessModel.UserName = o.user ?? pool.ProcessModel.UserName;
            pool.ProcessModel.Password = o.pwd ?? pool.ProcessModel.Password;
            pool.AutoStart = o.auto;
            pool.StartMode = o.startMode;
            sm.CommitChanges();

            await Task.CompletedTask;
        }
    }

    // 2) Site + Applications (atomic/blue-green)
    public sealed class EnsureSiteAndAppsStep : IDeployStep
    {
        public string Name => "EnsureSiteAndApps";
        private string? _oldRootPhys;

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            using var sm = new ServerManager();
            var (siteName, targetPhys) = ResolveTargets(ctx, sm); // BG ayarlarını dikkate alır
            if (!ctx.DryRun) Directory.CreateDirectory(targetPhys);

            var site = sm.Sites.FirstOrDefault(s => s.Name == siteName);
            if (site is null)
            {
                site = sm.Sites.Add(siteName, "http", "*:80:", targetPhys);
                site.ServerAutoStart = true;
                site.Applications["/"].ApplicationPoolName = ctx.Manifest.AppPool.Name;
            }
            else
            {
                _oldRootPhys = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (!ctx.UseBlueGreen) // atomic switch: root'u versiyon klasörüne çevir
                    site.Applications["/"].VirtualDirectories["/"].PhysicalPath = targetPhys;
                site.Applications["/"].ApplicationPoolName = ctx.Manifest.AppPool.Name;
            }

            // Child applications
            foreach (var app in ctx.Manifest.Applications)
            {
                var sub = app.Path;
                var subPhys = ctx.UseBlueGreen
                    ? Path.Combine(targetPhys, SanitizeApp(sub))
                    : Path.Combine(ctx.ExtractedVersionDir!, SanitizeApp(sub));
                if (!ctx.DryRun) Directory.CreateDirectory(subPhys);

                var ex = site.Applications.FirstOrDefault(a => a.Path.Equals(sub, StringComparison.OrdinalIgnoreCase));
                if (ex is null) ex = site.Applications.Add(sub, subPhys);
                ex.VirtualDirectories["/"].PhysicalPath = subPhys;
                ex.ApplicationPoolName = ctx.Manifest.AppPool.Name;
            }

            if (!ctx.DryRun) sm.CommitChanges();

            ctx.SitePhysicalPath = targetPhys;
            ctx.Log(PlanActionType.EnsureSite, $"{siteName} -> {targetPhys}");
            ctx.Log(PlanActionType.EnsureApplications, $"{ctx.Manifest.Applications.Count} application(s) ensured");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            if (ctx.DryRun || ctx.UseBlueGreen || _oldRootPhys is null) return;
            using var sm = new ServerManager();
            var site = sm.Sites.FirstOrDefault(s => s.Name == ctx.Manifest.SiteName);
            if (site is null) return;
            site.Applications["/"].VirtualDirectories["/"].PhysicalPath = _oldRootPhys;
            sm.CommitChanges();
            await Task.CompletedTask;
        }

        private static (string siteName, string phys) ResolveTargets(DeployContext ctx, ServerManager sm)
        {
            if (!ctx.UseBlueGreen) return (ctx.Manifest.SiteName, ctx.ExtractedVersionDir!);

            var bg = ctx.Manifest.BlueGreen!;
            var activeSuffix = bg.ActiveSuffix;
            var passiveSuffix = bg.SlotSuffix;

            var aName = ctx.Manifest.SiteName + activeSuffix;   // aktif
            var bName = ctx.Manifest.SiteName + passiveSuffix;  // pasif

            var exA = sm.Sites.FirstOrDefault(s => s.Name == aName);
            var exB = sm.Sites.FirstOrDefault(s => s.Name == bName);

            string pick = (exA, exB) switch
            {
                (null, null) => bName, // ilk kurulum B
                (null, _) => aName,
                (_, null) => bName,
                _ => exA!.Bindings.Count <= exB!.Bindings.Count ? aName : bName
            };
            var phys = Path.Combine(ctx.BaseDeployDir, pick);
            return (pick, phys);
        }

        private static string SanitizeApp(string p) => p.Trim('/').Replace('/', '_').Replace('\\', '_');
    }

    // 3) Blue/Green mirror (pasif slota dosya kopyalama)
    public sealed class BlueGreenMirrorStep : IDeployStep
    {
        public string Name => "BlueGreenMirror";
        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            if (!ctx.UseBlueGreen) { ctx.Log(PlanActionType.Noop, "blue/green disabled"); return; }
            if (ctx.DryRun) { ctx.Log(PlanActionType.DeployBlueGreenMirror, $"(dry-run) mirror {ctx.ExtractedVersionDir} -> {ctx.SitePhysicalPath}"); return; }
            RobocopyMirror(ctx.ExtractedVersionDir!, ctx.SitePhysicalPath);
            ctx.Log(PlanActionType.DeployBlueGreenMirror, $"mirror {ctx.ExtractedVersionDir} -> {ctx.SitePhysicalPath}");
            await Task.CompletedTask;
        }
        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct) => await Task.CompletedTask;

        private static void RobocopyMirror(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            var args = $"\"{src}\" \"{dst}\" /MIR /NFL /NDL /NJH /NJS /R:1 /W:1";
            var psi = new System.Diagnostics.ProcessStartInfo("robocopy", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var p = System.Diagnostics.Process.Start(psi)!; p.WaitForExit();
        }
    }

    // 4) Bindings (http/https + sertifika)
    public sealed class EnsureBindingsStep : IDeployStep
    {
        public string Name => "EnsureBindings";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            using var sm = new ServerManager();
            var siteName = FindSiteByPhysical(sm, ctx.SitePhysicalPath) ?? ctx.Manifest.SiteName;
            var site = sm.Sites[siteName];

            foreach (var b in ctx.Manifest.Bindings)
            {
                var info = $"{b.Ip}:{b.Port}:{b.Host}";
                var exist = site.Bindings.FirstOrDefault(x => x.Protocol == b.Protocol && x.BindingInformation == info);
                if (exist is not null) continue;

                if (!ctx.DryRun)
                {
                    if (b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                    {
                        var cert = FindCert(b.CertThumbprint!, b.CertStore);
                        if (cert is null) throw new InvalidOperationException($"Certificate {b.CertThumbprint} not found in store {b.CertStore}");
                        site.Bindings.Add(info, cert.GetCertHash(), b.CertStore);
                    }
                    else site.Bindings.Add(info, "http");
                    sm.CommitChanges();
                }
                ctx.Log(PlanActionType.EnsureBindings, $"add {b.Protocol} {info}");
            }
            await Task.CompletedTask;
        }
        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct) => await Task.CompletedTask;

        private static string? FindSiteByPhysical(ServerManager sm, string phys)
            => sm.Sites.FirstOrDefault(s => string.Equals(s.Applications["/"].VirtualDirectories["/"].PhysicalPath, phys, StringComparison.OrdinalIgnoreCase))?.Name;

        private static X509Certificate2? FindCert(string thumb, string storeName)
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var t = thumb.Replace(" ", string.Empty).ToUpperInvariant();
            return store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.Thumbprint?.ToUpperInvariant() == t);
        }
    }

    // 5) Env vars (AppPool)
    public sealed class SetEnvStep : IDeployStep
    {
        public string Name => "SetEnv";
        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var env = ctx.Manifest.Env;
            if (env is null || env.Count == 0) { ctx.Log(PlanActionType.Noop, "no env"); return; }

            using var sm = new ServerManager();
            var pool = sm.ApplicationPools[ctx.Manifest.AppPool.Name];
            if (!ctx.DryRun)
            {
                foreach (var kv in env) pool.EnvironmentVariables[kv.Key] = kv.Value;
                sm.CommitChanges();
            }
            ctx.Log(PlanActionType.SetEnv, $"set {env.Count} env");
            await Task.CompletedTask;
        }
        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct) => await Task.CompletedTask;
    }

    // 6) ACL
    public sealed class ApplyAclStep : IDeployStep
    {
        public string Name => "ApplyAcl";
        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var list = ctx.Manifest.Acls;
            if (list is null || list.Count == 0) { ctx.Log(PlanActionType.Noop, "no acl"); return; }

            foreach (var a in list)
            {
                var path = a.Path.Replace("%IISROOT%", ctx.IisRoot);
                if (ctx.DryRun) { ctx.Log(PlanActionType.ApplyAcl, $"(dry-run) {a.Identity} {a.Rights} -> {path}"); continue; }
                ApplyIcacls(path, a.Identity, a.Rights);
                ctx.Log(PlanActionType.ApplyAcl, $"{a.Identity} {a.Rights} -> {path}");
            }
            await Task.CompletedTask;
        }
        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct) => await Task.CompletedTask;

        private static void ApplyIcacls(string path, string identity, string rights)
        {
            Directory.CreateDirectory(path);
            var flag = rights switch { "F" => "(OI)(CI)F", "R" => "(OI)(CI)R", _ => "(OI)(CI)M" };
            var psi = new System.Diagnostics.ProcessStartInfo("icacls", $"\"{path}\" /grant {identity}:{flag} /T")
            { CreateNoWindow = true, UseShellExecute = false };
            var p = System.Diagnostics.Process.Start(psi)!; p.WaitForExit();
        }
    }

    // 7) Features
    public sealed class ConfigureFeaturesStep : IDeployStep
    {
        public string Name => "ConfigureFeatures";
        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var f = ctx.Manifest.Features;
            if (f is null) { ctx.Log(PlanActionType.Noop, "no features"); return; }

            using var sm = new ServerManager();
            var siteName = FindSiteByPhysical(sm, ctx.SitePhysicalPath) ?? ctx.Manifest.SiteName;
            var config = sm.GetWebConfiguration(siteName);

            if (!ctx.DryRun)
            {
                if (f.RequestFiltering?.MaxAllowedContentLength is long macl)
                {
                    var rf = config.GetSection("system.webServer/security/requestFiltering");
                    rf["maxAllowedContentLength"] = macl;
                }
                if (f.Compression is not null)
                {
                    var comp = config.GetSection("system.webServer/urlCompression");
                    comp["doStaticCompression"] = f.Compression.Static;
                    comp["doDynamicCompression"] = f.Compression.Dynamic;
                }
                // URL Rewrite import: pratikte AppCmd.exe / XML merge önerilir.
                sm.CommitChanges();
            }
            ctx.Log(PlanActionType.ConfigureFeatures, "features configured");
            await Task.CompletedTask;
        }
        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct) => await Task.CompletedTask;

        private static string? FindSiteByPhysical(ServerManager sm, string phys)
            => sm.Sites.FirstOrDefault(s => string.Equals(s.Applications["/"].VirtualDirectories["/"].PhysicalPath, phys, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    // 8) Recycle + Start
    public sealed class RecycleAndStartStep : IDeployStep
    {
        public string Name => "RecycleAndStart";
        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            if (ctx.DryRun)
            {
                ctx.Log(PlanActionType.Recycle, "(dry-run) recycle pool");
                ctx.Log(PlanActionType.StartSite, "(dry-run) start site");
                return;
            }

            using var sm = new ServerManager();
            var pool = sm.ApplicationPools[ctx.Manifest.AppPool.Name];
            try { pool.Recycle(); } catch { }

            var siteName = FindSiteByPhysical(sm, ctx.SitePhysicalPath) ?? ctx.Manifest.SiteName;
            var site = sm.Sites[siteName];
            if (site.State != ObjectState.Started) site.Start();

            ctx.Log(PlanActionType.Recycle, $"recycled {pool.Name}");
            ctx.Log(PlanActionType.StartSite, $"started {siteName}");
            await Task.CompletedTask;
        }
        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct) => await Task.CompletedTask;

        private static string? FindSiteByPhysical(ServerManager sm, string phys)
            => sm.Sites.FirstOrDefault(s => string.Equals(s.Applications["/"].VirtualDirectories["/"].PhysicalPath, phys, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    // 9) Health-check + advanced Blue/Green swap
    public sealed class HealthCheckAndSwapStep : IDeployStep
    {
        public string Name => "HealthCheckAndSwap";
        private readonly List<(string site, string proto, string info)> _swapped = new();
        private readonly List<(string site, string proto, string info, byte[]? hash, string? store)> _copiedToPassive = new();

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var bg = ctx.Manifest.BlueGreen;
            var urls = bg?.HealthUrls ?? new List<string>();
            if (urls.Count == 0) { ctx.Log(PlanActionType.Noop, "no health urls"); return; }

            if (ctx.DryRun)
            {
                ctx.Log(PlanActionType.HealthCheck, $"(dry-run) {string.Join(", ", urls)}");
                if (ctx.UseBlueGreen) ctx.Log(PlanActionType.SwapBindings, "(dry-run) swap");
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(bg?.HealthTimeoutSeconds ?? 15) };
            foreach (var u in urls)
            {
                var ok = await WaitHealthy(http, u, bg?.HealthAttempts ?? 10, ct);
                if (!ok) throw new InvalidOperationException($"health failed: {u}");
            }
            ctx.Log(PlanActionType.HealthCheck, "all 200");

            if (ctx.UseBlueGreen)
            {
                using var sm = new ServerManager();
                var (active, passive) = ResolveBlueGreen(sm, ctx.Manifest.SiteName, bg!.ActiveSuffix, bg.SlotSuffix);

                if (bg.StopActiveBeforeSwap && active.State == ObjectState.Started)
                    active.Stop();

                // Swap order (https -> http default)
                foreach (var proto in NormalizeOrder(bg.SwapProtocolOrder))
                    SwapProtocol(active, passive, proto, sm, _swapped, _copiedToPassive, bg.PreserveOldBindingsForRollback);

                sm.CommitChanges();
                ctx.Log(PlanActionType.SwapBindings, $"swapped {active.Name} -> {passive.Name}");
            }
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            if (ctx.DryRun || (!ctx.UseBlueGreen)) return;

            using var sm = new ServerManager();

            // Eğer preserveOldBindingsForRollback=true ise aktiften silmemiş, sadece pasife eklemiştik.
            // Bu durumda rollback: pasife eklediklerimizi geri sil.
            if (ctx.Manifest.BlueGreen!.PreserveOldBindingsForRollback && _copiedToPassive.Count > 0)
            {
                foreach (var (site, proto, info, _, _) in _copiedToPassive)
                {
                    var s = sm.Sites[site];
                    var b = s.Bindings.FirstOrDefault(x => x.Protocol == proto && x.BindingInformation == info);
                    if (b is not null) s.Bindings.Remove(b);
                }
                sm.CommitChanges();
                return;
            }

            // Aksi halde “aktiften kaldırıp pasife eklediğimiz” kayıtları geri al:
            if (_swapped.Count > 0)
            {
                foreach (var (siteName, proto, info) in _swapped)
                {
                    var site = sm.Sites[siteName];
                    var b = site.Bindings.FirstOrDefault(x => x.Protocol == proto && x.BindingInformation == info);
                    if (b is not null) site.Bindings.Remove(b);
                }
                sm.CommitChanges();
            }
            await Task.CompletedTask;
        }

        private static async Task<bool> WaitHealthy(HttpClient http, string url, int attempts, CancellationToken ct)
        {
            for (int i = 0; i < attempts; i++)
            {
                try { var r = await http.GetAsync(url, ct); if ((int)r.StatusCode is >= 200 and < 300) return true; }
                catch { }
                await Task.Delay(1000, ct);
            }
            return false;
        }

        private static (Site active, Site passive) ResolveBlueGreen(ServerManager sm, string baseName, string activeSuffix, string passiveSuffix)
        {
            var a = sm.Sites.FirstOrDefault(s => s.Name == baseName + activeSuffix);
            var b = sm.Sites.FirstOrDefault(s => s.Name == baseName + passiveSuffix);
            if (a is null || b is null) throw new InvalidOperationException("blue/green sites not found");

            // Aktif olanı “daha çok public binding” ölçütü ile teyit et
            var aScore = a.Bindings.Count(bd => bd.Protocol is "https" or "http");
            var bScore = b.Bindings.Count(bd => bd.Protocol is "https" or "http");
            return (aScore >= bScore) ? (a, b) : (b, a);
        }

        private static IEnumerable<string> NormalizeOrder(IEnumerable<string> order)
        {
            var set = order.Select(p => p.ToLowerInvariant()).Where(p => p is "http" or "https").ToList();
            if (set.Count == 0) set.AddRange(new[] { "https", "http" });
            return set;
        }

        private static void SwapProtocol(
            Site active, Site passive, string proto, ServerManager sm,
            List<(string site, string proto, string info)> removedFromActive,
            List<(string site, string proto, string info, byte[]? hash, string? store)> copiedToPassive,
            bool preserveOldBindingsForRollback)
        {
            var toMove = active.Bindings.Where(x => x.Protocol.Equals(proto, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var b in toMove)
            {
                var info = b.BindingInformation;

                if (proto == "https")
                {
                    // https: sertifikayı da pasife bağla
                    passive.Bindings.Add(info, b.CertificateHash, b.CertificateStoreName);
                    copiedToPassive.Add((passive.Name, "https", info, b.CertificateHash, b.CertificateStoreName));
                }
                else
                {
                    passive.Bindings.Add(info, "http");
                    copiedToPassive.Add((passive.Name, "http", info, null, null));
                }

                if (!preserveOldBindingsForRollback)
                {
                    removedFromActive.Add((active.Name, proto, info));
                    active.Bindings.Remove(b);
                }
            }
        }
    }
    #endregion

    #region Public Service (Wizard)
    public sealed class DeploymentService
    {
        private readonly ILogger<DeploymentService> _logger;
        public DeploymentService(ILogger<DeploymentService> logger) => _logger = logger;

        public DeploymentPlan BuildPlan(string manifestJson)
        {
            var m = SiteManifest.FromJson(manifestJson);
            var plan = new DeploymentPlan { UseBlueGreen = m.BlueGreen?.Enabled == true };

            plan.Add(PlanActionType.DeployContentAtomicSwitch, "Extract zip to versioned folder");
            plan.Add(PlanActionType.EnsureAppPool, $"Ensure AppPool {m.AppPool.Name}");
            plan.Add(PlanActionType.EnsureSite, $"Ensure Site {(plan.UseBlueGreen ? $"{m.SiteName}{m.BlueGreen!.ActiveSuffix}/{m.SiteName}{m.BlueGreen!.SlotSuffix}" : m.SiteName)}");
            plan.Add(PlanActionType.EnsureApplications, $"Ensure {m.Applications.Count} application(s)");
            plan.Add(PlanActionType.EnsureBindings, "Ensure bindings (http/https + certificate)");
            if (m.Env?.Count > 0) plan.Add(PlanActionType.SetEnv, $"Set {m.Env.Count} environment variables");
            if (m.Acls?.Count > 0) plan.Add(PlanActionType.ApplyAcl, $"Apply {m.Acls.Count} ACL entries");
            if (m.Features is not null) plan.Add(PlanActionType.ConfigureFeatures, "Configure features");
            plan.Add(plan.UseBlueGreen ? PlanActionType.DeployBlueGreenMirror : PlanActionType.DeployContentAtomicSwitch,
                     plan.UseBlueGreen ? "Mirror to passive slot" : "Atomic switch (root -> version dir)");
            plan.Add(PlanActionType.Recycle, "Recycle AppPool");
            plan.Add(PlanActionType.StartSite, "Start Site");
            if (m.BlueGreen?.HealthUrls?.Count > 0) plan.Add(PlanActionType.HealthCheck, "Health-check URLs");
            if (plan.UseBlueGreen) plan.Add(PlanActionType.SwapBindings, $"Swap public bindings ({string.Join("→", (m.BlueGreen?.SwapProtocolOrder ?? new() { "https", "http" }).Select(x => x.ToUpper()))})");
            return plan;
        }

        public async Task<List<PlanAction>> DryRunAsync(string zipPath, string manifestJson, string baseDeployDir, CancellationToken ct = default)
        {
            var ctx = new DeployContext { ZipPath = zipPath, Manifest = SiteManifest.FromJson(manifestJson), BaseDeployDir = baseDeployDir, DryRun = true, Logger = _logger };
            var steps = BuildAtomicScopes(ctx);
            await new PipelineEngine(_logger).RunAsync(steps, ctx, ct);
            return ctx.Executed;
        }

        public async Task<List<PlanAction>> ApplyAsync(string zipPath, string manifestJson, string baseDeployDir, CancellationToken ct = default)
        {
            var ctx = new DeployContext { ZipPath = zipPath, Manifest = SiteManifest.FromJson(manifestJson), BaseDeployDir = baseDeployDir, DryRun = false, Logger = _logger };
            var steps = BuildAtomicScopes(ctx);
            await new PipelineEngine(_logger).RunAsync(steps, ctx, ct);
            return ctx.Executed;
        }

        private static IEnumerable<IDeployStep> BuildAtomicScopes(DeployContext ctx)
        {
            // Scope 1: İçerik hazırlığı
            var scopeContent = new AtomicScopeStep("Content", new IDeployStep[]
            {
                new ExtractZipStep(),
                new BlueGreenMirrorStep() // no-op if disabled
            });

            // Scope 2: IIS yapısı
            var scopeIis = new AtomicScopeStep("IIS", new IDeployStep[]
            {
                new EnsureAppPoolStep(),
                new EnsureSiteAndAppsStep(),
                new EnsureBindingsStep()
            });

            // Scope 3: Konfig & İzinler
            var scopeConfig = new AtomicScopeStep("Config", new IDeployStep[]
            {
                new SetEnvStep(),
                new ApplyAclStep(),
                new ConfigureFeaturesStep()
            });

            // Scope 4: Aktivasyon
            var scopeActivation = new AtomicScopeStep("Activation", new IDeployStep[]
            {
                new RecycleAndStartStep(),
                new HealthCheckAndSwapStep()
            });

            return new IDeployStep[] { scopeContent, scopeIis, scopeConfig, scopeActivation };
        }
    }

    public sealed class DeploymentPlan
    {
        public bool UseBlueGreen { get; init; }
        public List<PlanAction> Actions { get; } = new();
        public void Add(PlanActionType t, string d) => Actions.Add(new PlanAction(t, d));
    }
    #endregion
}
