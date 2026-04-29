using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Admin;

// ── Pages ─────────────────────────────────────────────────────────────────────

public class PageListItemViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public bool IsPublished { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentTitle { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateEditPageViewModel
{
    public Guid? Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    [Required, MaxLength(300), RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "Slug may only contain lowercase letters, numbers, and hyphens.")]
    public string Slug { get; set; } = "";

    public string Content { get; set; } = "";

    [MaxLength(300)]
    public string? MetaTitle { get; set; }

    [MaxLength(500)]
    public string? MetaDescription { get; set; }

    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }

    public List<SelectListItem> AvailableParents { get; set; } = [];
}

// ── Categories ────────────────────────────────────────────────────────────────

public class CategoryListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public int PostCount { get; set; }
}

public class CreateEditCategoryViewModel
{
    public Guid? Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [Required, MaxLength(200), RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "Slug may only contain lowercase letters, numbers, and hyphens.")]
    public string Slug { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public List<SelectListItem> AvailableParents { get; set; } = [];
}

// ── Posts ─────────────────────────────────────────────────────────────────────

public class PostListItemViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public bool IsPublished { get; set; }
    public string? CategoryName { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ViewCount { get; set; }
}

public class CreateEditPostViewModel
{
    public Guid? Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    [Required, MaxLength(300), RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "Slug may only contain lowercase letters, numbers, and hyphens.")]
    public string Slug { get; set; } = "";

    [MaxLength(500)]
    public string? Excerpt { get; set; }

    public string Content { get; set; } = "";

    [MaxLength(300)]
    public string? MetaTitle { get; set; }

    [MaxLength(500)]
    public string? MetaDescription { get; set; }

    [MaxLength(1000), Url]
    public string? FeaturedImageUrl { get; set; }

    public Guid? CategoryId { get; set; }
    public bool IsPublished { get; set; }

    /// <summary>Comma-separated tag slugs, e.g. "dotnet,csharp,web"</summary>
    public string Tags { get; set; } = "";

    public List<SelectListItem> AvailableCategories { get; set; } = [];
}
