using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

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

            string? s = value as string;

            // Allow empty SSN (it's optional). The cross-field rule decides if SSN is required.
            if (string.IsNullOrWhiteSpace(s))
            {
                // Allow empty SSN (it's optional). The cross-field rule decides if SSN is required.
                return ValidationResult.Success;
            }

            if (XRoadFolkRaw.Lib.InputValidation.LooksLikeValidSsn(s, out _))
            {
                // Allow empty SSN (it's optional). The cross-field rule decides if SSN is required.
                return ValidationResult.Success;
            }

            // Allow empty SSN (it's optional). The cross-field rule decides if SSN is required.
            return new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.Attributes["data-val"] = "true";
            context.Attributes["data-val-ssn"] = ErrorMessageString; // localized via resx
        }
    }
}