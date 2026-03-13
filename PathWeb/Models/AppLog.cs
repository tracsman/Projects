namespace PathWeb.Models;

public class AppLog
{
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }

    public string Level { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? UserName { get; set; }
}
