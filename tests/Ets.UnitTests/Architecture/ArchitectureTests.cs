using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Ets.UnitTests.Architecture;

/// <summary>
/// 架構測試：驗證 Clean Architecture 四層之依賴方向正確性。
///
/// 合法依賴方向（單向）：
///   WebApi → Infrastructure → Application → Domain
///
/// 禁止的反向依賴：
///   Domain      不得 reference Application / Infrastructure / WebApi
///   Application 不得 reference Infrastructure / WebApi
/// </summary>
public class ArchitectureTests
{
    // 透過已參考的公開型別取得 Assembly（不需要額外 ProjectReference）
    private static readonly Assembly DomainAssembly = typeof(Ets.Domain.IDomainMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Ets.Application.ApplicationServiceExtensions).Assembly;

    // Infrastructure 未被 UnitTests 直接參考，透過 Assembly 名稱載入
    // 注意：此測試需在 build 後執行（bin 目錄下需有 Ets.Infrastructure.dll）
    private static readonly Assembly InfrastructureAssembly =
        Assembly.Load("Ets.Infrastructure");

    [Fact(DisplayName = "Domain 層不得依賴 Application 層")]
    public void Domain_ShouldNot_DependOn_Application()
    {
        var referencedNames = GetReferencedAssemblyNames(DomainAssembly);

        referencedNames
            .Should()
            .NotContain(
                name => name.Contains("Ets.Application", StringComparison.OrdinalIgnoreCase),
                because: "Domain 層是最內層，不得依賴 Application 層（違反 Clean Architecture）");
    }

    [Fact(DisplayName = "Domain 層不得依賴 Infrastructure 層")]
    public void Domain_ShouldNot_DependOn_Infrastructure()
    {
        var referencedNames = GetReferencedAssemblyNames(DomainAssembly);

        referencedNames
            .Should()
            .NotContain(
                name => name.Contains("Ets.Infrastructure", StringComparison.OrdinalIgnoreCase),
                because: "Domain 層是最內層，不得依賴 Infrastructure 層（違反 Clean Architecture）");
    }

    [Fact(DisplayName = "Application 層不得依賴 Infrastructure 層")]
    public void Application_ShouldNot_DependOn_Infrastructure()
    {
        var referencedNames = GetReferencedAssemblyNames(ApplicationAssembly);

        referencedNames
            .Should()
            .NotContain(
                name => name.Contains("Ets.Infrastructure", StringComparison.OrdinalIgnoreCase),
                because: "Application 層只能定義介面，實作細節由 Infrastructure 層提供（Dependency Inversion Principle）");
    }

    [Fact(DisplayName = "Application 層應依賴 Domain 層")]
    public void Application_Should_DependOn_Domain()
    {
        var referencedNames = GetReferencedAssemblyNames(ApplicationAssembly);

        referencedNames
            .Should()
            .Contain(
                name => name.Contains("Ets.Domain", StringComparison.OrdinalIgnoreCase),
                because: "Application 層需要使用 Domain 層的 Entity 和 Value Object");
    }

    [Fact(DisplayName = "Infrastructure 層應依賴 Application 層")]
    public void Infrastructure_Should_DependOn_Application()
    {
        var referencedNames = GetReferencedAssemblyNames(InfrastructureAssembly);

        referencedNames
            .Should()
            .Contain(
                name => name.Contains("Ets.Application", StringComparison.OrdinalIgnoreCase),
                because: "Infrastructure 層需要實作 Application 層定義的介面（如 IUnitOfWork、Repository）");
    }

    /// <summary>
    /// 取得指定 Assembly 直接參考的所有 Assembly 名稱清單。
    /// </summary>
    private static IEnumerable<string> GetReferencedAssemblyNames(Assembly assembly)
        => assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);
}
