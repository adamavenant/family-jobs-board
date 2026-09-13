using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FamilyJobsBoard.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    // Dependencies are included (non-recursively) so that references to
    // external assemblies such as Microsoft.EntityFrameworkCore and
    // Microsoft.AspNetCore.* are represented in the architecture and can be
    // asserted against, not just the four project assemblies themselves.
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssembliesIncludingDependencies(
            [
                System.Reflection.Assembly.Load("FamilyJobsBoard.Domain"),
                System.Reflection.Assembly.Load("FamilyJobsBoard.Application"),
                System.Reflection.Assembly.Load("FamilyJobsBoard.Infrastructure"),
                System.Reflection.Assembly.Load("FamilyJobsBoard.Api"),
            ],
            recursive: false)
        .Build();

    private static readonly IObjectProvider<IType> DomainLayer = Types()
        .That()
        .ResideInNamespaceMatching(@"^FamilyJobsBoard\.Domain(\..+)?$")
        .As("Domain layer");

    private static readonly IObjectProvider<IType> ApplicationLayer = Types()
        .That()
        .ResideInNamespaceMatching(@"^FamilyJobsBoard\.Application(\..+)?$")
        .As("Application layer");

    private static readonly IObjectProvider<IType> InfrastructureLayer = Types()
        .That()
        .ResideInNamespaceMatching(@"^FamilyJobsBoard\.Infrastructure(\..+)?$")
        .As("Infrastructure layer");

    private static readonly IObjectProvider<IType> ApiLayer = Types()
        .That()
        .ResideInNamespaceMatching(@"^FamilyJobsBoard\.Api(\..+)?$")
        .As("Api layer");

    private static readonly IObjectProvider<IType> AspNetCore = Types()
        .That()
        .ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..+)?$")
        .As("ASP.NET Core");

    private static readonly IObjectProvider<IType> EntityFrameworkCore = Types()
        .That()
        .ResideInNamespaceMatching(@"^Microsoft\.EntityFrameworkCore(\..+)?$")
        .As("Entity Framework Core");

    // ADR 0002: "Domain: aggregates, value objects, invariants, domain
    // events; it does not reference EF Core or ASP.NET Core."

    [Fact]
    public void Domain_does_not_depend_on_AspNetCore()
    {
        Types()
            .That()
            .Are(DomainLayer)
            .Should()
            .NotDependOnAny(AspNetCore)
            .Because("ADR 0002 requires the Domain layer to have no ASP.NET Core dependency")
            .Check(Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_EntityFrameworkCore()
    {
        Types()
            .That()
            .Are(DomainLayer)
            .Should()
            .NotDependOnAny(EntityFrameworkCore)
            .Because("ADR 0002 requires the Domain layer to have no EF Core dependency")
            .Check(Architecture);
    }

    // ADR 0002: "Application: use cases and ports; no infrastructure
    // implementation."

    [Fact]
    public void Application_does_not_depend_on_AspNetCore()
    {
        Types()
            .That()
            .Are(ApplicationLayer)
            .Should()
            .NotDependOnAny(AspNetCore)
            .Because("ADR 0002 requires the Application layer to have no ASP.NET Core dependency")
            .Check(Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_EntityFrameworkCore()
    {
        Types()
            .That()
            .Are(ApplicationLayer)
            .Should()
            .NotDependOnAny(EntityFrameworkCore)
            .Because("ADR 0002 requires the Application layer to have no EF Core dependency")
            .Check(Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure()
    {
        Types()
            .That()
            .Are(ApplicationLayer)
            .Should()
            .NotDependOnAny(InfrastructureLayer)
            .Because("ADR 0002: Application never depends on Infrastructure")
            .Check(Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_Api()
    {
        Types()
            .That()
            .Are(ApplicationLayer)
            .Should()
            .NotDependOnAny(ApiLayer)
            .Because("ADR 0002: Application never depends on the Api composition root")
            .Check(Architecture);
    }
}
