using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace XRoadFolkWeb.Validation
{
    public sealed class NameAttribute : ValidationAttribute, IClientModelValidator
    {
        private string? _messageKey;

        // Set a specific resource key per usage, e.g., "FirstName_Invalid" or "LastName_Invalid"
        public string? MessageKey
        {
            get => _messageKey;
            set
            {
                _messageKey = value;
                ErrorMessageResourceName = value ?? "Name_Invalid";
            }
        }

        public NameAttribute()
        {
            ErrorMessageResourceType = typeof(global::XRoadFolkWeb.Resources.ValidationMessages);
            ErrorMessageResourceName = "Name_Invalid";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
        {
            string? s = value as string;
            if (string.IsNullOrWhiteSpace(s)) return ValidationResult.Success; // not [Required]
            return XRoadFolkRaw.Lib.InputValidation.IsValidName(s)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(ctx.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext ctx)
        {
            ctx.Attributes["data-val"] = "true";
            ctx.Attributes["data-val-name"] = ErrorMessageString; // localized via resx
        }
    }
}