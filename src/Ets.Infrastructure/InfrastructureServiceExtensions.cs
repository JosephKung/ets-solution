using Ets.Application.Abstractions;
using Ets.Infrastructure.HealthChecks;
using Ets.Infrastructure.Logging;
using Ets.Infrastructure.Persistence;
using Ets.Infrastructure.Resilience;
using Ets.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Infrastructure;

/// <summary>
/// Infrastructure 層 DI 服務註冊擴充方法。
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── 1.1.7：Secret Manager + Options ──────────────────────────────
        services.Configure<DpapiProtectedConfigurationOptions>(
            configuration.GetSection(DpapiProtectedConfigurationOptions.SectionName));
        services.Configure<TeamPlusChannelsOptions>(
            configuration.GetSection(TeamPlusChannelsOptions.SectionName));
        services.Configure<TeamPlusSystemOptions>(
            configuration.GetSection(TeamPlusSystemOptions.SectionName));
        services.AddSingleton<ISecretManager, DpapiSecretManager>();

        // ── 1.2.1：IP 白名單 ──────────────────────────────────────────────
        services.Configure<SecurityOptions>(
            configuration.GetSection(SecurityOptions.SectionName));
        services.AddSingleton<IIpWhitelistService, IpWhitelistService>();

        // ── 1.2.7：Area Whitelist（IHostedService + IAreaWhitelistService）
        services.Configure<AreaWhitelistOptions>(
            configuration.GetSection(AreaWhitelistOptions.SectionName));
        // 以 Singleton 同時做為 IHostedService 與 IAreaWhitelistService
        services.AddSingleton<AreaWhitelistService>();
        services.AddSingleton<IAreaWhitelistService>(sp =>
            sp.GetRequiredService<AreaWhitelistService>());
        services.AddHostedService(sp =>
            sp.GetRequiredService<AreaWhitelistService>());

        // ── 1.1.2：EF Core DbContext ──────────────────────────────────────
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql =>
                {
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // ── 1.2.3~1.2.5：Repository + Unit of Work ───────────────────────
        services.AddScoped<IEtsRepository, EtsRepository>();
        services.AddScoped<IUnitOfWork, EtsUnitOfWork>();

        // ── 1.1.4：Polly Resilience ───────────────────────────────────────
        services.AddEtsResiliencePipelines(configuration);

        // ── 1.1.5：業務 Metrics ───────────────────────────────────────────
        services.AddSingleton<EtsMetrics>();

        // ── 1.1.6：Health Checks ──────────────────────────────────────────
        services.AddEtsHealthChecks(configuration);

        return services;
    }
}
