using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class Tenant
{
    public Guid TenantGuid { get; set; }

    public string Lab { get; set; } = null!;

    public short TenantId { get; set; }

    public short TenantVersion { get; set; }

    public string NinjaOwner { get; set; } = null!;

    public string? Contacts { get; set; }

    public DateOnly ReturnDate { get; set; }

    public string Usage { get; set; } = null!;

    public string? AzureRegion { get; set; }

    public string? Ersku { get; set; }

    public int? Erspeed { get; set; }

    public string? EruplinkPort { get; set; }

    public bool? PvtPeering { get; set; }

    public string? ErgatewaySize { get; set; }

    public bool? ErfastPath { get; set; }

    public bool? Msftpeering { get; set; }

    public string? Msftp2p { get; set; }

    public string? Msftadv { get; set; }

    public string? Msfttags { get; set; }

    public Guid? Skey { get; set; }

    public string? Vpngateway { get; set; }

    public bool? Vpnbgp { get; set; }

    public string? Vpnconfig { get; set; }

    public string? VpnendPoint { get; set; }

    public string? AzVm1 { get; set; }

    public string? AzVm2 { get; set; }

    public string? AzVm3 { get; set; }

    public string? AzVm4 { get; set; }

    public string? AddressFamily { get; set; }

    public string? LabVm1 { get; set; }

    public string? LabVm2 { get; set; }

    public string? LabVm3 { get; set; }

    public string? LabVm4 { get; set; }

    public int? WorkItemId { get; set; }

    public DateTime AssignedDate { get; set; }

    public string AssignedBy { get; set; } = null!;

    public DateTime? DeletedDate { get; set; }

    public string? DeletedBy { get; set; }

    public DateTime LastUpdateDate { get; set; }

    public string LastUpdateBy { get; set; } = null!;
}
