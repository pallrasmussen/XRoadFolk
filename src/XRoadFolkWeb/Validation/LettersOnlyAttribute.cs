using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace XRoadFolkWeb.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class LettersOnlyAttribute : ValidationAttribute, IClientModelValidator
    {
        public LettersOnlyAttribute()
        {
            // Reuse your localized message
            ErrorMessageResourceType = typeof(global::XRoadFolkWeb.Resources.ValidationMessages);
            ErrorMessageResourceName = "Name_Invalid";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
        {
            string? s = value as string;
            if (string.IsNullOrWhiteSpace(s)) return ValidationResult.Success; // not [Required]
            // Accept letters, spaces, apostrophes, hyphens (incl. Latin-1 letters)
            return System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-zÀ-ÖØ-öø-ÿ' -]+$") 
                ? ValidationResult.Success 
                : new ValidationResult(FormatErrorMessage(ctx.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext ctx)
        {
            ctx.Attributes["data-val"] = "true";
            ctx.Attributes["data-val-lettersonly"] = ErrorMessageString;
        }
    }
}