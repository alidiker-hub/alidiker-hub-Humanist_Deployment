// DeploymentEngine.cs
// .NET 9 / Blazor Server / Microsoft.Web.Administration
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Web.Administration;

namespace Humanist.Deployer
{
    #region Manifest Models
    public sealed class SiteManifest
    {
        [JsonPropertyName("siteName")] public string SiteName { get; set; } = default!;
        [JsonPropertyName("physicalPath")] public string PhysicalPathInZip { get; set; } = ".";
        [JsonPropertyName("bindings")] public List<BindingDef> Bindings { get; set; } = new();
        [JsonPropertyName("appPool")] public AppPoolDef AppPool { get; set; } = new();
        [JsonPropertyName("applications")] public List<ApplicationDef> Applications { get; set; } = new();
        [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }
        [JsonPropertyName("acl")] public List<AclDef>? Acls { get; set; }
        [JsonPropertyName("features")] public FeaturesDef? Features { get; set; }
        [JsonPropertyName("blueGreen")] public BlueGreenDef? BlueGreen { get; set; }

        // Sertifika eksikse https binding'lerini atlayıp ilerle
        [JsonPropertyName("allowMissingCertificates")] public bool AllowMissingCertificates { get; set; } = false;

        public static SiteManifest FromJson(string json)
            => JsonSerializer.Deserialize<SiteManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            })!;
    }

    public sealed class BindingDef
    {
        [JsonPropertyName("protocol")] public string Protocol { get; set; } = "http"; // http | https
        [JsonPropertyName("ip")] public string Ip { get; set; } = "*";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("certThumbprint")] public string? CertThumbprint { get; set; }
        [JsonPropertyName("certStore")] public string CertStore { get; set; } = "My";

        // Tekil binding için atlama bayrağı
        [JsonPropertyName("skipIfCertMissing")] public bool SkipIfCertMissing { get; set; } = false;
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
        [JsonPropertyName("type")] public string Type { get; set; } = "ApplicationPoolIdentity"; // LocalSystem|LocalService|NetworkService|SpecificUser
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
        [JsonPropertyName("path")] public string Path { get; set; } = "/api";                          // IIS application path
        [JsonPropertyName("physicalPath")] public string PhysicalPathInZip { get; set; } = "content";  // ZIP içindeki göreli klasör
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

    public sealed class BlueGreenDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;
        [JsonPropertyName("activeSuffix")] public string ActiveSuffix { get; set; } = "_A";
        [JsonPropertyName("slotSuffix")] public string SlotSuffix { get; set; } = "_B"; // passive
        [JsonPropertyName("healthUrls")] public List<string>? HealthUrls { get; set; }
        [JsonPropertyName("healthTimeoutSeconds")] public int HealthTimeoutSeconds { get; set; } = 15;
        [JsonPropertyName("healthAttempts")] public int HealthAttempts { get; set; } = 10;
        [JsonPropertyName("swapProtocolOrder")] public List<string> SwapProtocolOrder { get; set; } = new() { "https", "http" };
        [JsonPropertyName("stopActiveBeforeSwap")] public bool StopActiveBeforeSwap { get; set; } = false;
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

    public sealed record PlanAction(PlanActionType Type, string Description, DateTime Timestamp, bool IsDryRun = false);

    public sealed class DeployContext
    {
        public string ZipPath { get; init; } = default!;
        public SiteManifest Manifest { get; init; } = default!;
        public string BaseDeployDir { get; init; } = default!;
        public bool DryRun { get; init; }
        public ILogger Logger { get; init; } = default!;

        public string? ExtractedVersionDir { get; set; }   // D:\Sites\Contoso_yyyyMMddHHmmss
        public string SitePhysicalPath { get; set; } = ""; // atomic: = ExtractedVersionDir; blue/green: passive slot kökü
        public bool UseBlueGreen => Manifest.BlueGreen?.Enabled == true;
        public string? ResolvedSiteName { get; set; }      // hangi site üstünde çalışıyoruz (A/B/base)

        public readonly List<PlanAction> Executed = new();
        public string IisRoot => Environment.GetFolderPath(Environment.SpecialFolder.System).Replace("\\System32", "") + "\\inetpub";

        // Basit ve güvenli loglama - re-entrancy sorunu çözüldü
        public void Log(PlanActionType type, string message)
        {
            var action = new PlanAction(type, message, DateTime.UtcNow, DryRun);
            Executed.Add(action);

            // Log seviyelerini daha anlamlı hale getir
            var logLevel = type switch
            {
                PlanActionType.Noop => LogLevel.Debug,
                PlanActionType.HealthCheck => LogLevel.Information,
                PlanActionType.SwapBindings => LogLevel.Warning, // Önemli işlem
                _ => LogLevel.Information
            };

            var prefix = DryRun ? "[DRY-RUN] " : "";
            Logger.Log(logLevel, "{Prefix}[{ActionType}] {Message}", prefix, type, message);
        }

        public void LogError(PlanActionType type, string message, Exception? ex = null)
        {
            var action = new PlanAction(type, $"{message} - Error: {ex?.Message}", DateTime.UtcNow, DryRun);
            Executed.Add(action);

            var prefix = DryRun ? "[DRY-RUN] " : "";
            if (ex != null)
                Logger.LogError(ex, "{Prefix}[{ActionType}] {Message}", prefix, type, message);
            else
                Logger.LogError("{Prefix}[{ActionType}] {Message}", prefix, type, message);
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
                foreach (var step in _inner)
                {
                    ctx.Logger.LogInformation("▶ Executing step: {StepName}", step.Name);
                    await step.ExecuteAsync(ctx, ct);
                    _executed.Push(step);
                    ctx.Logger.LogDebug("✅ Step completed: {StepName}", step.Name);
                }
                ctx.Log(PlanActionType.Noop, $"-- Commit {Name}");
            }
            catch (Exception ex)
            {
                ctx.LogError(PlanActionType.Noop, $"-- Rollback {Name} due to error", ex);
                await RollbackExecutedSteps(ctx, ct);
                throw;
            }
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            await RollbackExecutedSteps(ctx, ct);
        }

        private async Task RollbackExecutedSteps(DeployContext ctx, CancellationToken ct)
        {
            while (_executed.Count > 0)
            {
                var step = _executed.Pop();
                try
                {
                    ctx.Logger.LogWarning("↩ Rolling back step: {StepName}", step.Name);
                    await step.RollbackAsync(ctx, ct);
                    ctx.Logger.LogInformation("↩ Rollback completed: {StepName}", step.Name);
                }
                catch (Exception rex)
                {
                    ctx.LogError(PlanActionType.Noop, $"Rollback failed for step {step.Name}", rex);
                }
            }
        }
    }

    public sealed class PipelineEngine
    {
        private readonly ILogger _logger;
        public PipelineEngine(ILogger logger) => _logger = logger;

        public async Task RunAsync(IEnumerable<IDeployStep> steps, DeployContext ctx, CancellationToken ct)
        {
            var executed = new Stack<IDeployStep>();
            var stepList = steps.ToList();

            _logger.LogInformation("🚀 Starting deployment pipeline with {StepCount} steps", stepList.Count);

            try
            {
                for (int i = 0; i < stepList.Count; i++)
                {
                    var step = stepList[i];
                    _logger.LogInformation("▶ Step {StepNumber}/{TotalSteps}: {StepName}", i + 1, stepList.Count, step.Name);

                    await step.ExecuteAsync(ctx, ct);
                    executed.Push(step);

                    _logger.LogDebug("✅ Step {StepNumber} completed: {StepName}", i + 1, step.Name);
                }
                _logger.LogInformation("🎉 Deployment pipeline completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Pipeline failed at step {StepName} - starting rollback", executed.Count > 0 ? executed.Peek().Name : "unknown");
                await RollbackPipeline(ctx, ct, executed);
                throw;
            }
        }

        private async Task RollbackPipeline(DeployContext ctx, CancellationToken ct, Stack<IDeployStep> executed)
        {
            _logger.LogWarning("🔄 Starting rollback of {StepCount} executed steps", executed.Count);

            int rolledBackCount = 0;
            while (executed.Count > 0)
            {
                var step = executed.Pop();
                try
                {
                    _logger.LogWarning("↩ Rolling back: {StepName}", step.Name);
                    await step.RollbackAsync(ctx, ct);
                    rolledBackCount++;
                    _logger.LogInformation("✅ Rollback completed: {StepName}", step.Name);
                }
                catch (Exception rex)
                {
                    _logger.LogError(rex, "❌ Rollback failed for step: {StepName}", step.Name);
                    // Continue with other rollbacks despite errors
                }
            }

            _logger.LogInformation("📊 Rollback finished: {RolledBack}/{Total} steps rolled back", rolledBackCount, rolledBackCount);
        }
    }
    #endregion

    #region Helpers
    static class IisLookup
    {
        public static Site GetTargetSite(ServerManager sm, DeployContext ctx)
        {
            if (!string.IsNullOrWhiteSpace(ctx.ResolvedSiteName))
            {
                var site = sm.Sites[ctx.ResolvedSiteName];
                if (site != null) return site;
            }

            var baseSite = sm.Sites[ctx.Manifest.SiteName];
            if (baseSite != null) return baseSite;

            var bg = ctx.Manifest.BlueGreen;
            if (bg is not null)
            {
                var activeSite = sm.Sites[ctx.Manifest.SiteName + bg.ActiveSuffix];
                if (activeSite != null) return activeSite;

                var passiveSite = sm.Sites[ctx.Manifest.SiteName + bg.SlotSuffix];
                if (passiveSite != null) return passiveSite;
            }

            throw new InvalidOperationException($"Target site not found (base: {ctx.Manifest.SiteName}).");
        }
    }

    static class ZipMirror
    {
        public static void RobocopyMirror(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            var args = $"\"{src}\" \"{dst}\" /MIR /NFL /NDL /NJH /NJS /R:1 /W:1";
            var psi = new System.Diagnostics.ProcessStartInfo("robocopy", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi)!;
            process.WaitForExit();

            if (process.ExitCode > 7) // Robocopy error codes > 7 indicate serious errors
            {
                throw new InvalidOperationException($"Robocopy failed with exit code: {process.ExitCode}");
            }
        }
    }

    static class PathUtil
    {
        public static string[] AppPathSegments(string appPath)
        {
            if (string.IsNullOrWhiteSpace(appPath))
                return Array.Empty<string>();

            return appPath.Trim().Trim('/')
                          .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public static string CombineUnder(string basePhys, string appPath)
        {
            var segs = AppPathSegments(appPath);
            if (segs.Length == 0)
                return basePhys;

            // Tüm segmentleri birleştir - "paketler" gibi extra klasörler EKLEME
            var pathParts = new List<string> { basePhys };
            pathParts.AddRange(segs);
            return Path.Combine(pathParts.ToArray());
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return path.Replace('/', '\\').Trim('\\', '/');
        }

        // Yeni: Path'in geçerli olup olmadığını kontrol et
        public static bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    #endregion

    #region Steps
    // 0) Validation – sertifika ve önkoşul kontrolü
    public sealed class ValidatePrerequisitesStep : IDeployStep
    {
        public string Name => "ValidatePrerequisites";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogInformation("🔍 Validating prerequisites...");

            if (!File.Exists(ctx.ZipPath))
                throw new InvalidOperationException($"Zip not found: {ctx.ZipPath}");

            if (string.IsNullOrWhiteSpace(ctx.Manifest.AppPool?.Name))
                throw new InvalidOperationException("AppPool name is required.");

            var httpsBindings = ctx.Manifest.Bindings
                .Where(b => b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (httpsBindings.Count > 0)
            {
                var missingCerts = new List<string>();
                foreach (var binding in httpsBindings)
                {
                    if (string.IsNullOrWhiteSpace(binding.CertThumbprint))
                    {
                        missingCerts.Add($"{binding.Host}:{binding.Port} (no thumbprint)");
                        continue;
                    }

                    var cert = EnsureBindingsStep.FindCertStatic(binding.CertThumbprint!, binding.CertStore);
                    if (cert is null)
                        missingCerts.Add($"{binding.Host}:{binding.Port} (thumbprint {binding.CertThumbprint})");
                }

                if (missingCerts.Count > 0 && !ctx.Manifest.AllowMissingCertificates &&
                    !httpsBindings.Any(b => b.SkipIfCertMissing))
                {
                    var errorMessage = "Missing certificates for HTTPS bindings: " + string.Join(", ", missingCerts)
                              + " | Install certs or set allowMissingCertificates=true / per-binding skipIfCertMissing=true.";
                    throw new InvalidOperationException(errorMessage);
                }

                ctx.Logger.LogInformation("✅ HTTPS certificate validation passed");
            }

            ctx.Log(PlanActionType.Noop, "Validation completed successfully");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct) => await Task.CompletedTask;
    }

    // 1) ZIP -> versiyon klasörü (iç ZIP desteği)
    // 1) ZIP -> versiyon klasörü (iç ZIP desteği) - DÜZELTİLMİŞ

    // 2) AppPool upsert
    public sealed class EnsureAppPoolStep : IDeployStep
    {
        public string Name => "EnsureAppPool";
        private (string runtime, string pipeline, bool enable32, ProcessModelIdentityType idType, string? user, string? pwd, bool auto, StartMode startMode)? _old;

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogInformation("🏊 Ensuring AppPool: {AppPoolName}", ctx.Manifest.AppPool.Name);

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
                ctx.Logger.LogInformation("✅ AppPool configured: {AppPoolName}", def.Name);
            }

            ctx.Log(PlanActionType.EnsureAppPool, $"{def.Name} ensured");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            if (ctx.DryRun || _old is null) return;

            ctx.Logger.LogWarning("↩ Rolling back AppPool: {AppPoolName}", ctx.Manifest.AppPool.Name);

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

            ctx.Logger.LogInformation("✅ AppPool rollback completed: {AppPoolName}", ctx.Manifest.AppPool.Name);

            await Task.CompletedTask;
        }
    }

    // 3) Site + Applications (atomic/blue-green bootstrap güvenli)
    public sealed class EnsureSiteAndAppsStep : IDeployStep
    {
        public string Name => "EnsureSiteAndApps";
        private string? _oldRootPhys;

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogInformation("🌐 Ensuring Site and Applications...");

            using var sm = new ServerManager();

            if (!ctx.UseBlueGreen)
            {
                var versionPhys = ctx.ExtractedVersionDir!;
                if (!ctx.DryRun) Directory.CreateDirectory(versionPhys);

                var site = sm.Sites.FirstOrDefault(s => s.Name == ctx.Manifest.SiteName);
                if (site is null)
                {
                    site = sm.Sites.Add(ctx.Manifest.SiteName, "http", "*:80:", versionPhys);
                    site.ServerAutoStart = true;
                    ctx.Logger.LogInformation("✅ Created new site: {SiteName}", ctx.Manifest.SiteName);
                }
                else
                {
                    _oldRootPhys = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                    site.Applications["/"].VirtualDirectories["/"].PhysicalPath = versionPhys;
                    ctx.Logger.LogInformation("🔄 Updated existing site: {SiteName}", ctx.Manifest.SiteName);
                }
                site.Applications["/"].ApplicationPoolName = ctx.Manifest.AppPool.Name;

                EnsureChildApps(site, versionPhys, ctx, createDirs: !ctx.DryRun);
                if (!ctx.DryRun) sm.CommitChanges();

                ctx.ResolvedSiteName = ctx.Manifest.SiteName;
                ctx.SitePhysicalPath = versionPhys;
                ctx.Log(PlanActionType.EnsureSite, $"{ctx.ResolvedSiteName} -> {versionPhys}");
                ctx.Log(PlanActionType.EnsureApplications, $"{ctx.Manifest.Applications.Count} application(s) ensured");
                await Task.CompletedTask;
                return;
            }

            // ---- Blue/Green ----
            var bg = ctx.Manifest.BlueGreen!;
            var aName = ctx.Manifest.SiteName + bg.ActiveSuffix;
            var bName = ctx.Manifest.SiteName + bg.SlotSuffix;

            ctx.Logger.LogInformation("🔵🟢 Blue/Green mode: Active={ActiveSite}, Passive={PassiveSite}", aName, bName);

            var aSite = sm.Sites.FirstOrDefault(s => s.Name == aName);
            var bSite = sm.Sites.FirstOrDefault(s => s.Name == bName);

            var aPhys = Path.Combine(ctx.BaseDeployDir, aName);
            var bPhys = Path.Combine(ctx.BaseDeployDir, bName);
            if (!ctx.DryRun) { Directory.CreateDirectory(aPhys); Directory.CreateDirectory(bPhys); }

            if (aSite is null)
            {
                aSite = sm.Sites.Add(aName, "http", "*:80:", aPhys);
                aSite.ServerAutoStart = true;
                ctx.Logger.LogInformation("✅ Created active site: {SiteName}", aName);
            }
            if (bSite is null)
            {
                bSite = sm.Sites.Add(bName, "http", "*:80:", bPhys);
                bSite.ServerAutoStart = true;
                ctx.Logger.LogInformation("✅ Created passive site: {SiteName}", bName);
            }

            // Pasif slotu seç: "daha az public binding" puanı
            var aScore = aSite.Bindings.Count(b => b.Protocol is "http" or "https");
            var bScore = bSite.Bindings.Count(b => b.Protocol is "http" or "https");
            var passive = (aScore <= bScore) ? aSite : bSite;

            ctx.Logger.LogInformation("🎯 Selected passive slot: {PassiveSite} (bindings: Active={ActiveScore}, Passive={PassiveScore})",
                passive.Name, aScore, bScore);

            // Pasif slot kökünü versiyon dizinine çevir
            var versionPhysBG = ctx.ExtractedVersionDir!;
            passive.Applications["/"].VirtualDirectories["/"].PhysicalPath = versionPhysBG;
            passive.Applications["/"].ApplicationPoolName = ctx.Manifest.AppPool.Name;

            // Diğer slot da doğru pool ve kendi klasörünü işaret etsin
            var other = (passive.Name == aName) ? bSite : aSite;
            var otherPhys = (other.Name == aName) ? aPhys : bPhys;
            other.Applications["/"].VirtualDirectories["/"].PhysicalPath = otherPhys;
            other.Applications["/"].ApplicationPoolName = ctx.Manifest.AppPool.Name;

            // Alt uygulamalar (pasif slota versiyon dizininden)
            EnsureChildApps(passive, versionPhysBG, ctx, createDirs: !ctx.DryRun);

            if (!ctx.DryRun) sm.CommitChanges();

            ctx.ResolvedSiteName = passive.Name;          // kritik: bundan sonra herkes bu ismi kullanacak
            ctx.SitePhysicalPath = versionPhysBG;
            ctx.Log(PlanActionType.EnsureSite, $"{aName}/{bName} (passive: {passive.Name}) -> {versionPhysBG}");
            ctx.Log(PlanActionType.EnsureApplications, $"{ctx.Manifest.Applications.Count} application(s) ensured on {passive.Name}");

            ctx.Logger.LogInformation("✅ Blue/Green site configuration completed");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            if (ctx.DryRun || _oldRootPhys is null || ctx.UseBlueGreen) return;

            ctx.Logger.LogWarning("↩ Rolling back Site configuration...");

            using var sm = new ServerManager();
            var targetName = ctx.ResolvedSiteName ?? ctx.Manifest.SiteName;
            var site = sm.Sites.FirstOrDefault(s => s.Name == targetName);
            if (site is null) return;
            site.Applications["/"].VirtualDirectories["/"].PhysicalPath = _oldRootPhys;
            sm.CommitChanges();

            ctx.Logger.LogInformation("✅ Site rollback completed: {SiteName}", targetName);
            await Task.CompletedTask;
        }

        private static void EnsureChildApps(Site site, string basePhys, DeployContext ctx, bool createDirs)
        {
            // Ebeveynler önce: "/admin" -> "/admin/HXCORE"
            foreach (var app in ctx.Manifest.Applications.OrderBy(a => a.Path.Count(c => c == '/')))
            {
                var appPath = app.Path.StartsWith("/") ? app.Path : "/" + app.Path;
                var phys = PathUtil.CombineUnder(basePhys, appPath);

                if (createDirs) Directory.CreateDirectory(phys);

                var ex = site.Applications.FirstOrDefault(a => a.Path.Equals(appPath, StringComparison.OrdinalIgnoreCase))
                         ?? site.Applications.Add(appPath, phys);

                ex.VirtualDirectories["/"].PhysicalPath = phys;
                ex.ApplicationPoolName = ctx.Manifest.AppPool.Name;

                ctx.Logger.LogDebug("✅ Configured application: {AppPath} -> {PhysicalPath}", appPath, phys);
            }
        }
    }

    // 4) Blue/Green mirror (pasif slota dosya kopyalama)
    public sealed class BlueGreenMirrorStep : IDeployStep
    {
        public string Name => "BlueGreenMirror";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            if (!ctx.UseBlueGreen)
            {
                ctx.Log(PlanActionType.Noop, "blue/green disabled");
                return;
            }

            ctx.Logger.LogInformation("🔄 Blue/Green mirroring started...");

            if (ctx.DryRun)
            {
                ctx.Log(PlanActionType.DeployBlueGreenMirror, $"(dry-run) mirror {ctx.ExtractedVersionDir} -> {ctx.SitePhysicalPath}");
                return;
            }

            ZipMirror.RobocopyMirror(ctx.ExtractedVersionDir!, ctx.SitePhysicalPath);
            ctx.Log(PlanActionType.DeployBlueGreenMirror, $"mirror {ctx.ExtractedVersionDir} -> {ctx.SitePhysicalPath}");

            ctx.Logger.LogInformation("✅ Blue/Green mirroring completed");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogDebug("No rollback needed for BlueGreenMirrorStep");
            await Task.CompletedTask;
        }
    }

    // 5) Bindings (http/https + sertifika) — BG bootstrap güvenli + opsiyonel atlama
    public sealed class EnsureBindingsStep : IDeployStep
    {
        public string Name => "EnsureBindings";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogInformation("🔗 Ensuring bindings...");

            using var sm = new ServerManager();
            var site = IisLookup.GetTargetSite(sm, ctx);

            // BG bootstrap: iki sitede de public binding yoksa, burada ekleme (health sonrası atanacak)
            if (ctx.UseBlueGreen)
            {
                var bg = ctx.Manifest.BlueGreen!;
                var a = sm.Sites[ctx.Manifest.SiteName + bg.ActiveSuffix];
                var b = sm.Sites[ctx.Manifest.SiteName + bg.SlotSuffix];
                int pubA = a.Bindings.Count(x => x.Protocol is "http" or "https");
                int pubB = b.Bindings.Count(x => x.Protocol is "http" or "https");
                if (pubA == 0 && pubB == 0)
                {
                    ctx.Log(PlanActionType.Noop, "BG bootstrap: deferring public bindings until post-health swap/assignment");
                    ctx.Logger.LogInformation("⏳ Deferring bindings for Blue/Green bootstrap");
                    await Task.CompletedTask;
                    return;
                }
            }

            int bindingsAdded = 0;
            foreach (var binding in ctx.Manifest.Bindings)
            {
                var info = $"{binding.Ip}:{binding.Port}:{binding.Host}";
                var exist = site.Bindings.FirstOrDefault(x => x.Protocol == binding.Protocol && x.BindingInformation == info);
                if (exist is not null) continue;

                if (!ctx.DryRun)
                {
                    if (binding.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(binding.CertThumbprint))
                        {
                            if (ctx.Manifest.AllowMissingCertificates || binding.SkipIfCertMissing)
                            {
                                ctx.Log(PlanActionType.Noop, $"skip https {info} (no thumbprint)");
                                continue;
                            }
                            throw new InvalidOperationException($"HTTPS binding for {info} needs certThumbprint.");
                        }

                        var cert = FindCert(binding.CertThumbprint!, binding.CertStore);
                        if (cert is null)
                        {
                            if (ctx.Manifest.AllowMissingCertificates || binding.SkipIfCertMissing)
                            {
                                ctx.Log(PlanActionType.Noop, $"skip https {info} (cert not found in {binding.CertStore})");
                                continue;
                            }
                            throw new InvalidOperationException($"Certificate {binding.CertThumbprint} not found in store {binding.CertStore}");
                        }

                        site.Bindings.Add(info, cert.GetCertHash(), binding.CertStore);
                        bindingsAdded++;
                        ctx.Logger.LogDebug("✅ Added HTTPS binding: {BindingInfo}", info);
                    }
                    else
                    {
                        site.Bindings.Add(info, "http");
                        bindingsAdded++;
                        ctx.Logger.LogDebug("✅ Added HTTP binding: {BindingInfo}", info);
                    }
                    sm.CommitChanges();
                }
                ctx.Log(PlanActionType.EnsureBindings, $"add {binding.Protocol} {info}");
            }

            ctx.Logger.LogInformation("✅ Bindings ensured: {AddedCount} bindings added", bindingsAdded);
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogDebug("No rollback needed for EnsureBindingsStep");
            await Task.CompletedTask;
        }

        // Validation step ile paylaşmak için static versiyon
        internal static X509Certificate2? FindCertStatic(string thumb, string storeName)
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var t = thumb.Replace(" ", string.Empty).ToUpperInvariant();
            return store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.Thumbprint?.ToUpperInvariant() == t);
        }

        private static X509Certificate2? FindCert(string thumb, string storeName)
            => FindCertStatic(thumb, storeName);
    }

    // 6) Env vars (AppPool) — config API ile
    public sealed class SetEnvStep : IDeployStep
    {
        public string Name => "SetEnv";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var env = ctx.Manifest.Env;
            if (env is null || env.Count == 0)
            {
                ctx.Log(PlanActionType.Noop, "no env");
                return;
            }

            ctx.Logger.LogInformation("🌍 Setting environment variables: {Count} variables", env.Count);

            using var sm = new ServerManager();
            var pool = sm.ApplicationPools[ctx.Manifest.AppPool.Name];
            if (pool is null) throw new InvalidOperationException($"AppPool not found: {ctx.Manifest.AppPool.Name}");

            if (!ctx.DryRun)
            {
                var appHost = sm.GetApplicationHostConfiguration();
                var appPoolsSection = appHost.GetSection("system.applicationHost/applicationPools");
                var appPoolsCollection = appPoolsSection.GetCollection();
                var poolElement = appPoolsCollection
                    .FirstOrDefault(e => string.Equals((string)e["name"], ctx.Manifest.AppPool.Name, StringComparison.OrdinalIgnoreCase));
                if (poolElement is null)
                    throw new InvalidOperationException($"AppPool element not found in config: {ctx.Manifest.AppPool.Name}");

                var envCollection = poolElement.GetCollection("environmentVariables");
                foreach (var kv in env)
                {
                    var existing = envCollection.FirstOrDefault(el => string.Equals((string)el["name"], kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        var add = envCollection.CreateElement("add");
                        add["name"] = kv.Key;
                        add["value"] = kv.Value;
                        envCollection.Add(add);
                        ctx.Logger.LogDebug("✅ Added environment variable: {Key}", kv.Key);
                    }
                    else
                    {
                        existing["value"] = kv.Value;
                        ctx.Logger.LogDebug("✅ Updated environment variable: {Key}", kv.Key);
                    }
                }
                sm.CommitChanges();
            }
            ctx.Log(PlanActionType.SetEnv, $"set {env.Count} env");
            ctx.Logger.LogInformation("✅ Environment variables configured");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogDebug("No rollback needed for SetEnvStep");
            await Task.CompletedTask;
        }
    }

    // 7) ACL
    public sealed class ApplyAclStep : IDeployStep
    {
        public string Name => "ApplyAcl";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var list = ctx.Manifest.Acls;
            if (list is null || list.Count == 0)
            {
                ctx.Log(PlanActionType.Noop, "no acl");
                return;
            }

            ctx.Logger.LogInformation("🔐 Applying ACLs: {Count} entries", list.Count);

            foreach (var acl in list)
            {
                var path = acl.Path.Replace("%IISROOT%", ctx.IisRoot);
                if (ctx.DryRun)
                {
                    ctx.Log(PlanActionType.ApplyAcl, $"(dry-run) {acl.Identity} {acl.Rights} -> {path}");
                    continue;
                }
                ApplyIcacls(path, acl.Identity, acl.Rights);
                ctx.Log(PlanActionType.ApplyAcl, $"{acl.Identity} {acl.Rights} -> {path}");
                ctx.Logger.LogDebug("✅ Applied ACL: {Path} -> {Identity} ({Rights})", path, acl.Identity, acl.Rights);
            }

            ctx.Logger.LogInformation("✅ ACL configuration completed");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogDebug("No rollback needed for ApplyAclStep");
            await Task.CompletedTask;
        }

        private static void ApplyIcacls(string path, string identity, string rights)
        {
            Directory.CreateDirectory(path);
            var flag = rights switch { "F" => "(OI)(CI)F", "R" => "(OI)(CI)R", _ => "(OI)(CI)M" };
            var psi = new System.Diagnostics.ProcessStartInfo("icacls", $"\"{path}\" /grant {identity}:{flag} /T")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                throw new InvalidOperationException($"ICACLS failed with exit code: {p.ExitCode}");
            }
        }
    }

    // 8) Features
    public sealed class ConfigureFeaturesStep : IDeployStep
    {
        public string Name => "ConfigureFeatures";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var f = ctx.Manifest.Features;
            if (f is null)
            {
                ctx.Log(PlanActionType.Noop, "no features");
                return;
            }

            ctx.Logger.LogInformation("⚙️ Configuring IIS features...");

            using var sm = new ServerManager();
            var site = IisLookup.GetTargetSite(sm, ctx);
            var config = sm.GetWebConfiguration(site.Name);

            // ----- REQUEST FILTERING: maxAllowedContentLength -----
            if (f.RequestFiltering?.MaxAllowedContentLength is long macl && macl > 0)
            {
                try
                {
                    var rf = config.GetSection("system.webServer/security/requestFiltering");
                    var limits = rf.GetChildElement("requestLimits");
                    limits["maxAllowedContentLength"] = macl; // byte

                    ctx.Log(PlanActionType.ConfigureFeatures,
                        $"requestLimits.maxAllowedContentLength={macl}");
                    ctx.Logger.LogDebug("✅ Configured request filtering: MaxContentLength={MaxContentLength}", macl);
                }
                catch (COMException ex)
                {
                    ctx.Log(PlanActionType.Noop,
                        $"skip requestFiltering (reason: {ex.Message})");
                    ctx.Logger.LogWarning("⚠️ Skipped request filtering: {Reason}", ex.Message);
                }
            }

            // ----- URL COMPRESSION: static/dynamic -----
            if (f.Compression is { } comp)
            {
                try
                {
                    var urlComp = config.GetSection("system.webServer/urlCompression");

                    urlComp["doStaticCompression"] = comp.Static;
                    urlComp["doDynamicCompression"] = comp.Dynamic;

                    ctx.Log(PlanActionType.ConfigureFeatures,
                        $"urlCompression: static={comp.Static}, dynamic={comp.Dynamic}");
                    ctx.Logger.LogDebug("✅ Configured compression: Static={Static}, Dynamic={Dynamic}", comp.Static, comp.Dynamic);
                }
                catch (COMException ex)
                {
                    ctx.Log(PlanActionType.Noop,
                        $"skip urlCompression (reason: {ex.Message})");
                    ctx.Logger.LogWarning("⚠️ Skipped compression: {Reason}", ex.Message);
                }
            }

            // ----- (Opsiyonel) URL REWRITE import (yalnızca seksiyon kontrol/log)
            if (!string.IsNullOrWhiteSpace(f.UrlRewriteImport))
            {
                try
                {
                    _ = config.GetSection("system.webServer/rewrite");
                    ctx.Log(PlanActionType.ConfigureFeatures,
                        $"urlRewrite: plan to import '{f.UrlRewriteImport}' (section present)");
                    ctx.Logger.LogInformation("✅ URL Rewrite section available for import");
                }
                catch (COMException ex)
                {
                    ctx.Log(PlanActionType.Noop,
                        $"skip urlRewrite import (reason: {ex.Message})");
                    ctx.Logger.LogWarning("⚠️ Skipped URL Rewrite: {Reason}", ex.Message);
                }
            }

            if (!ctx.DryRun)
                sm.CommitChanges();

            ctx.Log(PlanActionType.ConfigureFeatures, "features configured");
            ctx.Logger.LogInformation("✅ IIS features configuration completed");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogDebug("No rollback needed for ConfigureFeaturesStep");
            await Task.CompletedTask;
        }
    }

    // 9) Recycle + Start
    public sealed class RecycleAndStartStep : IDeployStep
    {
        public string Name => "RecycleAndStart";

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogInformation("🔄 Recycling AppPool and starting site...");

            if (ctx.DryRun)
            {
                ctx.Log(PlanActionType.Recycle, "(dry-run) recycle pool");
                ctx.Log(PlanActionType.StartSite, "(dry-run) start site");
                return;
            }

            using var sm = new ServerManager();
            var pool = sm.ApplicationPools[ctx.Manifest.AppPool.Name];
            try
            {
                pool.Recycle();
                ctx.Logger.LogInformation("✅ AppPool recycled: {AppPoolName}", pool.Name);
            }
            catch (Exception ex)
            {
                ctx.Logger.LogWarning(ex, "⚠️ AppPool recycle failed: {AppPoolName}", pool.Name);
            }

            var site = IisLookup.GetTargetSite(sm, ctx);
            if (site.State != ObjectState.Started)
            {
                site.Start();
                ctx.Logger.LogInformation("✅ Site started: {SiteName}", site.Name);
            }
            else
            {
                ctx.Logger.LogInformation("ℹ️ Site already started: {SiteName}", site.Name);
            }

            ctx.Log(PlanActionType.Recycle, $"recycled {pool.Name}");
            ctx.Log(PlanActionType.StartSite, $"started {site.Name}");
            await Task.CompletedTask;
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            ctx.Logger.LogDebug("No rollback needed for RecycleAndStartStep");
            await Task.CompletedTask;
        }
    }

    // 10) Health-check + advanced Blue/Green swap
    public sealed class HealthCheckAndSwapStep : IDeployStep
    {
        public string Name => "HealthCheckAndSwap";
        private readonly List<(string site, string proto, string info)> _swapped = new();
        private readonly List<(string site, string proto, string info, byte[]? hash, string? store)> _copiedToPassive = new();

        public async Task ExecuteAsync(DeployContext ctx, CancellationToken ct)
        {
            var bg = ctx.Manifest.BlueGreen;
            var urls = bg?.HealthUrls ?? new List<string>();
            if (urls.Count == 0)
            {
                ctx.Log(PlanActionType.Noop, "no health urls");
                return;
            }

            ctx.Logger.LogInformation("🏥 Starting health checks: {UrlCount} URLs", urls.Count);

            if (ctx.DryRun)
            {
                ctx.Log(PlanActionType.HealthCheck, $"(dry-run) {string.Join(", ", urls)}");
                if (ctx.UseBlueGreen) ctx.Log(PlanActionType.SwapBindings, "(dry-run) swap");
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(bg?.HealthTimeoutSeconds ?? 15) };
            foreach (var url in urls)
            {
                ctx.Logger.LogInformation("🔍 Health checking: {Url}", url);
                var ok = await WaitHealthy(http, url, bg?.HealthAttempts ?? 10, ct);
                if (!ok) throw new InvalidOperationException($"health failed: {url}");
                ctx.Logger.LogInformation("✅ Health check passed: {Url}", url);
            }
            ctx.Log(PlanActionType.HealthCheck, "all 200");
            ctx.Logger.LogInformation("🎉 All health checks passed");

            if (ctx.UseBlueGreen)
            {
                ctx.Logger.LogInformation("🔵🟢 Starting Blue/Green swap...");
                using var sm = new ServerManager();
                var (active, passive) = ResolveBlueGreen(sm, ctx.Manifest.SiteName, bg!.ActiveSuffix, bg.SlotSuffix);

                if (bg.StopActiveBeforeSwap && active.State == ObjectState.Started)
                {
                    active.Stop();
                    ctx.Logger.LogInformation("⏹️ Stopped active site: {SiteName}", active.Name);
                }

                // BG bootstrap: iki tarafta da public binding yoksa, manifest'teki public bindingleri ilk kez PASİF'e ata
                var activePub = active.Bindings.Count(b => b.Protocol is "http" or "https");
                var passivePub = passive.Bindings.Count(b => b.Protocol is "http" or "https");
                if (activePub == 0 && passivePub == 0)
                {
                    ctx.Logger.LogInformation("🚀 Blue/Green bootstrap: assigning public bindings to passive slot");
                    foreach (var binding in ctx.Manifest.Bindings)
                    {
                        var info = $"{binding.Ip}:{binding.Port}:{binding.Host}";
                        if (binding.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrWhiteSpace(binding.CertThumbprint))
                            {
                                if (ctx.Manifest.AllowMissingCertificates || binding.SkipIfCertMissing)
                                {
                                    ctx.Log(PlanActionType.Noop, $"bootstrap skip https {info} (no thumbprint)");
                                    continue;
                                }
                                throw new InvalidOperationException($"HTTPS binding for {info} needs certThumbprint.");
                            }

                            var cert = FindCert(binding.CertThumbprint!, binding.CertStore);
                            if (cert is null)
                            {
                                if (ctx.Manifest.AllowMissingCertificates || binding.SkipIfCertMissing)
                                {
                                    ctx.Log(PlanActionType.Noop, $"bootstrap skip https {info} (cert not found in {binding.CertStore})");
                                    continue;
                                }
                                throw new InvalidOperationException($"Certificate {binding.CertThumbprint} not found in store {binding.CertStore}");
                            }
                            passive.Bindings.Add(info, cert.GetCertHash(), binding.CertStore);
                        }
                        else
                        {
                            passive.Bindings.Add(info, "http");
                        }
                    }
                    sm.CommitChanges();
                    ctx.Log(PlanActionType.SwapBindings, "BG bootstrap: assigned public bindings to passive (first-time)");
                    ctx.Logger.LogInformation("✅ Blue/Green bootstrap completed");
                    return;
                }

                // Normal swap: order (https -> http default)
                ctx.Logger.LogInformation("🔄 Performing Blue/Green swap with protocol order: {Protocols}",
                    string.Join(" -> ", NormalizeOrder(bg.SwapProtocolOrder)));

                foreach (var proto in NormalizeOrder(bg.SwapProtocolOrder))
                    SwapProtocol(active, passive, proto, sm, _swapped, _copiedToPassive, bg.PreserveOldBindingsForRollback);

                sm.CommitChanges();
                ctx.Log(PlanActionType.SwapBindings, $"swapped {active.Name} -> {passive.Name}");
                ctx.Logger.LogInformation("✅ Blue/Green swap completed: {ActiveSite} -> {PassiveSite}", active.Name, passive.Name);
            }
        }

        public async Task RollbackAsync(DeployContext ctx, CancellationToken ct)
        {
            if (ctx.DryRun || (!ctx.UseBlueGreen)) return;

            ctx.Logger.LogWarning("↩ Rolling back Blue/Green swap...");

            using var sm = new ServerManager();

            if (ctx.Manifest.BlueGreen!.PreserveOldBindingsForRollback && _copiedToPassive.Count > 0)
            {
                ctx.Logger.LogInformation("🔄 Removing bindings from passive slot...");
                foreach (var (site, proto, info, _, _) in _copiedToPassive)
                {
                    var s = sm.Sites[site];
                    var b = s.Bindings.FirstOrDefault(x => x.Protocol == proto && x.BindingInformation == info);
                    if (b is not null) s.Bindings.Remove(b);
                }
                sm.CommitChanges();
                ctx.Logger.LogInformation("✅ Blue/Green swap rollback completed");
                return;
            }

            if (_swapped.Count > 0)
            {
                ctx.Logger.LogInformation("🔄 Reverting binding swaps...");
                foreach (var (siteName, proto, info) in _swapped)
                {
                    var site = sm.Sites[siteName];
                    var b = site.Bindings.FirstOrDefault(x => x.Protocol == proto && x.BindingInformation == info);
                    if (b is not null) site.Bindings.Remove(b);
                }
                sm.CommitChanges();
                ctx.Logger.LogInformation("✅ Blue/Green swap rollback completed");
            }
            await Task.CompletedTask;
        }

        private static async Task<bool> WaitHealthy(HttpClient http, string url, int attempts, CancellationToken ct)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    var response = await http.GetAsync(url, ct);
                    if ((int)response.StatusCode is >= 200 and < 300)
                        return true;
                }
                catch (Exception ex)
                {
                    // Log the exception but continue retrying
                    System.Diagnostics.Debug.WriteLine($"Health check attempt {i + 1} failed: {ex.Message}");
                }
                await Task.Delay(1000, ct);
            }
            return false;
        }

        private static (Site active, Site passive) ResolveBlueGreen(ServerManager sm, string baseName, string activeSuffix, string passiveSuffix)
        {
            var activeSite = sm.Sites.FirstOrDefault(s => s.Name == baseName + activeSuffix);
            var passiveSite = sm.Sites.FirstOrDefault(s => s.Name == baseName + passiveSuffix);
            if (activeSite is null || passiveSite is null)
                throw new InvalidOperationException("blue/green sites not found");

            var activeScore = activeSite.Bindings.Count(bd => bd.Protocol is "https" or "http");
            var passiveScore = passiveSite.Bindings.Count(bd => bd.Protocol is "https" or "http");
            return (activeScore >= passiveScore) ? (activeSite, passiveSite) : (passiveSite, activeSite);
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
            foreach (var binding in toMove)
            {
                var info = binding.BindingInformation;

                if (proto == "https")
                {
                    passive.Bindings.Add(info, binding.CertificateHash, binding.CertificateStoreName);
                    copiedToPassive.Add((passive.Name, "https", info, binding.CertificateHash, binding.CertificateStoreName));
                }
                else
                {
                    passive.Bindings.Add(info, "http");
                    copiedToPassive.Add((passive.Name, "http", info, null, null));
                }

                if (!preserveOldBindingsForRollback)
                {
                    removedFromActive.Add((active.Name, proto, info));
                    active.Bindings.Remove(binding);
                }
            }
        }

        private static X509Certificate2? FindCert(string thumb, string storeName)
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var t = thumb.Replace(" ", string.Empty).ToUpperInvariant();
            return store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.Thumbprint?.ToUpperInvariant() == t);
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
            _logger.LogInformation("📋 Building deployment plan...");

            var manifest = SiteManifest.FromJson(manifestJson);
            var plan = new DeploymentPlan { UseBlueGreen = manifest.BlueGreen?.Enabled == true };

            plan.Add(PlanActionType.DeployContentAtomicSwitch, "Extract zip to versioned folder");
            plan.Add(PlanActionType.EnsureAppPool, $"Ensure AppPool {manifest.AppPool.Name}");
            plan.Add(PlanActionType.EnsureSite, $"Ensure Site {(plan.UseBlueGreen ? $"{manifest.SiteName}{manifest.BlueGreen!.ActiveSuffix}/{manifest.SiteName}{manifest.BlueGreen!.SlotSuffix}" : manifest.SiteName)}");
            plan.Add(PlanActionType.EnsureApplications, $"Ensure {manifest.Applications.Count} application(s)");
            plan.Add(PlanActionType.EnsureBindings, "Ensure bindings (http/https + certificate)");

            if (manifest.Env?.Count > 0)
                plan.Add(PlanActionType.SetEnv, $"Set {manifest.Env.Count} environment variables");

            if (manifest.Acls?.Count > 0)
                plan.Add(PlanActionType.ApplyAcl, $"Apply {manifest.Acls.Count} ACL entries");

            if (manifest.Features is not null)
                plan.Add(PlanActionType.ConfigureFeatures, "Configure features");

            plan.Add(plan.UseBlueGreen ? PlanActionType.DeployBlueGreenMirror : PlanActionType.DeployContentAtomicSwitch,
                     plan.UseBlueGreen ? "Mirror to passive slot" : "Atomic switch (root -> version dir)");
            plan.Add(PlanActionType.Recycle, "Recycle AppPool");
            plan.Add(PlanActionType.StartSite, "Start Site");

            if (manifest.BlueGreen?.HealthUrls?.Count > 0)
                plan.Add(PlanActionType.HealthCheck, "Health-check URLs");

            if (plan.UseBlueGreen)
                plan.Add(PlanActionType.SwapBindings, $"Swap public bindings ({string.Join("→", (manifest.BlueGreen?.SwapProtocolOrder ?? new() { "https", "http" }).Select(x => x.ToUpper()))})");

            _logger.LogInformation("✅ Deployment plan built: {ActionCount} actions, BlueGreen: {UseBlueGreen}",
                plan.Actions.Count, plan.UseBlueGreen);

            return plan;
        }

        public async Task<List<PlanAction>> DryRunAsync(string zipPath, string manifestJson, string baseDeployDir, CancellationToken ct = default)
        {
            _logger.LogInformation("🔍 Starting dry-run deployment...");

            var ctx = new DeployContext
            {
                ZipPath = zipPath,
                Manifest = SiteManifest.FromJson(manifestJson),
                BaseDeployDir = baseDeployDir,
                DryRun = true,
                Logger = _logger
            };

            var steps = BuildAtomicScopes(ctx);
            await new PipelineEngine(_logger).RunAsync(steps, ctx, ct);

            _logger.LogInformation("📊 Dry-run completed: {ActionCount} actions simulated", ctx.Executed.Count);
            return ctx.Executed;
        }

        public async Task<List<PlanAction>> ApplyAsync(string zipPath, string manifestJson, string baseDeployDir, CancellationToken ct = default)
        {
            _logger.LogInformation("🚀 Starting actual deployment...");

            var ctx = new DeployContext
            {
                ZipPath = zipPath,
                Manifest = SiteManifest.FromJson(manifestJson),
                BaseDeployDir = baseDeployDir,
                DryRun = false,
                Logger = _logger
            };

            var steps = BuildAtomicScopes(ctx);
            await new PipelineEngine(_logger).RunAsync(steps, ctx, ct);

            _logger.LogInformation("🎉 Deployment completed successfully: {ActionCount} actions executed", ctx.Executed.Count);
            return ctx.Executed;
        }

        private static IEnumerable<IDeployStep> BuildAtomicScopes(DeployContext ctx)
        {
            // Scope 1: Validation + İçerik hazırlığı
            var scopeContent = new AtomicScopeStep("Content", new IDeployStep[]
            {
                new ValidatePrerequisitesStep(),
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
                //new HealthCheckAndSwapStep()
            });

            return new IDeployStep[] { scopeContent, scopeIis, scopeConfig, scopeActivation };
        }
    }

    public sealed class DeploymentPlan
    {
        public bool UseBlueGreen { get; init; }
        public List<PlanAction> Actions { get; } = new();
        public void Add(PlanActionType type, string description) => Actions.Add(new PlanAction(type, description, DateTime.UtcNow));
    }
    #endregion
}