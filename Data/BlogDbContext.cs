using ClassicBlog.Models;
using Microsoft.EntityFrameworkCore;

namespace ClassicBlog.Data;

public class BlogDbContext(DbContextOptions<BlogDbContext> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<PageView> PageViews => Set<PageView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>(e =>
        {
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasMany(p => p.Comments)
                .WithOne(c => c.Post)
                .HasForeignKey(c => c.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Author)
                .WithMany()
                .HasForeignKey(p => p.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Comment>(e =>
        {
            // Threaded replies: a comment may have a parent comment.
            e.HasOne(c => c.Parent)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<PageView>(e =>
        {
            e.HasIndex(p => p.Path).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Seeds a handful of sample posts the first time the database is created.</summary>
    public async Task EnsureSeedDataAsync()
    {
        if (await Posts.AnyAsync())
            return;

        var now = DateTime.UtcNow;
        var posts = new List<Post>
        {
            new()
            {
                Title = "Welcome to ClassicBlog",
                Slug = Post.MakeSlug("Welcome to ClassicBlog"),
                Excerpt = "A retro blog running on Blazor Server, EF Core and SQLite, skinned with BOOTSTRA.386.",
                Content =
                    "Welcome!\n\n" +
                    "This blog is built with:\n" +
                    "  - ASP.NET Core Blazor Server (.NET 10)\n" +
                    "  - Entity Framework Core + SQLite\n" +
                    "  - BOOTSTRA.386 by kristopolous\n\n" +
                    "Use the Manage page to create, edit and delete posts. Comments can be added on each post.",
                IsPublished = true,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now.AddDays(-2),
            },
            new()
            {
                Title = "Why 386?",
                Slug = Post.MakeSlug("Why 386?"),
                Excerpt = "A few words on the aesthetic of early-nineties user interfaces.",
                Content =
                    "The BOOTSTRA.386 theme brings back the Fixedsys font, beveled borders and the boot sequence " +
                    "of 16-bit Windows. It is a fun, nostalgic skin on top of Bootstrap 3.\n\n" +
                    "Everything you see here is standard Blazor, just with a different coat of paint.",
                IsPublished = true,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1),
            },
            new()
            {
                Title = "Markdown Cheatsheet",
                Slug = Post.MakeSlug("Markdown Cheatsheet"),
                Excerpt = "A quick tour of the Markdown subset this blog renders.",
                Content =
                    "# Heading 1\n\n" +
                    "## Heading 2\n\n" +
                    "You can write **bold**, *italic* and ~~struck~~ text.\n\n" +
                    "Inline `code` works, and so do fenced blocks:\n\n" +
                    "```\n" +
                    "public static void Hello() => Console.WriteLine(\"hi\");\n" +
                    "```\n\n" +
                    "- A list item\n" +
                    "- Another item\n" +
                    "  - Lists support nesting via indentation in the source (best-effort)\n\n" +
                    "1. Ordered\n" +
                    "2. Lists\n\n" +
                    "> This is a blockquote.\n" +
                    "> It can span multiple lines.\n\n" +
                    "Links: [visit Blazor](https://learn.microsoft.com/aspnet/core/blazor/)\n\n" +
                    "---\n\n" +
                    "Horizontal rules, headings and paragraphs all just work.",
                IsPublished = true,
                CreatedAt = now.AddHours(-6),
                UpdatedAt = now.AddHours(-6),
            },
            new()
            {
                Title = "Draft post (unpublished)",
                Slug = Post.MakeSlug("Draft post (unpublished)"),
                Excerpt = "This post is not published, so it only appears in the Manage area.",
                Content = "Unpublished drafts are hidden from the home page but visible in /manage.",
                IsPublished = false,
                CreatedAt = now,
                UpdatedAt = now,
            },
        };

        Posts.AddRange(posts);
        await SaveChangesAsync();

        // A sample threaded comment discussion on the Welcome post (Id == 1).
        var welcome = posts[0];
        var top = new Comment
        {
            PostId = welcome.Id,
            Author = "Alice",
            Email = "alice@example.com",
            Content = "Great intro! The 386 boot sequence is a lovely touch.",
            CreatedAt = now.AddHours(-20),
        };
        Comments.Add(top);
        await SaveChangesAsync();

        Comments.Add(new Comment
        {
            PostId = welcome.Id,
            ParentCommentId = top.Id,
            Author = "Bob",
            Email = "bob@example.com",
            Content = "Agreed — and Markdown support makes writing comments fun.",
            CreatedAt = now.AddHours(-19),
        });
        await SaveChangesAsync();
    }
}
