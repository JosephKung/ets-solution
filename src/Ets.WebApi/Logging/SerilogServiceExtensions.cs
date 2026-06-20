using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Ets.WebApi.Logging;

/// <summary>
/// Serilog 結構化日誌設定擴充方法（WebApi 層）。
/// 對應規格書 §13.2 結構化日誌設計。
/// </summary>
public static class SerilogServiceExtensions
{
    /// <summary>
    /// 設定 Serilog 並取代 .NET 內建 ILogger。
    /// 在 WebApplicationBuilder 建立後、app.Run() 之前呼叫。
    /// </summary>
    public static WebApplicationBuilder AddEtsSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((ctx, services, loggerConfig) =>
        {
            loggerConfig
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("Service", "Ets.WebApi")
                .Enrich.WithProperty("Version",
                    typeof(SerilogServiceExtensions).Assembly.GetName().Version?.ToString() ?? "unknown")
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
                    ctx.HostingEnvironment.IsDevelopment()
                        ? LogEventLevel.Information
                        : LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Polly", LogEventLevel.Warning)
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services);

            if (ctx.HostingEnvironment.IsDevelopment())
            {
                loggerConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                loggerConfig.WriteTo.Console(new CompactJsonFormatter());
            }

            loggerConfig.WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: Path.Combine("logs", "ets-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 100 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(5));
        });

        return builder;
    }
}
