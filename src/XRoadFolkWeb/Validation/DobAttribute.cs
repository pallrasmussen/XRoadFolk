using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace XRoadFolkWeb.Validation
{
    public sealed class DobAttribute : ValidationAttribute, IClientModelValidator
    {
        public DobAttribute()
        {
            ErrorMessageResourceType = typeof(Resources.ValidationMessages);
            ErrorMessageResourceName = "Dob_Format";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            ArgumentNullException.ThrowIfNull(validationContext);

            string? s = value as string;
            if (string.IsNullOrWhiteSpace(s))
            {
                return ValidationResult.Success; // not [Required]
            }

            return XRoadFolkRaw.Lib.InputValidation.TryParseDob(s, out _)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(name: validationContext.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.Attributes["data-val"] = "true";
            context.Attributes["data-val-dob"] = ErrorMessageString; // localized via resx
        }
    }
}