namespace PathWeb.Services;

/// <summary>
/// Authorization levels matching the old PathWeb system.
/// Each level automatically includes all lower level privileges.
/// </summary>
public enum AuthLevels : byte
{
    Reject = 0,
    WorkshopRegistration = 1,
    WorkshopReadOnly = 3,
    WorkshopAdmin = 5,
    TenantReadOnly = 6,
    TenantAdmin = 8,
    DataAdminReadOnly = 11,
    DataAdmin = 12,
    SiteAdminReadOnly = 14,
    SiteAdmin = 15
}
