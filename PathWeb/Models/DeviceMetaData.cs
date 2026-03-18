using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Models;

[ModelMetadataType(typeof(DeviceMetaData))]
public partial class Device
{
}

public class DeviceMetaData
{
    [Display(Name = "Device ID")]
    public Guid DeviceId { get; set; }

    [Display(Name = "Type")]
    [Required]
    public string Type { get; set; } = null!;

    [Display(Name = "Name")]
    [Required]
    public string Name { get; set; } = null!;

    [Display(Name = "Lab")]
    [Required]
    public string Lab { get; set; } = null!;

    [Display(Name = "Mgmt IPv4")]
    [ValidIPv4]
    [StringLength(15)]
    public string? MgmtIpv4 { get; set; }

    [Display(Name = "Mgmt IPv6")]
    [ValidIPv6]
    [StringLength(45)]
    public string? MgmtIpv6 { get; set; }

    [Display(Name = "OS")]
    [StringLength(50, ErrorMessage = "OS must be 50 characters or fewer.")]
    public string? Os { get; set; }

    [Display(Name = "In Service")]
    public bool InService { get; set; }

    [Display(Name = "Issues")]
    public string? Issues { get; set; }
}

public class ValidIPv4Attribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string address || string.IsNullOrWhiteSpace(address))
            return ValidationResult.Success;

        if (IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            return ValidationResult.Success;

        return new ValidationResult("Enter a valid IPv4 address (e.g. 10.0.0.1)");
    }
}

public class ValidIPv6Attribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string address || string.IsNullOrWhiteSpace(address))
            return ValidationResult.Success;

        if (IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ValidationResult.Success;

        return new ValidationResult("Enter a valid IPv6 address (e.g. fd00::1)");
    }
}
