// src/Ets.Infrastructure/ExternalClients/Voice/VoiceApiClientExtensions.cs
using Ets.Application.Interfaces;
using Ets.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ets.Infrastructure.Metrics;

namespace Ets.Infrastructure.ExternalClients.Voice;

/// <summary>
/// Voice API Client + VoiceFallbackWorker DI 註冊擴充方法
/// 於 Program.cs 呼叫：builder.Services.AddVoiceFallback(builder.Configuration);
/// </summary>
public static class VoiceApiClientExtensions
{
    public static IServiceCollection AddVoiceFallback(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 綁定 VoiceNotifyOptions
        services.Configure<VoiceNotifyOptions>(
            configuration.GetSection(VoiceNotifyOptions.SectionName));
		// ... HttpClient 註冊 ...
		services.AddSingleton<IVoiceQueueMetrics, VoiceQueueMetrics>(); 
        var section = configuration.GetSection(VoiceNotifyOptions.SectionName);

        var baseUrl = section["VoiceApiBaseUrl"]
            ?? throw new InvalidOperationException(
                "VoiceNotifyConfig:VoiceApiBaseUrl 未設定");

        var timeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var t) ? t : 10;

        // 註冊 Typed HttpClient
        services.AddHttpClient<IVoiceApiClient, VoiceApiClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout     = TimeSpan.FromSeconds(timeoutSeconds);
        });

        // 啟動 VoiceFallbackWorker
        services.AddHostedService<VoiceFallbackWorker>();

        return services;
    }
}
