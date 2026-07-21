using FamilyTree.Domain;
using FamilyTree.Storage;
using Shouldly;
using Xunit;

namespace FamilyTree.Tests;

/// <summary>
/// Smoke-тести каркаса (T-0.1): тестовий проєкт бачить Domain і Storage,
/// збірка й тестовий раннер працюють.
/// </summary>
public class SolutionSmokeTests
{
    [Fact]
    public void Test_project_references_Domain()
    {
        DomainAssemblyMarker.Name.ShouldBe("FamilyTree.Domain");
    }

    [Fact]
    public void Test_project_references_Storage()
    {
        StorageAssemblyMarker.Name.ShouldBe("FamilyTree.Storage");
    }
}
