using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3: <see cref="PreparationSchemaCapability"/>, the preparation-specific schema
/// resolver that is deliberately independent of <c>Services.DataSafety.BackupSchemaCapability</c> even
/// though both reuse the same <see cref="Schema8ShapeValidator"/>. Covers valid Schema-7/Schema-8 shapes,
/// malformed shapes for each reported version, and unsupported versions. Every fixture is a single
/// <see cref="Schema7Fixture"/>, migrated in place via <see cref="Schema8BackupFixtureBuilders.MigrateAsync"/>
/// where a Schema-8 shape is needed — never a real application database.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PreparationSchemaCapabilityTests
{
    [TestMethod]
    public async Task Resolve_ValidSchema7Shape_ReturnsSchema7Capability()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        PreparationSchemaCapabilityResult? result = null;
        await fixture.Connection.RunInTransactionAsync(connection => result = PreparationSchemaCapability.Resolve(connection));

        Assert.IsInstanceOfType<PreparationSchema7CapabilityResult>(result);
    }

    [TestMethod]
    public async Task Resolve_ValidSchema8Shape_ReturnsSchema8Capability()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);

        PreparationSchemaCapabilityResult? result = null;
        await fixture.Connection.RunInTransactionAsync(connection => result = PreparationSchemaCapability.Resolve(connection));

        Assert.IsInstanceOfType<PreparationSchema8CapabilityResult>(result);
    }

    [TestMethod]
    public async Task Resolve_Schema7VersionButSchema8Shape_ThrowsShapeMismatch()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        // Mimic a Schema-8-only table existing while user_version still reports 7.
        await fixture.Connection.ExecuteAsync(
            "CREATE TABLE Senses (Id INTEGER PRIMARY KEY)");

        var exception = await Assert.ThrowsExactlyAsync<PreparationSchemaCapabilityException>(() =>
            fixture.Connection.RunInTransactionAsync(connection => PreparationSchemaCapability.Resolve(connection)));

        Assert.AreEqual(7, exception.FoundVersion);
        Assert.IsTrue(exception.ShapeMismatch);
        Assert.AreEqual("preparation-schema-capability-shape-mismatch", exception.ErrorCode);
    }

    [TestMethod]
    public async Task Resolve_Schema8VersionButMissingRequiredTable_ThrowsShapeMismatch()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        await fixture.Connection.ExecuteAsync("DROP TABLE Senses");

        var exception = await Assert.ThrowsExactlyAsync<PreparationSchemaCapabilityException>(() =>
            fixture.Connection.RunInTransactionAsync(connection => PreparationSchemaCapability.Resolve(connection)));

        Assert.AreEqual(8, exception.FoundVersion);
        Assert.IsTrue(exception.ShapeMismatch);
        Assert.AreEqual("preparation-schema-capability-shape-mismatch", exception.ErrorCode);
    }

    [TestMethod]
    public async Task Resolve_UnsupportedVersion_ThrowsUnsupportedVersion()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 99");

        var exception = await Assert.ThrowsExactlyAsync<PreparationSchemaCapabilityException>(() =>
            fixture.Connection.RunInTransactionAsync(connection => PreparationSchemaCapability.Resolve(connection)));

        Assert.AreEqual(99, exception.FoundVersion);
        Assert.IsFalse(exception.ShapeMismatch);
        Assert.AreEqual("preparation-schema-capability-unsupported-version", exception.ErrorCode);
    }
}
