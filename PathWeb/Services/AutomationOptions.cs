namespace PathWeb.Services;

public class AutomationOptions
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroupName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2023-11-01";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SubscriptionId) &&
        !string.IsNullOrWhiteSpace(ResourceGroupName) &&
        !string.IsNullOrWhiteSpace(AccountName) &&
        !string.IsNullOrWhiteSpace(Location);
}
