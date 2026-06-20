// tests/Ets.IntegrationTests/Infrastructure/IntegrationTestBase.cs
using Ets.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ets.IntegrationTests.Infrastructure;

/// <summary>
/// M3 整合測試基礎類別
/// 提供標準的 Factory、HttpClient、DB 存取，以及資料 Seed
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected EtsWebApplicationFactory Factory { get; }
    protected HttpClient Client { get; }

    protected IntegrationTestBase()
    {
        Factory = new EtsWebApplicationFactory();
        Client  = Factory.CreateClient();
        TestDataBuilder.SetupMockSystemClientDefaults(Factory.MockSystemClient);
        TestDataBuilder.SetupMockChannelClientDefaults(Factory.MockChannelClient);
    }

    /// <summary>每個測試開始前 seed 標準測試資料</summary>
    public virtual async Task InitializeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<Ets.Infrastructure.Persistence.AppDbContext>();
        await TestDataBuilder.SeedStandardEventAsync(db);
    }

    public virtual Task DisposeAsync()
    {
        Factory.Dispose();
        Client.Dispose();
        return Task.CompletedTask;
    }
}
