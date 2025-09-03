using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace XRoadFolkWeb.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class NameAttribute : ValidationAttribute, IClientModelValidator
    {
        private string? _messageKey;

        /// <summary>
        /// Set a specific resource key per usage, e.g., "FirstName_Invalid" or "LastName_Invalid"
        /// </summary>
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
            ErrorMessageResourceType = typeof(Resources.ValidationMessages);
            ErrorMessageResourceName = "Name_Invalid";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            ArgumentNullException.ThrowIfNull(validationContext);
            string? s = value as string;
            if (string.IsNullOrWhiteSpace(s))
            {
                return ValidationResult.Success; // not [Required]
            }

            return XRoadFolkRaw.Lib.InputValidation.IsValidName(s)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Attributes["data-val"] = "true";
            context.Attributes["data-val-name"] = ErrorMessageString; // localized via resx
        }
    }
}