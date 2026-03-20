namespace PathWeb.Models;

public class TenantIndexViewModel
{
    public IReadOnlyList<Tenant> Tenants { get; set; } = [];

    public bool ShowReleased { get; set; }

    public string? SortOrder { get; set; }

    public string? SearchString { get; set; }

    public int CurrentPage { get; set; }

    public int TotalPages { get; set; }

    public int TotalCount { get; set; }

    public int PageSize { get; set; }
}
