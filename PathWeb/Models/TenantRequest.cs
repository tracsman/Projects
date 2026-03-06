using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class TenantRequest
{
    public Guid RequestId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime RequestedDate { get; set; }

    public string RequestedBy { get; set; } = null!;

    public string Lab { get; set; } = null!;

    public string? Contacts { get; set; }

    public string Usage { get; set; } = null!;

    public string? AzureRegion { get; set; }

    public string? Ersku { get; set; }

    public int? Erspeed { get; set; }

    public string? EruplinkPort { get; set; }

    public bool? PvtPeering { get; set; }

    public string? ErgatewaySize { get; set; }

    public bool? ErfastPath { get; set; }

    public bool? Msftpeering { get; set; }

    public string? Vpngateway { get; set; }

    public bool? Vpnbgp { get; set; }

    public string? Vpnconfig { get; set; }

    public string? AddressFamily { get; set; }

    public string? AzVm1 { get; set; }

    public string? AzVm2 { get; set; }

    public string? AzVm3 { get; set; }

    public string? AzVm4 { get; set; }

    public string? LabVm1 { get; set; }

    public string? LabVm2 { get; set; }

    public string? LabVm3 { get; set; }

    public string? LabVm4 { get; set; }

    public DateTime? ReviewedDate { get; set; }

    public string? ReviewedBy { get; set; }

    public string? ReviewNotes { get; set; }

    public Guid? TenantGuid { get; set; }
}
