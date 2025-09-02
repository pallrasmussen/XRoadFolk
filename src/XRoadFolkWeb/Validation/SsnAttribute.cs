using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace XRoadFolkWeb.Validation
{
    public sealed class SsnAttribute : ValidationAttribute, IClientModelValidator
    {
        public SsnAttribute()
        {
            ErrorMessageResourceType = typeof(Resources.ValidationMessages);
            ErrorMessageResourceName = "Ssn_Invalid";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            ArgumentNullException.ThrowIfNull(validationContext);

            // SSN is optional; cross-field rules decide if it is required
            if (value is not string s || string.IsNullOrWhiteSpace(s))
            {
                return ValidationResult.Success;
            }

            return XRoadFolkRaw.Lib.InputValidation.LooksLikeValidSsn(s, out _)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.Attributes["data-val"] = "true";
            context.Attributes["data-val-ssn"] = ErrorMessageString; // localized via resx
        }
    }
}