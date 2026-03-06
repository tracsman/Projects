using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Models;

[ModelMetadataType(typeof(TenantRequestMetaData))]
public partial class TenantRequest
{
}

public class TenantRequestMetaData
{
    [Display(Name = "Request ID")]
    public Guid RequestId { get; set; }

    public string Status { get; set; } = null!;

    [Display(Name = "Requested")]
    [DisplayFormat(DataFormatString = "{0:u}")]
    public DateTime RequestedDate { get; set; }

    [Display(Name = "Requested By")]
    public string RequestedBy { get; set; } = null!;

    [Display(Name = "Lab")]
    [Required]
    public string Lab { get; set; } = null!;

    [Display(Name = "Contact(s)")]
    [Required]
    public string? Contacts { get; set; }

    [Display(Name = "Usage")]
    [Required]
    [MinLength(7, ErrorMessage = "Usage must be a minimum length of {1} characters, try a little harder please!")]
    public string Usage { get; set; } = null!;

    [Display(Name = "Azure Region")]
    [Required]
    public string? AzureRegion { get; set; }

    [Display(Name = "ER SKU")]
    [Required]
    public string? Ersku { get; set; }

    [Display(Name = "ER Speed")]
    [Required]
    public int? Erspeed { get; set; }

    [Display(Name = "Uplink Port")]
    [Required]
    public string? EruplinkPort { get; set; }

    [Display(Name = "Private Peering")]
    [Required]
    public bool? PvtPeering { get; set; }

    [Display(Name = "ER Gateway Size")]
    [Required]
    public string? ErgatewaySize { get; set; }

    [Display(Name = "ER Fast Path")]
    [Required]
    public bool? ErfastPath { get; set; }

    [Display(Name = "MSFT Peering")]
    [Required]
    public bool? Msftpeering { get; set; }

    [Display(Name = "VPN Gateway")]
    [Required]
    public string? Vpngateway { get; set; }

    [Display(Name = "VPN BGP")]
    public bool? Vpnbgp { get; set; }

    [Display(Name = "VPN Config")]
    public string? Vpnconfig { get; set; }

    [Display(Name = "Address Family")]
    public string? AddressFamily { get; set; }

    [Display(Name = "Azure VM 1")]
    public string? AzVm1 { get; set; }

    [Display(Name = "Azure VM 2")]
    public string? AzVm2 { get; set; }

    [Display(Name = "Azure VM 3")]
    public string? AzVm3 { get; set; }

    [Display(Name = "Azure VM 4")]
    public string? AzVm4 { get; set; }

    [Display(Name = "Lab VM 1")]
    public string? LabVm1 { get; set; }

    [Display(Name = "Lab VM 2")]
    public string? LabVm2 { get; set; }

    [Display(Name = "Lab VM 3")]
    public string? LabVm3 { get; set; }

    [Display(Name = "Lab VM 4")]
    public string? LabVm4 { get; set; }

    [Display(Name = "Reviewed")]
    [DisplayFormat(DataFormatString = "{0:u}")]
    public DateTime? ReviewedDate { get; set; }

    [Display(Name = "Reviewed By")]
    public string? ReviewedBy { get; set; }

    [Display(Name = "Review Notes")]
    public string? ReviewNotes { get; set; }

    [Display(Name = "Tenant GUID")]
    public Guid? TenantGuid { get; set; }
}
