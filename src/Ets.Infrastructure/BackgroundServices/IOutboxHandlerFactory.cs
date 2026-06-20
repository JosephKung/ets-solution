// src/Ets.Infrastructure/BackgroundServices/IOutboxHandlerFactory.cs
using Ets.Application.Interfaces;
using Ets.Domain.Enums;

namespace Ets.Infrastructure.BackgroundServices;

/// <summary>
/// Outbox Handler 工廠介面
/// 依 OutboxMessageType 路由至對應 IOutboxHandler
/// </summary>
public interface IOutboxHandlerFactory
{
    IOutboxHandler GetHandler(OutboxMessageType messageType);
}
