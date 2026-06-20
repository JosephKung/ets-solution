using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Application;

/// <summary>
/// Application 層 DI 服務註冊擴充方法。
/// 在 Program.cs 呼叫 builder.Services.AddApplicationServices() 即可完成整層服務注入。
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// 註冊 Application 層所有服務：MediatR Handlers、FluentValidation Validators、Pipeline Behaviors。
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <returns>IServiceCollection（供鏈式呼叫）</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationServiceExtensions).Assembly;

        // 註冊 MediatR（掃描本 Assembly 下所有 IRequestHandler）
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        // 註冊 FluentValidation（掃描本 Assembly 下所有 AbstractValidator<T>）
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
