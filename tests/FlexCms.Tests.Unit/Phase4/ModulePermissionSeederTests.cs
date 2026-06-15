using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Framework.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Unit.Phase4;

/// <summary>
/// Permissions a module declares must end up in fcms_permissions with the
/// <c>{ModuleId}.</c> prefix, and re-running the seeder on the next restart
/// must not insert duplicates. These tests pin both contracts.
/// </summary>
public class ModulePermissionSeederTests
{
    [Fact]
    public async Task Empty_GetPermissions_seeds_nothing()
    {
        var (seeder, repo, uow) = Build();
        var module = new TestModule { Permissions = [] };

        await seeder.SeedAsync(module);

        await repo.DidNotReceive().AddAsync(Arg.Any<FcmsPermission>(), Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task First_run_inserts_each_permission_with_module_id_prefix()
    {
        var (seeder, repo, uow) = Build();
        var module = new TestModule
        {
            Permissions =
            [
                new("invest.create", "Create Investments", "Investments"),
                new("invest.delete", "Delete Investments", "Investments")
            ]
        };

        var captured = new List<FcmsPermission>();
        await repo.AddAsync(Arg.Do<FcmsPermission>(captured.Add), Arg.Any<CancellationToken>());

        await seeder.SeedAsync(module);

        Assert.Equal(2, captured.Count);
        Assert.Contains(captured, p => p.Key == "flexcms.investment.invest.create");
        Assert.Contains(captured, p => p.Key == "flexcms.investment.invest.delete");
        Assert.All(captured, p => Assert.Equal("Investments", p.Group));
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Second_run_with_same_definitions_does_not_re_add()
    {
        var (seeder, repo, uow) = Build();
        var module = new TestModule
        {
            Permissions = [new("invest.create", "Create Investments", "Investments")]
        };

        // Simulate fcms_permissions already containing the permission from a previous restart
        var existing = new List<FcmsPermission>
        {
            new() { Key = "flexcms.investment.invest.create", DisplayName = "Create Investments", Group = "Investments" }
        };
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(existing);

        await seeder.SeedAsync(module);

        await repo.DidNotReceive().AddAsync(Arg.Any<FcmsPermission>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().UpdateAsync(Arg.Any<FcmsPermission>(), Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Display_name_or_group_change_triggers_update_not_insert()
    {
        var (seeder, repo, uow) = Build();
        var module = new TestModule
        {
            Permissions = [new("invest.create", "Create New Investments", "Investments")]
        };

        var existing = new FcmsPermission
        {
            Key = "flexcms.investment.invest.create",
            DisplayName = "Old label",
            Group = "Investments"
        };
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<FcmsPermission> { existing });

        await seeder.SeedAsync(module);

        await repo.DidNotReceive().AddAsync(Arg.Any<FcmsPermission>(), Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateAsync(
            Arg.Is<FcmsPermission>(p => p.DisplayName == "Create New Investments"),
            Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_group_falls_back_to_module_name()
    {
        var (seeder, repo, _) = Build();
        var module = new TestModule
        {
            Permissions = [new("invest.create", "Create", Group: "")]
        };

        var captured = new List<FcmsPermission>();
        await repo.AddAsync(Arg.Do<FcmsPermission>(captured.Add), Arg.Any<CancellationToken>());

        await seeder.SeedAsync(module);

        Assert.Single(captured);
        Assert.Equal("Investments", captured[0].Group);
    }

    [Fact]
    public async Task Blank_keys_are_skipped()
    {
        var (seeder, repo, _) = Build();
        var module = new TestModule
        {
            Permissions =
            [
                new("", "Empty key", "G"),
                new("   ", "Whitespace", "G")
            ]
        };

        await seeder.SeedAsync(module);

        await repo.DidNotReceive().AddAsync(Arg.Any<FcmsPermission>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Key_prefix_is_lowercased_regardless_of_module_id_casing()
    {
        var (seeder, repo, _) = Build();
        var module = new TestModule
        {
            ModuleIdValue = "FlexCms.Investment.MIXEDcase",
            Permissions = [new("INVEST.Create", "Create", "Investments")]
        };

        var captured = new List<FcmsPermission>();
        await repo.AddAsync(Arg.Do<FcmsPermission>(captured.Add), Arg.Any<CancellationToken>());

        await seeder.SeedAsync(module);

        Assert.Single(captured);
        Assert.Equal("flexcms.investment.mixedcase.invest.create", captured[0].Key);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (ModulePermissionSeeder, IRepository<FcmsPermission>, IFcmsUnitOfWork) Build()
    {
        var repo = Substitute.For<IRepository<FcmsPermission>>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<FcmsPermission>());
        var uow = Substitute.For<IFcmsUnitOfWork>();
        var logger = Substitute.For<ILogger<ModulePermissionSeeder>>();
        return (new ModulePermissionSeeder(repo, uow, logger), repo, uow);
    }

    private sealed class TestModule : IFcmsModule
    {
        public string ModuleIdValue { get; init; } = "FlexCms.Investment";
        public List<FcmsPermissionDef> Permissions { get; init; } = [];

        public string ModuleId => ModuleIdValue;
        public string ModuleName => "Investments";
        public string Version => "1.0.0";
        public string TablePrefix => "inv";

        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { }
        public DbContext? CreateMigrationContext(string connectionString, string provider) => null;
        public Task SeedDataAsync(IServiceProvider sp, CancellationToken ct = default) => Task.CompletedTask;
        public Task OnUpgradeAsync(string fromVersion, IServiceProvider sp, CancellationToken ct = default) => Task.CompletedTask;
        public Task DropTablesAsync(string connectionString, string provider, CancellationToken ct = default) => Task.CompletedTask;
        public List<FcmsMenuItemDef> GetMenuItems() => [];
        public List<FcmsPermissionDef> GetPermissions() => Permissions;
    }
}
