// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusChannelClientExtensions.cs
using Ets.Application.Interfaces.External;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Infrastructure.ExternalClients.TeamPlus;

/// <summary>
/// ITeamPlusChannelClient DI 註冊擴充方法
/// 於 Program.cs 呼叫：builder.Services.AddTeamPlusChannelClient(builder.Configuration);
/// </summary>
public static class TeamPlusChannelClientExtensions
{
    public static IServiceCollection AddTeamPlusChannelClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 綁定 appsettings.json → TeamPlusChannelsOptions
        // 注意：appsettings 中 "TeamPlusChannels" 為 Dictionary，需自訂綁定
        var channelsSection = configuration.GetSection(TeamPlusChannelsOptions.SectionName);
        services.Configure<TeamPlusChannelsOptions>(options =>
        {
            foreach (var child in channelsSection.GetChildren())
            {
                options.Channels[child.Key] = new TeamPlusChannelEntry
                {
                    ChannelId     = child["ChannelId"]     ?? string.Empty,
                    ChannelSecret = child["ChannelSecret"] ?? string.Empty,
                    AccessToken   = child["AccessToken"]   ?? string.Empty
                };
            }
        });

        // BaseUrl / Timeout 複用 TeamPlusSystemOptions（§6.1 兩套 client 共用相同 BaseUrl）
        var baseUrl = configuration.GetSection(TeamPlusSystemOptions.SectionName)["BaseUrl"]
            ?? throw new InvalidOperationException(
                "TeamPlusSystem:BaseUrl 未設定，請檢查 appsettings.json");

        var timeoutSeconds = int.TryParse(
            configuration.GetSection(TeamPlusSystemOptions.SectionName)["TimeoutSeconds"],
            out var t) ? t : 10;

        services.AddHttpClient<ITeamPlusChannelClient, TeamPlusChannelClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout     = TimeSpan.FromSeconds(timeoutSeconds);
        });

        return services;
    }
}
