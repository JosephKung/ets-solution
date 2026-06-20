// src/Ets.Infrastructure/BackgroundServices/ReadStatusPollingWorkerExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Infrastructure.BackgroundServices;

/// <summary>
/// ReadStatusPollingWorker DI 註冊擴充方法
/// 於 Program.cs 呼叫：builder.Services.AddReadStatusPollingWorker();
/// </summary>
public static class ReadStatusPollingWorkerExtensions
{
    public static IServiceCollection AddReadStatusPollingWorker(
        this IServiceCollection services)
    {
        services.AddHostedService<ReadStatusPollingWorker>();
        return services;
    }
}
