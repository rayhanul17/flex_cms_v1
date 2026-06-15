using FlexCms.Framework.Db;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Tests.Integration.Helpers;

/// <summary>
/// Exercises <see cref="FcmsSqlHelper"/> against a real SQLite in-memory
/// database. SQLite reproduces enough of the ADO.NET surface to pin the
/// helper's column-to-property mapping, DBNull handling, and parameter
/// binding without needing MySQL/Postgres infrastructure.
/// </summary>
public class FcmsSqlHelperTests : IAsyncLifetime
{
    private TestSqlContext _ctx = null!;

    public async Task InitializeAsync()
    {
        _ctx = new TestSqlContext();
        await _ctx.Database.OpenConnectionAsync();
        await _ctx.Database.EnsureCreatedAsync();

        _ctx.People.AddRange(
            new Person { Id = 1, Name = "Alice", Age = 30, IsActive = true },
            new Person { Id = 2, Name = "Bob",   Age = 40, IsActive = false },
            new Person { Id = 3, Name = "Carol", Age = 25, IsActive = true });
        await _ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task QueryAsync_maps_columns_to_dto_properties_by_name()
    {
        var rows = await FcmsSqlHelper.QueryAsync<PersonDto>(
            _ctx,
            "SELECT Id, Name, Age FROM People ORDER BY Id;");

        Assert.Equal(3, rows.Count);
        Assert.Equal("Alice", rows[0].Name);
        Assert.Equal(30, rows[0].Age);
    }

    [Fact]
    public async Task QueryAsync_honours_FcmsSqlColumn_override()
    {
        var rows = await FcmsSqlHelper.QueryAsync<RenamedDto>(
            _ctx,
            "SELECT Name AS FullName FROM People WHERE Id = 1;");

        Assert.Equal("Alice", rows.Single().FullName);
    }

    [Fact]
    public async Task QueryAsync_binds_parameters()
    {
        var rows = await FcmsSqlHelper.QueryAsync<PersonDto>(
            _ctx,
            "SELECT Id, Name, Age FROM People WHERE IsActive = @active ORDER BY Id;",
            new[] { new KeyValuePair<string, object?>("@active", 1) });

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Contains(r.Name, new[] { "Alice", "Carol" }));
    }

    [Fact]
    public async Task QueryAsync_handles_DBNull_by_leaving_property_at_default()
    {
        var rows = await FcmsSqlHelper.QueryAsync<NullableDto>(
            _ctx,
            "SELECT Id, NULL AS OptionalAge FROM People WHERE Id = 1;");

        Assert.Null(rows.Single().OptionalAge);
    }

    [Fact]
    public async Task ScalarAsync_returns_typed_scalar()
    {
        var count = await FcmsSqlHelper.ScalarAsync<long>(_ctx, "SELECT COUNT(*) FROM People;");
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ScalarAsync_returns_default_on_DBNull()
    {
        var result = await FcmsSqlHelper.ScalarAsync<int?>(
            _ctx,
            "SELECT NULL;");
        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_returns_affected_row_count()
    {
        var affected = await FcmsSqlHelper.ExecuteAsync(
            _ctx,
            "UPDATE People SET IsActive = 1 WHERE Id = @id;",
            new[] { new KeyValuePair<string, object?>("@id", 2) });

        Assert.Equal(1, affected);
    }

    // ── test fixtures ───────────────────────────────────────────────────

    public class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }

    public class PersonDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    public class RenamedDto
    {
        [FcmsSqlColumn("FullName")]
        public string FullName { get; set; } = "";
    }

    public class NullableDto
    {
        public int Id { get; set; }
        public int? OptionalAge { get; set; }
    }

    private class TestSqlContext : DbContext
    {
        public DbSet<Person> People => Set<Person>();
        protected override void OnConfiguring(DbContextOptionsBuilder b)
            => b.UseSqlite("DataSource=:memory:");
    }
}
