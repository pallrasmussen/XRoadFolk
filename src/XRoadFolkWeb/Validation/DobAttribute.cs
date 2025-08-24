using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace XRoadFolkWeb.Validation
{
    public sealed class DobAttribute : ValidationAttribute, IClientModelValidator
    {
        public DobAttribute()
        {
            ErrorMessageResourceType = typeof(global::XRoadFolkWeb.Resources.ValidationMessages);
            ErrorMessageResourceName = "Dob_Format";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
        {
            string? s = value as string;
            if (string.IsNullOrWhiteSpace(s)) return ValidationResult.Success; // not [Required]
            return XRoadFolkRaw.Lib.InputValidation.TryParseDob(s, out _)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(ctx.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext ctx)
        {
            ctx.Attributes["data-val"] = "true";
            ctx.Attributes["data-val-dob"] = ErrorMessageString; // localized via resx
        }
    }
}