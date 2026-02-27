using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Models;

[ModelMetadataType(typeof(PublicIpMetaData))]
public partial class PublicIp
{
}

public class PublicIpMetaData
{
    [Display(Name = "Range ID")]
    public int RangeId { get; set; }

    [Required]
    public string Lab { get; set; } = null!;

    [Required]
    public string Range { get; set; } = null!;

    [Display(Name = "Range Type")]
    [Required]
    public string RangeType { get; set; } = null!;

    [Display(Name = "Tenant GUID")]
    public Guid? TenantGuid { get; set; }

    [Display(Name = "Tenant Number")]
    [ValidIpTenant]
    public short? TenantId { get; set; }

    [Display(Name = "Date Assigned")]
    [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:u}")]
    public DateTime? AssignedDate { get; set; }

    [Display(Name = "Assigned By")]
    [ValidIpAssigned]
    [ValidIpNoData]
    public string? AssignedBy { get; set; }
}

/// <summary>
/// If any optional fields have data, Purpose, Date Assigned, and Assigned By are required.
/// </summary>
public class ValidIpAssignedAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (PublicIp)validationContext.ObjectInstance;
        var hasAnyData = !string.IsNullOrEmpty(model.Device)
                      || !string.IsNullOrEmpty(model.Purpose)
                      || model.TenantGuid.HasValue
                      || model.TenantId.HasValue
                      || model.AssignedDate.HasValue
                      || !string.IsNullOrEmpty(model.AssignedBy);

        if (hasAnyData && (string.IsNullOrEmpty(model.Purpose) || !model.AssignedDate.HasValue || string.IsNullOrEmpty(model.AssignedBy)))
        {
            return new ValidationResult("If any optional fields contain data, Purpose, Date Assigned, and Assigned By fields must be entered.");
        }
        return ValidationResult.Success;
    }
}

/// <summary>
/// If no optional fields have data, Date Assigned and Assigned By must also be blank.
/// </summary>
public class ValidIpNoDataAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (PublicIp)validationContext.ObjectInstance;
        var hasNoOptionalData = string.IsNullOrEmpty(model.Device)
                             && string.IsNullOrEmpty(model.Purpose)
                             && !model.TenantGuid.HasValue
                             && !model.TenantId.HasValue;

        if (hasNoOptionalData && (model.AssignedDate.HasValue || !string.IsNullOrEmpty(model.AssignedBy)))
        {
            return new ValidationResult("If no optional fields contain data, Date Assigned and Assigned By fields must be blank as well.");
        }
        return ValidationResult.Success;
    }
}

/// <summary>
/// If either Tenant GUID or Tenant Number has data, both must have data.
/// </summary>
public class ValidIpTenantAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (PublicIp)validationContext.ObjectInstance;
        var hasGuid = model.TenantGuid.HasValue;
        var hasId = model.TenantId.HasValue;

        if (hasGuid != hasId)
        {
            return new ValidationResult("If either Tenant GUID or Number contain data, both fields require data.");
        }
        return ValidationResult.Success;
    }
}
