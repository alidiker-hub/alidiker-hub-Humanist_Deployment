using System.Text.Json.Serialization;

namespace DeploymentApp2
{
    public enum RuntimeVersion
    {
        V20,        // "v2.0"
        V40,        // "v4.0"
        NoManaged   // ""
    }

    public enum PipelineMode
    {
        Integrated,
        Classic
    }

    public enum IdentityType
    {
        ApplicationPoolIdentity,
        NetworkService,
        LocalSystem,
        CustomAccount
    }

    public enum ProtocolType
    {
        Http,
        Https
    }

    public enum BuildConfiguration
    {
        Release,
        Debug,
        Custom
    }

    public class AppPoolConfig
    {
        public string Name { get; set; } = "DefaultAppPool";
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RuntimeVersion RuntimeVersion { get; set; } = RuntimeVersion.V40;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PipelineMode Pipeline { get; set; } = PipelineMode.Integrated;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IdentityType Identity { get; set; } = IdentityType.ApplicationPoolIdentity;
    }

    public class BindingConfig
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProtocolType Protocol { get; set; } = ProtocolType.Http;
        public int Port { get; set; } = 80;
        public string Host { get; set; } = "localhost";
    }

    public class PublishDefaults
    {
        public BuildConfiguration BuildConfiguration { get; set; } = BuildConfiguration.Release;
        public bool UseAppOffline { get; set; } = true;
        public string OutputPath { get; set; } = @"C:\deploy\output";
        public List<string> ZipExtractionFilter { get; set; } = new() { "*.dll", "wwwroot/*" };
    }

    public class AppConfig
    {
        public List<SiteConfig> Sites { get; set; } = new();
        public PublishDefaults Publish { get; set; } = new();
    }

    public class SiteConfig
    {
        public string Name { get; set; } = "DefaultSite";
        public string PhysicalPath { get; set; } = @"C:\inetpub\wwwroot\DefaultSite";

        public BindingConfig Binding { get; set; } = new();
        public SslConfig Ssl { get; set; } = new();
        public AppPoolConfig AppPool { get; set; } = new();

        public List<ApplicationConfig> Applications { get; set; } = new();
    }

    public class ApplicationConfig
    {
        public string Path { get; set; } = "/";
        public string PhysicalPath { get; set; } = @"C:\inetpub\wwwroot\App";
        public AppPoolConfig AppPool { get; set; } = new();
    }

    public class SslConfig
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 443;
        public string? Thumbprint { get; set; }   // sistemden doldurulacak
    }


    namespace DeploymentConfig
    {
        public class AppConfig
        {
            public IisDefaults Iis { get; set; } = new();
            public List<SiteConfig> Sites { get; set; } = new();
            public PublishDefaults Publish { get; set; } = new();
        }

        public class IisDefaults
        {
            public BindingConfig DefaultBinding { get; set; } = new();
            public SslConfig DefaultSsl { get; set; } = new();
            public AppPoolDefaults DefaultAppPool { get; set; } = new();
        }

        public class BindingConfig
        {
            public string Protocol { get; set; } = "http";
            public int Port { get; set; } = 80;
            public string? Host { get; set; }
        }

        public class SslConfig
        {
            public bool Enabled { get; set; } = false;
            public int Port { get; set; } = 443;
            public string? Thumbprint { get; set; }
        }

        public class AppPoolDefaults
        {
            public string RuntimeVersion { get; set; } = "v4.0";
            public string Pipeline { get; set; } = "Integrated";
            public string Identity { get; set; } = "ApplicationPoolIdentity";
        }

        public class SiteConfig
        {
            public string Name { get; set; } = "DefaultSite";
            public string PhysicalPath { get; set; } = @"C:\inetpub\wwwroot\DefaultSite";
            public BindingConfig? Binding { get; set; }
            public SslConfig? Ssl { get; set; }
            public AppPoolConfig AppPool { get; set; } = new();
            public List<ApplicationConfig> Applications { get; set; } = new();
        }

        public class AppPoolConfig
        {
            public string Name { get; set; } = "DefaultAppPool";
            public string? RuntimeVersion { get; set; }    // override edilebilir
            public string? Pipeline { get; set; }
            public string? Identity { get; set; }
        }

        public class ApplicationConfig
        {
            public string Path { get; set; } = "/";
            public string PhysicalPath { get; set; } = @"C:\inetpub\wwwroot\App";
            public AppPoolConfig AppPool { get; set; } = new();
        }

        public class PublishDefaults
        {
            public string BuildConfiguration { get; set; } = "Release";
            public bool UseAppOffline { get; set; } = true;
            public string OutputPath { get; set; } = @"C:\deploy\output";
            public List<string> ZipExtractionFilter { get; set; } = new() { "*.dll", "wwwroot/*" };
        }
    }

}
