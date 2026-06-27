using System.ComponentModel.DataAnnotations;

namespace ClassicBlog.Models;

public class ApplicationUser
{
    public int Id { get; set; }

    [Required, StringLength(64)]
    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash produced by ASP.NET Core's PasswordHasher.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="Roles"/>.</summary>
    [Required, StringLength(32)]
    public string Role { get; set; } = Roles.Author;

    /// <summary>Optional, used as the destination for "new comment" notifications on the user's posts.</summary>
    [EmailAddress, StringLength(256)]
    public string? Email { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Application roles. Stored as strings on <see cref="ApplicationUser.Role"/>.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Author = "Author";

    public static readonly string[] All = { Admin, Author };
}
