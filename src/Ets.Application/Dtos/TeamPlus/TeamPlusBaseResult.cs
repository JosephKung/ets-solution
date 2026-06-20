// src/Ets.Application/Dtos/TeamPlus/TeamPlusBaseResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// team+ API 通用回應基底（所有 System API 皆包含此三個欄位）
/// </summary>
public record TeamPlusBaseResult(
    bool IsSuccess,
    string Description,
    int ErrorCode);
