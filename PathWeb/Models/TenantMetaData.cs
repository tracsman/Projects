using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Models;

[ModelMetadataType(typeof(TenantMetaData))]
public partial class Tenant
{
}

public class TenantMetaData
{
    [Display(Name = "Tenant GUID")]
    public Guid TenantGuid { get; set; }

    [Display(Name = "On-prem Lab")]
    [Required]
    [ValidColo]
    public string Lab { get; set; } = null!;

    [Display(Name = "Tenant ID")]
    [Required]
    public short TenantId { get; set; }

    [Display(Name = "Server Preference")]
    [Required]
    [ValidServerPreference]
    public short TenantVersion { get; set; }

    [Display(Name = "Ninja")]
    [Required]
    public string NinjaOwner { get; set; } = null!;

    [Display(Name = "Contact(s)")]
    [Required]
    [ValidContacts]
    public string? Contacts { get; set; }

    [Display(Name = "Return Date")]
    [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:yyyy-MM-dd}")]
    [Required]
    public DateOnly ReturnDate { get; set; }

    [Display(Name = "Usage")]
    [Required]
    [MinLength(7, ErrorMessage = "Usage must be a minimum length of {1} characters, try a little harder please!")]
    public string Usage { get; set; } = null!;

    [Display(Name = "Azure Region")]
    [Required]
    public string? AzureRegion { get; set; }

    [Display(Name = "ER SKU")]
    [Required]
    [ValidErSku]
    public string? Ersku { get; set; }

    [Display(Name = "ER Speed")]
    [Required]
    [ValidErSpeed]
    public int? Erspeed { get; set; }

    [Display(Name = "Uplink Port")]
    [Required]
    [ValidErUplink]
    public string? EruplinkPort { get; set; }

    [Display(Name = "Private Peering")]
    [Required]
    [ValidErPrivate]
    public bool? PvtPeering { get; set; }

    [Display(Name = "ER Gateway Size")]
    [Required]
    [ValidErGateway]
    public string? ErgatewaySize { get; set; }

    [Display(Name = "ER Fast Path")]
    [Required]
    [ValidErFastPath]
    public bool? ErfastPath { get; set; }

    [Display(Name = "MSFT Peering")]
    [Required]
    [ValidErMicrosoft]
    public bool? Msftpeering { get; set; }

    [Display(Name = "MSFT P2P")]
    public string? Msftp2p { get; set; }

    [Display(Name = "MSFT NAT(s)")]
    public string? Msftadv { get; set; }

    [Display(Name = "BGP Communities")]
    [ValidCommunities]
    public string? Msfttags { get; set; }

    [Display(Name = "Service Key")]
    [ValidSKey]
    public Guid? Skey { get; set; }

    [Display(Name = "VPN Gateway Size")]
    [Required]
    [ValidVpnGw]
    public string? Vpngateway { get; set; }

    [Display(Name = "BGP")]
    public bool? Vpnbgp { get; set; }

    [Display(Name = "VPN Config")]
    public string? Vpnconfig { get; set; }

    [Display(Name = "VPN End Point(s)")]
    [ValidVpnEp]
    public string? VpnendPoint { get; set; }

    [Display(Name = "Azure VM 1")]
    public string? AzVm1 { get; set; }

    [Display(Name = "Azure VM 2")]
    public string? AzVm2 { get; set; }

    [Display(Name = "Azure VM 3")]
    public string? AzVm3 { get; set; }

    [Display(Name = "Azure VM 4")]
    public string? AzVm4 { get; set; }

    [Display(Name = "Address Family")]
    [ValidAddressFamily]
    public string? AddressFamily { get; set; }

    [Display(Name = "On-prem VM 1")]
    [ValidLabVm1]
    public string? LabVm1 { get; set; }

    [Display(Name = "On-prem VM 2")]
    [ValidLabVm2]
    public string? LabVm2 { get; set; }

    [Display(Name = "On-prem VM 3")]
    [ValidLabVm3]
    public string? LabVm3 { get; set; }

    [Display(Name = "On-prem VM 4")]
    [ValidLabVm4]
    public string? LabVm4 { get; set; }

    [Display(Name = "Work Item ID")]
    public int? WorkItemId { get; set; }

    [Display(Name = "Assigned Date")]
    public DateTime AssignedDate { get; set; }

    [Display(Name = "Assigned By")]
    public string AssignedBy { get; set; } = null!;

    [Display(Name = "Released Date")]
    public DateTime? DeletedDate { get; set; }

    [Display(Name = "Released By")]
    public string? DeletedBy { get; set; }

    [Display(Name = "Last Update")]
    public DateTime LastUpdateDate { get; set; }

    [Display(Name = "Updated By")]
    public string LastUpdateBy { get; set; } = null!;
}

// --- Tenant Validators ---

public class ValidColoAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if (model.Lab != "SEA" && model.Lab != "ASH")
            return new ValidationResult("Invalid lab value, please select either Seattle or Ashburn.");
        return ValidationResult.Success;
    }
}

public class ValidServerPreferenceAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if (model.TenantId == 0 && (model.TenantVersion > 6 || model.TenantVersion < 0))
            return new ValidationResult("Pick 0 - 6");
        return ValidationResult.Success;
    }
}

public class ValidContactsAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if (string.IsNullOrEmpty(model.Contacts)) return ValidationResult.Success;

        var contacts = model.Contacts.Split(';');
        var emailAttr = new EmailAddressAttribute();
        foreach (var contact in contacts)
        {
            if (!emailAttr.IsValid(contact.Trim()))
                return new ValidationResult("Ensure each email is the full address, for multiple addresses separate with ;");
        }
        return ValidationResult.Success;
    }
}

public class ValidErSkuAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.Ersku != "None")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, ER SKU must be set to None.");

        if (model.Ersku == "Local" && model.EruplinkPort == "ECX")
            return new ValidationResult("ExpressRoute Local is not valid on ECX connections, change SKU or Uplink value.");

        return ValidationResult.Success;
    }
}

public class ValidErSpeedAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.Erspeed != 50)
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, ER Speed must be set to 50 Mbps.");
        if (model.Ersku == "None" && model.Erspeed != 50)
            return new ValidationResult("With ER SKU equal to None, ER Speed must be set to 50 Mbps.");
        if (model.Ersku != "None" && model.EruplinkPort != "ECX" && model.Erspeed < 1000)
            return new ValidationResult("A Direct ExpressRoute circuit speed must be 1 Gbps or greater.");
        if (model.Ersku != "None" && model.EruplinkPort == "ECX" && model.Erspeed > 10000)
            return new ValidationResult("An ExpressRoute circuit (non-direct) speed must be 10 Gbps or lower.");
        return ValidationResult.Success;
    }
}

public class ValidErUplinkAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.EruplinkPort != "ECX")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, Uplink Port must be set to ECX.");
        if (model.Ersku == "None" && model.EruplinkPort != "ECX")
            return new ValidationResult("With ER SKU equal to None, Uplink Port must be set to ECX.");
        return ValidationResult.Success;
    }
}

public class ValidErPrivateAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.PvtPeering == true)
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, Private Peering must be set to Not Enabled.");
        if (model.Ersku == "None" && model.PvtPeering == true)
            return new ValidationResult("With ER SKU equal to None, Private Peering must be set to Not Enabled.");
        return ValidationResult.Success;
    }
}

public class ValidErGatewayAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.ErgatewaySize != "None")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, ER Gateway Size must be set to None.");
        if ((model.Ersku == "None" || model.PvtPeering == false) && model.ErgatewaySize != "None")
            return new ValidationResult("With ER SKU equal to None or Private Peering not enabled, ER Gateway Size must be set to None.");

        if (model.Ersku != "None" && model.PvtPeering == true
            && (model.AddressFamily == "Dual" || model.AddressFamily == "IPv6")
            && (model.ErgatewaySize == "None" || model.ErgatewaySize?.Contains("AZ") != true))
            return new ValidationResult("An AZ aware gateway must be used when Dual/IPv6 is selected.");

        return ValidationResult.Success;
    }
}

public class ValidErFastPathAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.ErfastPath == true)
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, ER Fast Path must be set to Not Enabled.");
        if ((model.Ersku == "None" || model.PvtPeering == false) && model.ErfastPath == true)
            return new ValidationResult("With ER SKU equal to None or Private Peering not enabled, ER Fast Path must be set to Not Enabled.");
        if (model.Ersku != "None" && model.PvtPeering == true && model.ErfastPath == true
            && model.ErgatewaySize != "UltraPerformance" && model.ErgatewaySize != "ErGw3AZ")
            return new ValidationResult("An UltraPerformance or ErGw3AZ gateway must be used when FastPath is selected.");
        return ValidationResult.Success;
    }
}

public class ValidErMicrosoftAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.Msftpeering == true)
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, Microsoft Peering must be set to Not Enabled.");
        if (model.Ersku == "None" && model.Msftpeering == true)
            return new ValidationResult("With ER SKU equal to None, Microsoft Peering must be set to Not Enabled.");
        return ValidationResult.Success;
    }
}

public class ValidCommunitiesAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && !string.IsNullOrEmpty(model.Msfttags))
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, BGP Communities must be empty.");
        if ((model.Ersku == "None" || model.Msftpeering == false) && !string.IsNullOrEmpty(model.Msfttags))
            return new ValidationResult("With ER SKU equal to None or Microsoft Peering not enabled, BGP Communities must be empty.");
        return ValidationResult.Success;
    }
}

public class ValidSKeyAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.Skey.HasValue)
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, Service Key must be empty.");
        if (model.Ersku == "None" && model.Skey.HasValue)
            return new ValidationResult("With ER SKU equal to None, Service Key must be empty.");
        return ValidationResult.Success;
    }
}

public class ValidVpnGwAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.Vpngateway != "None")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, VPN Gateway Size must be None.");
        return ValidationResult.Success;
    }
}

public class ValidVpnEpAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) || model.Vpngateway == "None")
        {
            model.VpnendPoint = "TBD,N/A";
        }
        return ValidationResult.Success;
    }
}

public class ValidAddressFamilyAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if (model.AddressFamily == "IPv6")
            return new ValidationResult("\"IPv6 only\" is not currently a valid option, please select Dual stack to deploy with both IPv4 and IPv6.");
        return ValidationResult.Success;
    }
}

public class ValidLabVm1Attribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.LabVm1 != "None")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, On-prem VM 1 must be None.");
        return ValidationResult.Success;
    }
}

public class ValidLabVm2Attribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.LabVm2 != "None")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, On-prem VM 2 must be None.");
        return ValidationResult.Success;
    }
}

public class ValidLabVm3Attribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.LabVm3 != "None")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, On-prem VM 3 must be None.");
        return ValidationResult.Success;
    }
}

public class ValidLabVm4Attribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (Tenant)validationContext.ObjectInstance;
        if ((model.TenantId >= 100 || model.TenantId == 1) && model.LabVm4 != "None")
            return new ValidationResult("Tenant IDs greater than or equal to 100 can't have lab resources, On-prem VM 4 must be None.");
        return ValidationResult.Success;
    }
}
