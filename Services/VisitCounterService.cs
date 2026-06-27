using ClassicBlog.Data;
using ClassicBlog.Models;
using Microsoft.EntityFrameworkCore;

namespace ClassicBlog.Services;

/// <summary>
/// Simple visit counter backed by the <see cref="PageView"/> table. Increments are
/// atomic (<c>Count = Count + 1</c> via <see cref="ExecuteUpdateAsync"/>) to avoid
/// lost updates under concurrency.
/// </summary>
public class VisitCounterService(BlogDbContext db)
{
    public async Task<long> GetAsync(string path) =>
        await db.PageViews.AsNoTracking()
            .Where(p => p.Path == path)
            .Select(p => p.Count)
            .FirstOrDefaultAsync();

    public async Task IncrementAsync(string path)
    {
        // Create the row on first visit; tolerate the race where another request
        // created it concurrently (unique index on Path).
        if (!await db.PageViews.AnyAsync(p => p.Path == path))
        {
            db.PageViews.Add(new PageView { Path = path, Count = 1 });
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException)
            {
                // Another request created it first — fall through to an atomic increment.
            }
        }

        await db.PageViews
            .Where(p => p.Path == path)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Count, p => p.Count + 1));
    }
}
