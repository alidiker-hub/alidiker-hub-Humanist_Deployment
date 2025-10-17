using DeploymentApp2.Components;
using DeploymentApp2.Logging;
using Humanist.Deployer;
using Microsoft.Extensions.Logging;

namespace DeploymentApp2
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Logging: Console + UI sink
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddSingleton<UiLogSink>();
            builder.Logging.AddProvider(new UiLoggerProvider(
                sink: builder.Services.BuildServiceProvider().GetRequiredService<UiLogSink>(),
                minLevel: LogLevel.Information));

            // Gürültülü kaynakları sustur
            builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer", LogLevel.Warning);

            // Blazor
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents()
                .AddCircuitOptions(options =>
                {
                    options.DetailedErrors = true;
                    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(5);
                });

            // App services
            builder.Services.AddScoped<DeploymentService>();

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAntiforgery();

            app.MapGet("/health", () => Results.Ok("OK"));
            app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
