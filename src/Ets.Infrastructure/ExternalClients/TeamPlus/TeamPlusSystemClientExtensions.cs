// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusSystemClientExtensions.cs
using Ets.Application.Interfaces.External;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Infrastructure.ExternalClients.TeamPlus;

/// <summary>
/// ITeamPlusSystemClient DI 註冊擴充方法
/// 於 Program.cs 呼叫：builder.Services.AddTeamPlusSystemClient(builder.Configuration);
/// </summary>
public static class TeamPlusSystemClientExtensions
{
    public static IServiceCollection AddTeamPlusSystemClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 綁定 appsettings → TeamPlusSystemOptions
        services.Configure<TeamPlusSystemOptions>(
            configuration.GetSection(TeamPlusSystemOptions.SectionName));

        var section = configuration.GetSection(TeamPlusSystemOptions.SectionName);

        var baseUrl = section["BaseUrl"]
            ?? throw new InvalidOperationException(
                "TeamPlusSystem:BaseUrl 未設定，請檢查 appsettings.json");

        var timeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var t) ? t : 10;

        // 註冊 Typed HttpClient，Polly Pipeline 由 ResiliencePipelineKeys.TeamPlus 管理（M1 已建立）
        services.AddHttpClient<ITeamPlusSystemClient, TeamPlusSystemClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout     = TimeSpan.FromSeconds(timeoutSeconds);
        });

        return services;
    }
}
