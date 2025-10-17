// ===========================================================
// ===============  ADD: Manifest Models  ====================
// ===========================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Humanist.Deployer
{
    #region Manifest Models (Single-site with multisite-capable loader)

    public sealed class Manifest
    {
        // --- Required ---
        [JsonPropertyName("siteName")] public string SiteName { get; set; } = default!;
        [JsonPropertyName("physicalPath")] public string PhysicalPathInZip { get; set; } = ".";

        // --- Optional (global) ---
        [JsonPropertyName("variables")] public Dictionary<string, string>? Variables { get; set; }

        // Shared pools (çok site kullanırsan)
        [JsonPropertyName("appPools")] public List<AppPoolDef>? AppPools { get; set; }

        // --- Site level ---
        [JsonPropertyName("bindings")] public List<BindingDef> Bindings { get; set; } = new();
        [JsonPropertyName("appPool")] public AppPoolDef AppPool { get; set; } = new();
        [JsonPropertyName("applications")] public List<ApplicationDef> Applications { get; set; } = new();
        [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }
        [JsonPropertyName("acl")] public List<AclDef>? Acls { get; set; }
        [JsonPropertyName("features")] public FeaturesDef? Features { get; set; }
        [JsonPropertyName("warmup")] public WarmupDef? Warmup { get; set; }
        [JsonPropertyName("health")] public HealthDef? Health { get; set; }
        [JsonPropertyName("blueGreen")] public BlueGreenDef? BlueGreen { get; set; }

        // Sertifika eksikse https binding'lerini atlayıp ilerle
        [JsonPropertyName("allowMissingCertificates")] public bool AllowMissingCertificates { get; set; } = false;

        // Loader: tek site veya çok site JSON’larını destekler (engine tek site çalışır)
        public static Manifest FromJson(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            // Çok site desteği: kökte "sites": [...]
            if (doc.RootElement.TryGetProperty("sites", out var sites) &&
                sites.ValueKind == JsonValueKind.Array)
            {
                if (sites.GetArrayLength() == 0)
                    throw new InvalidOperationException("sites[] boş olamaz.");

                var firstSiteJson = sites[0].GetRawText();

                var site = JsonSerializer.Deserialize<Manifest>(firstSiteJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                })!;

                // Kökteki global appPools/variables varsa, tek site manifestine kopyala
                if (doc.RootElement.TryGetProperty("appPools", out var gpools))
                    site.AppPools = JsonSerializer.Deserialize<List<AppPoolDef>>(gpools.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (doc.RootElement.TryGetProperty("variables", out var gvars))
                    site.Variables = JsonSerializer.Deserialize<Dictionary<string, string>>(gvars.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return site;
            }

            // Tek site
            return JsonSerializer.Deserialize<Manifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            })!;
        }
    }

    public sealed class BindingDef
    {
        [JsonPropertyName("protocol")] public string Protocol { get; set; } = "http"; // http | https
        [JsonPropertyName("ip")] public string Ip { get; set; } = "*";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("certThumbprint")] public string? CertThumbprint { get; set; }
        [JsonPropertyName("certStore")] public string CertStore { get; set; } = "My";
        [JsonPropertyName("skipIfCertMissing")] public bool SkipIfCertMissing { get; set; } = false;
    }

    public sealed class AppPoolDef
    {
        [JsonPropertyName("name")] public string Name { get; set; } = default!;
        [JsonPropertyName("runtimeVersion")] public string RuntimeVersion { get; set; } = "v4.0"; // "NoManagedCode" for ASP.NET Core
        [JsonPropertyName("pipelineMode")] public string PipelineMode { get; set; } = "Integrated"; // Classic | Integrated

        // Optional extras
        [JsonPropertyName("basedOn")] public string? BasedOn { get; set; } // global AppPools içinden kalıtım için isim
        [JsonPropertyName("enable32Bit")] public bool Enable32Bit { get; set; } = false;
        [JsonPropertyName("autoStart")] public bool AutoStart { get; set; } = true;
        [JsonPropertyName("alwaysRunning")] public bool AlwaysRunning { get; set; } = false;
        [JsonPropertyName("startMode")] public string StartMode { get; set; } = "OnDemand"; // OnDemand | AlwaysRunning

        [JsonPropertyName("identity")] public AppPoolIdentityDef Identity { get; set; } = new();
        [JsonPropertyName("recycle")] public AppPoolRecycleDef Recycle { get; set; } = new();
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
        [JsonPropertyName("requests")] public int Requests { get; set; } = 0;
        [JsonPropertyName("specificTimes")] public List<string>? SpecificTimes { get; set; }
    }

    public sealed class ApplicationDef
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "/";                       // IIS app path
        [JsonPropertyName("physicalPath")] public string PhysicalPathInZip { get; set; } = ".";  // ZIP içindeki göreli klasör

        // App-pool seçimi (opsiyonel)
        [JsonPropertyName("appPool")] public string? AppPool { get; set; }                       // var olan bir pool adı
        [JsonPropertyName("appPoolOverride")] public AppPoolDef? AppPoolOverride { get; set; }   // inline override (öncelikli)

        // Advanced (opsiyonel)
        [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }
        [JsonPropertyName("virtualDirectories")] public List<VDirDef>? VirtualDirectories { get; set; }
        [JsonPropertyName("webConfigTransforms")] public List<string>? WebConfigTransforms { get; set; }
    }

    public sealed class VDirDef
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "/content";
        [JsonPropertyName("physicalPath")] public string PhysicalPathInZip { get; set; } = ".";
        [JsonPropertyName("appPool")] public string? AppPool { get; set; } // nadir: ayrı pool istenirse
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

        // Opsiyoneller
        [JsonPropertyName("authentication")] public AuthenticationDef? Authentication { get; set; }
        [JsonPropertyName("staticContent")] public StaticContentDef? StaticContent { get; set; }
        [JsonPropertyName("defaultDocument")] public DefaultDocumentDef? DefaultDocument { get; set; }
        [JsonPropertyName("logging")] public LoggingDef? Logging { get; set; }
    }

    public sealed class RequestFilteringDef
    {
        [JsonPropertyName("maxAllowedContentLength")] public long? MaxAllowedContentLength { get; set; }
        [JsonPropertyName("verbs")] public List<string>? Verbs { get; set; }
        [JsonPropertyName("fileExtensionsDeny")] public List<string>? FileExtensionsDeny { get; set; }
    }

    public sealed class CompressionDef
    {
        [JsonPropertyName("static")] public bool Static { get; set; } = true;
        [JsonPropertyName("dynamic")] public bool Dynamic { get; set; } = true;
    }

    public sealed class AuthenticationDef
    {
        [JsonPropertyName("anonymous")] public ToggleDef Anonymous { get; set; } = new() { Enabled = true };
        [JsonPropertyName("windows")] public ToggleDef Windows { get; set; } = new() { Enabled = false };
        [JsonPropertyName("basic")] public ToggleDef Basic { get; set; } = new() { Enabled = false };
    }

    public sealed class ToggleDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    }

    public sealed class StaticContentDef
    {
        [JsonPropertyName("mimeMap")] public List<MimeMapDef>? MimeMap { get; set; }
    }

    public sealed class MimeMapDef
    {
        [JsonPropertyName("fileExtension")] public string FileExtension { get; set; } = "";
        [JsonPropertyName("mimeType")] public string MimeType { get; set; } = "";
    }

    public sealed class DefaultDocumentDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("files")] public List<string>? Files { get; set; }
    }

    public sealed class LoggingDef
    {
        [JsonPropertyName("httpLogging")] public HttpLoggingDef HttpLogging { get; set; } = new();
        [JsonPropertyName("failedRequestTracing")] public FailedRequestTracingDef FailedRequestTracing { get; set; } = new();
    }

    public sealed class HttpLoggingDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("directory")] public string? Directory { get; set; }
    }

    public sealed class FailedRequestTracingDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;
    }

    public sealed class WarmupDef
    {
        [JsonPropertyName("applicationInitialization")] public AppInitDef ApplicationInitialization { get; set; } = new();
        [JsonPropertyName("preload")] public bool Preload { get; set; } = true;
        [JsonPropertyName("warmupUrls")] public List<string>? WarmupUrls { get; set; }
        [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; } = 20;
    }

    public sealed class AppInitDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("doAppInitAfterRestart")] public bool DoAppInitAfterRestart { get; set; } = true;
    }

    public sealed class HealthDef
    {
        [JsonPropertyName("urls")] public List<string>? Urls { get; set; }
        [JsonPropertyName("attempts")] public int Attempts { get; set; } = 6;
        [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; } = 15;
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
}
