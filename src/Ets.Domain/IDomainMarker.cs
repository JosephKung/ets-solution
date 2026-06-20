namespace Ets.Domain;

/// <summary>
/// Domain 層 Assembly 識別標記介面。
/// 用途：其他層（Application / Tests）透過 typeof(IDomainMarker).Assembly
/// 取得 Ets.Domain Assembly，用於反射掃描或架構測試。
/// 此介面不應被任何類別實作。
/// </summary>
public interface IDomainMarker { }
