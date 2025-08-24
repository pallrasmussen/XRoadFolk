using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace XRoadFolkWeb.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class LettersOnlyAttribute : ValidationAttribute, IClientModelValidator
    {
        // Allow: Unicode letters and combining marks, spaces, straight ' and curly ’ apostrophes, and hyphen
        private static readonly Regex LettersRegex = new(
            @"^[\p{L}\p{M}\s\-'\u2019]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

        public LettersOnlyAttribute()
        {
            // Reuse your localized message
            ErrorMessageResourceType = typeof(global::XRoadFolkWeb.Resources.ValidationMessages);
            ErrorMessageResourceName = "Name_Invalid";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
        {
            var s = value as string;
            if (string.IsNullOrWhiteSpace(s)) return ValidationResult.Success; // not [Required]
            s = s.Trim();
            // Unicode-aware match; avoids unintended hyphen ranges
            return LettersRegex.IsMatch(s)
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