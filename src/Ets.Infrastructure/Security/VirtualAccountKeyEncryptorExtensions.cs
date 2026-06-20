// src/Ets.Infrastructure/Security/VirtualAccountKeyEncryptorExtensions.cs
using Ets.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Infrastructure.Security;

/// <summary>
/// VirtualAccountKeyEncryptor DI 註冊擴充方法
/// 於 Program.cs 呼叫：builder.Services.AddVirtualAccountKeyEncryptor();
/// </summary>
public static class VirtualAccountKeyEncryptorExtensions
{
    public static IServiceCollection AddVirtualAccountKeyEncryptor(
        this IServiceCollection services)
    {
        // Singleton：金鑰只需從環境變數讀取一次
        services.AddSingleton<IVirtualAccountKeyEncryptor, VirtualAccountKeyEncryptor>();
        return services;
    }
}
