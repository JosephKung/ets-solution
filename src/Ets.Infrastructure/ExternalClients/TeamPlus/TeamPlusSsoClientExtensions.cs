// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusSsoClientExtensions.cs
using Ets.Application.Interfaces.External;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Infrastructure.ExternalClients.TeamPlus;

/// <summary>
/// ITeamPlusSsoClient DI 註冊擴充方法
/// 於 Program.cs 呼叫：builder.Services.AddTeamPlusSsoClient(builder.Configuration);
/// </summary>
public static class TeamPlusSsoClientExtensions
{
    public static IServiceCollection AddTeamPlusSsoClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TeamPlusSsoOptions>(
            configuration.GetSection(TeamPlusSsoOptions.SectionName));

        var section = configuration.GetSection(TeamPlusSsoOptions.SectionName);

        var baseUrl = section["BaseUrl"]
            ?? throw new InvalidOperationException(
                "TeamPlusSso:BaseUrl 未設定，請檢查 appsettings.json");

        var timeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var t) ? t : 10;

        services.AddHttpClient<ITeamPlusSsoClient, TeamPlusSsoClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout     = TimeSpan.FromSeconds(timeoutSeconds);
        });

        return services;
    }
}
