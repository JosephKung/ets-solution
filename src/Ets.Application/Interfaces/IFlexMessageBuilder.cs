// src/Ets.Application/Interfaces/IFlexMessageBuilder.cs
using Ets.Domain.Entities;

namespace Ets.Application.Interfaces;

/// <summary>
/// Flex Message 組裝器介面（§6.4）
/// Application 層依賴此介面，Infrastructure 層實作
/// </summary>
public interface IFlexMessageBuilder
{
    /// <summary>
    /// 組裝含按鈕版本 Flex Message contents（§6.4，非 observer 使用）
    /// </summary>
    object BuildContentsWithButtons(EmergencyEvent ev);

    /// <summary>
    /// 組裝無按鈕版本 Flex Message contents（§6.4.2，observer 使用）
    /// </summary>
    object BuildContentsWithoutButtons(EmergencyEvent ev);
}
