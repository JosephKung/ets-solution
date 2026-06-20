namespace Ets.Application.UseCases.CheckIn;

/// <summary>
/// 報到流程驗證失敗例外（含業務錯誤碼）。
/// </summary>
public class CheckInException : Exception
{
    /// <summary>業務錯誤碼（如 C4001、C4002、C4004、C4005、A7001）</summary>
    public string ErrorCode { get; }

    public CheckInException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
