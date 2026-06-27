namespace ClassicBlog.Models;

public class Post
{
    public int Id { get; set; }

    /// <summary>URL-friendly identifier, derived from the title.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Short summary shown in the post list.</summary>
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>Full body, plain text. Rendered with preserved line breaks.</summary>
    public string Content { get; set; } = string.Empty;

    public bool IsPublished { get; set; } = true;

    /// <summary>Author of the post (the logged-in user who created it). Null for seeded posts.</summary>
    public int? AuthorId { get; set; }
    public ApplicationUser? Author { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Comment> Comments { get; set; } = new();

    /// <summary>Builds a slug from a title (lowercase, hyphen-separated, ASCII-only).</summary>
    public static string MakeSlug(string title)
    {
        var slug = title.Trim().ToLowerInvariant();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = slug.Trim('-');
        return slug;
    }
}
