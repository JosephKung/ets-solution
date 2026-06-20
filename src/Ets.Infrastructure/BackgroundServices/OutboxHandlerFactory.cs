// src/Ets.Infrastructure/BackgroundServices/OutboxHandlerFactory.cs
using Ets.Application.Interfaces;
using Ets.Domain.Enums;

namespace Ets.Infrastructure.BackgroundServices;

/// <summary>
/// Outbox Handler 工廠實作
/// 從 DI 容器取得所有 IOutboxHandler，以 MessageType 為 key 建立路由字典
/// </summary>
public sealed class OutboxHandlerFactory : IOutboxHandlerFactory
{
    private readonly IReadOnlyDictionary<OutboxMessageType, IOutboxHandler> _handlers;

    public OutboxHandlerFactory(IEnumerable<IOutboxHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.MessageType);
    }

    public IOutboxHandler GetHandler(OutboxMessageType messageType)
    {
        if (_handlers.TryGetValue(messageType, out var handler))
            return handler;

        throw new InvalidOperationException(
            $"找不到 MessageType={messageType} 的 IOutboxHandler，" +
            $"請確認已在 DI 容器中註冊對應 Handler。");
    }
}
