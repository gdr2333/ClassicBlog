using System.ComponentModel.DataAnnotations;

namespace ClassicBlog.Models;

public class Comment
{
    public int Id { get; set; }

    public int PostId { get; set; }
    public Post? Post { get; set; }

    /// <summary>Parent comment id for threaded replies; null for a top-level comment.</summary>
    public int? ParentCommentId { get; set; }
    public Comment? Parent { get; set; }

    public List<Comment> Replies { get; set; } = new();

    [Required, StringLength(64)]
    public string Author { get; set; } = "Anonymous";

    /// <summary>Required on every comment; used to notify the commenter when someone replies.</summary>
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
