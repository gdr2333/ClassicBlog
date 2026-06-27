using System.ComponentModel.DataAnnotations;

namespace ClassicBlog.Models;

/// <summary>Simple per-path visit counter (e.g. "home", "post:3").</summary>
public class PageView
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string Path { get; set; } = string.Empty;

    public long Count { get; set; }
}
