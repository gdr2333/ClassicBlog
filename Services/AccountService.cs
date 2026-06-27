using ClassicBlog.Data;
using ClassicBlog.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClassicBlog.Services;

/// <summary>
/// User account operations: credential validation, creation, password management,
/// role assignment, and deletion. Passwords are hashed with ASP.NET Core's
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2). No self-registration — every user
/// is created by an admin via <see cref="CreateAsync"/>.
/// </summary>
public class AccountService(BlogDbContext db, PasswordHasher<ApplicationUser> hasher)
{
    public Task<ApplicationUser?> FindByNameAsync(string username) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);

    public async Task<ApplicationUser?> ValidateAsync(string username, string password)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
        if (user is null) return null;

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }

    public async Task<ApplicationUser> CreateAsync(string username, string password, string role)
    {
        var user = new ApplicationUser
        {
            Username = username.Trim(),
            Role = role,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Verifies the current password, then sets a new one. Returns false if the current password is wrong.</summary>
    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return false;

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return false;

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Admin-driven password reset (no current password required).</summary>
    public async Task ResetPasswordAsync(int userId, string newPassword)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new InvalidOperationException("User not found.");
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync();
    }

    public async Task SetRoleAsync(int userId, string role)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new InvalidOperationException("User not found.");
        user.Role = role;
        await db.SaveChangesAsync();
    }

    public async Task SetEmailAsync(int userId, string? email)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new InvalidOperationException("User not found.");
        user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is not null)
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }
}
