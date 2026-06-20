using Ets.Infrastructure.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ets.Infrastructure.HealthChecks;

/// <summary>
/// Health Checks 服務註冊擴充方法。
/// 對應規格書 §13.5 三個端點：live / ready / startup。
///
/// Tag 規則（決定哪個端點回應哪些 check）：
///   live    → 無 tag（只要程序存活即可）
///   ready   → tag = "ready"（DB、Outbox 深度）
///   startup → tag = "startup"（DB 連線可用即可啟動）
/// </summary>
public static class HealthCheckServiceExtensions
{
    public static IServiceCollection AddEtsHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("DefaultConnection")
                   ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");

        services
            .AddHealthChecks()
            // ── SQL Server（ready + startup 兩用）──────────────────────────
            .AddSqlServer(
                connectionString: connStr,
                healthQuery:      "SELECT 1",
                name:             "mssql",
                failureStatus:    HealthStatus.Unhealthy,
                tags:             new[] { "ready", "startup" })
            // ── Outbox 佇列深度（僅 ready）──────────────────────────────────
            .AddCheck<OutboxQueueDepthHealthCheck>(
                name:          "outbox-depth",
                failureStatus: HealthStatus.Unhealthy,
                tags:          new[] { "ready" });

        return services;
    }
}
