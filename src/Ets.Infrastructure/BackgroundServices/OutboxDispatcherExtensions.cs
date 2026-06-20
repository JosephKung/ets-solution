// src/Ets.Infrastructure/BackgroundServices/OutboxDispatcherExtensions.cs
using Ets.Application.Interfaces;
using Ets.Infrastructure.Outbox.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Ets.Infrastructure.BackgroundServices;

/// <summary>
/// OutboxDispatcherWorker + 所有 IOutboxHandler DI 統一註冊
/// 於 Program.cs 呼叫：builder.Services.AddOutboxDispatcher();
/// </summary>
public static class OutboxDispatcherExtensions
{
    public static IServiceCollection AddOutboxDispatcher(
        this IServiceCollection services)
    {
        // ── 註冊所有 IOutboxHandler（Scoped，每次 scope 建立新實例）──
        services.AddScoped<IOutboxHandler, CreateTeamOutboxHandler>();
        services.AddScoped<IOutboxHandler, CreateChatOutboxHandler>();
        services.AddScoped<IOutboxHandler, InviteTeamMemberOutboxHandler>();
        services.AddScoped<IOutboxHandler, InviteChatMemberOutboxHandler>();
        services.AddScoped<IOutboxHandler, AssignTeamManagerOutboxHandler>();
        services.AddScoped<IOutboxHandler, AssignChatManagerOutboxHandler>();
        services.AddScoped<IOutboxHandler, SendFlexMessageOutboxHandler>();
        services.AddScoped<IOutboxHandler, SendObserverFlexOutboxHandler>();
        services.AddScoped<IOutboxHandler, UpdateFlexFooterOutboxHandler>();
        services.AddScoped<IOutboxHandler, CreateTeamApiAccountOutboxHandler>();
        services.AddScoped<IOutboxHandler, PostVirtualMsgOutboxHandler>();

        // ── 工廠（Scoped，從同一 scope 取 Handler）──────────────────
        services.AddScoped<IOutboxHandlerFactory, OutboxHandlerFactory>();

        // ── Background Worker（Singleton）───────────────────────────
        services.AddHostedService<OutboxDispatcherWorker>();

        return services;
    }
}
