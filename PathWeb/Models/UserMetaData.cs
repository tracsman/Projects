using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Models;

[ModelMetadataType(typeof(UserMetaData))]
public partial class User
{
}

public class UserMetaData
{
    [Display(Name = "User Email")]
    [Required(ErrorMessage = "User Email is required.")]
    [EmailAddress(ErrorMessage = "Must be a valid email address.")]
    [MicrosoftEmail]
    public string UserName { get; set; } = null!;

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = null!;

    [Display(Name = "Authorization Level")]
    [Required]
    public byte AuthLevel { get; set; }
}

/// <summary>
/// Validates that the email address ends with @microsoft.com
/// </summary>
public class MicrosoftEmailAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string email && !string.IsNullOrWhiteSpace(email))
        {
            if (!email.EndsWith("@microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                return new ValidationResult("Email must be in the format alias@microsoft.com");
            }
        }
        return ValidationResult.Success;
    }
}
