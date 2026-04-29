# FlexCms Module Template

## Install

```bash
dotnet new install ./templates/flexcms-module
```

Or from NuGet (after publishing):

```bash
dotnet new install FlexCms.Templates
```

## Usage

```bash
dotnet new flexcms-module -n FlexCms.Blog --TablePrefix blog
```

This creates a `FlexCms.Blog/` folder with:
- `FlexCms.Blog.csproj`
- `module.json` (embedded resource — discovered by ModuleLoader)
- `FlexCms.BlogModule.cs` (inherits BaseModule, all lifecycle hooks stubbed)
- `BlogDbContext.cs` (module-owned EF context with auto table naming)

## After scaffolding

1. Add your entities to `BlogDbContext`
2. Run `dotnet ef migrations add Init --project FlexCms.Blog`
3. Implement `CreateMigrationContext()` in `FlexCms.BlogModule.cs`
4. Build → copy output to `modules/FlexCms.Blog/` → restart app
5. Admin → Modules → Activate
