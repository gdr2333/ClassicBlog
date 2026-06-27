using ClassicBlog.Data;
using ClassicBlog.Models;
using Microsoft.EntityFrameworkCore;

namespace ClassicBlog.Services;

/// <summary>
/// Handles comment creation and the associated best-effort email notifications:
/// posting a comment notifies the post's author; replying to a comment also
/// notifies the author of the parent comment. Self-notifications are skipped.
/// </summary>
public class CommentService(BlogDbContext db, IEmailService email, ILogger<CommentService> logger)
{
    public async Task AddCommentAsync(int postId, int? parentCommentId, string author, string email, string content)
    {
        var comment = new Comment
        {
            PostId = postId,
            ParentCommentId = parentCommentId,
            Author = author,
            Email = email,
            Content = content,
            CreatedAt = DateTime.UtcNow,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        await NotifyAsync(comment, postId, parentCommentId, author, email, content);
    }

    private async Task NotifyAsync(Comment comment, int postId, int? parentCommentId,
        string author, string commenterEmail, string content)
    {
        try
        {
            var post = await db.Posts.AsNoTracking().Include(p => p.Author)
                .FirstOrDefaultAsync(p => p.Id == postId);
            if (post is null) return;

            var link = $"/post/{post.Id}";
            var snippet = content.Length > 200 ? content[..200] + "…" : content;
            var authorEmail = post.Author?.Email;

            // Notify the post author of a new comment (skip if commenter is the author).
            if (!string.IsNullOrWhiteSpace(authorEmail) &&
                !string.Equals(authorEmail, commenterEmail, StringComparison.OrdinalIgnoreCase))
            {
                await email.SendAsync(
                    authorEmail,
                    $"New comment on \"{post.Title}\"",
                    $"{author} commented on your post \"{post.Title}\":\n\n{snippet}\n\nView: {link}");
            }

            // Notify the parent comment's author of a reply (skip self and the post author).
            if (parentCommentId is int parentId)
            {
                var parent = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId);
                if (parent is not null &&
                    !string.IsNullOrWhiteSpace(parent.Email) &&
                    !string.Equals(parent.Email, commenterEmail, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parent.Email, authorEmail, StringComparison.OrdinalIgnoreCase))
                {
                    await email.SendAsync(
                        parent.Email,
                        $"Reply to your comment on \"{post.Title}\"",
                        $"{author} replied to your comment on \"{post.Title}\":\n\n{snippet}\n\nView: {link}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Comment notification failed for post {PostId}", postId);
        }
    }
}
