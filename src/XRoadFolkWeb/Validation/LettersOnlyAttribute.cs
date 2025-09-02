using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace XRoadFolkWeb.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class LettersOnlyAttribute : ValidationAttribute, IClientModelValidator
    {
        /// <summary>
        /// Allow: Unicode letters and combining marks, spaces, straight ' and curly ’ apostrophes, and hyphen
        /// </summary>
        private static readonly Regex LettersRegex = new(
            @"^[\p{L}\p{M}\s\-'\u2019]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

        public LettersOnlyAttribute()
        {
            // Reuse your localized message
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

            s = s.Trim();
            // Unicode-aware match; avoids unintended hyphen ranges
            return LettersRegex.IsMatch(s)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Attributes["data-val"] = "true";
            context.Attributes["data-val-lettersonly"] = ErrorMessageString;
        }
    }
}