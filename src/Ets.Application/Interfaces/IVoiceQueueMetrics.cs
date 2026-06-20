// src/Ets.Application/Interfaces/IVoiceQueueMetrics.cs
namespace Ets.Application.Interfaces;

/// <summary>
/// 語音 API 佇列觀察指標介面（WBS 1.4.3）
/// 實作由 Infrastructure 層提供（Application Insights / Serilog metrics）
/// </summary>
public interface IVoiceQueueMetrics
{
    /// <summary>
    /// 記錄外撥後的佇列狀態，若接近壅塞則發出告警
    /// </summary>
    void RecordQueueStatus(int inUse, int max, int waiting);
}
