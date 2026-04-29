using FlexCms.Framework.Cms;
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

    /// <summary>Set a future time to schedule publishing (leave blank to publish immediately or save as draft).</summary>
    public DateTime? ScheduledAt { get; set; }

    public PageAccessControl AccessControl { get; set; } = PageAccessControl.Public;

    /// <summary>Plain-text; stored as SHA-256 hash. Leave blank to keep existing.</summary>
    [MaxLength(200)]
    public string? Password { get; set; }

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

    /// <summary>Set a future time to schedule publishing.</summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>Comma-separated tag slugs, e.g. "dotnet,csharp,web"</summary>
    public string Tags { get; set; } = "";

    public List<SelectListItem> AvailableCategories { get; set; } = [];
}

// ── Trash ─────────────────────────────────────────────────────────────────────

public class TrashItemViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime? DeletedAt { get; set; }
}

public class TrashViewModel
{
    public List<TrashItemViewModel> Pages { get; set; } = [];
    public List<TrashItemViewModel> Posts { get; set; } = [];
}

// ── Redirects ─────────────────────────────────────────────────────────────────

public class RedirectListItemViewModel
{
    public Guid Id { get; set; }
    public string FromPath { get; set; } = "";
    public string ToPath { get; set; } = "";
    public int StatusCode { get; set; }
    public bool IsActive { get; set; }
    public int HitCount { get; set; }
}

public class CreateEditRedirectViewModel
{
    public Guid? Id { get; set; }

    [Required, MaxLength(500)]
    public string FromPath { get; set; } = "";

    [Required, MaxLength(2000)]
    public string ToPath { get; set; } = "";

    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
}
