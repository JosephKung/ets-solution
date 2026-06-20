// tests/Ets.IntegrationTests/Infrastructure/EtsWebApplicationFactory.cs
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Domain.Enums;
using Ets.Infrastructure.BackgroundServices;
using Ets.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ets.Application.Abstractions;
using NSubstitute;

namespace Ets.IntegrationTests.Infrastructure;

/// <summary>
/// M3 整合測試用 WebApplicationFactory
///
/// 設計決策：
/// - DB：替換為 In-Memory（隔離，每個測試獨立）
/// - Background Workers：停用（避免干擾測試）
/// - 外部 API Client（team+）：替換為 Mock
/// - SignalR Notifier：替換為 Mock
/// </summary>
public sealed class EtsWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>可從測試中設定 mock 行為</summary>
    public ITeamPlusSystemClient MockSystemClient { get; } =
        Substitute.For<ITeamPlusSystemClient>();
    public ITeamPlusChannelClient MockChannelClient { get; } =
        Substitute.For<ITeamPlusChannelClient>();
    public IDashboardNotifier MockNotifier { get; } =
        Substitute.For<IDashboardNotifier>();

    public IIpWhitelistService IpWhitelistService { get; } =
        Substitute.For<IIpWhitelistService>();
    public IAreaWhitelistService AreaWhitelistService { get; } =
        Substitute.For<IAreaWhitelistService>();

    private readonly string _dbName = $"EtsIntTest_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // ── 替換 AppDbContext → In-Memory ─────────────────────
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // ── 停用所有 BackgroundService（Worker）──────────────
            services.RemoveAll<IHostedService>();

            // ── 替換外部 API Client 為 Mock ────────────────────────
            services.RemoveAll<ITeamPlusSystemClient>();
            services.AddScoped<ITeamPlusSystemClient>(_ => MockSystemClient);

            services.RemoveAll<ITeamPlusChannelClient>();
            services.AddScoped<ITeamPlusChannelClient>(_ => MockChannelClient);

            // ── 替換 SignalR Notifier 為 Mock ──────────────────────
            services.RemoveAll<IDashboardNotifier>();
            services.AddScoped<IDashboardNotifier>(_ => MockNotifier);

            // ── 替換 Whitelist Services 為 Mock ────────────────────
            services.RemoveAll<IIpWhitelistService>();
            services.AddSingleton<IIpWhitelistService>(_ => IpWhitelistService);

            services.RemoveAll<IAreaWhitelistService>();
            services.AddSingleton<IAreaWhitelistService>(_ => AreaWhitelistService);

            // ── 設定測試用環境變數（AES Key）─────────────────────
            Environment.SetEnvironmentVariable(
                "ETS_VIRTUAL_ACCOUNT_KEY",
                Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        });
    }

    /// <summary>
    /// 取得一個已有資料的 DbContext scope（用於 test arrange / assert）
    /// </summary>
    public AppDbContext GetDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }
}
