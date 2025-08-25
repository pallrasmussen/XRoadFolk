using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace XRoadFolkWeb.Infrastructure.Validation
{
    // Server + client (unobtrusive) SSN validation
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SsnAttribute : ValidationAttribute, IClientModelValidator
    {
        public SsnAttribute()
        {
            ErrorMessage = "Enter a valid 9-digit SSN.";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            string input = (value?.ToString() ?? "").Trim();
            if (string.IsNullOrEmpty(input))
            {
                // Let [Required] (if present) handle empties
                return ValidationResult.Success;
            }

            string digits = StripNonDigits(input);
            if (!IsValidSsn(digits))
            {
                return new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
            }

            return ValidationResult.Success;
        }

        public void AddValidation(ClientModelValidationContext context)
        {
            // Enable jQuery Unobtrusive adapter 'ssn' (see validation-ssn.js)
            Merge(context.Attributes, "data-val", "true");
            Merge(context.Attributes, "data-val-ssn", ErrorMessage ?? "Enter a valid 9-digit SSN.");
        }

        private static void Merge(IDictionary<string, string> attrs, string key, string value)
        {
            if (!attrs.ContainsKey(key)) attrs.Add(key, value);
        }

        private static string StripNonDigits(string s)
        {
            Span<char> buf = stackalloc char[s.Length];
            int j = 0;
            foreach (char c in s)
                if (char.IsDigit(c)) buf[j++] = c;
            return new string(buf[..j]);
        }

        // Basic US SSN rules (format-only checks)
        internal static bool IsValidSsn(string digits)
        {
            if (digits.Length != 9) return false;

            // Reject obviously fake patterns
            if (AllSame(digits)) return false;

            int area = int.Parse(digits.AsSpan(0, 3));   // AAA
            int group = int.Parse(digits.AsSpan(3, 2));  // GG
            int serial = int.Parse(digits.AsSpan(5, 4)); // SSSS

            // Area cannot be 000, 666, or 900–999
            if (area == 0 || area == 666 || area >= 900) return false;

            // Group cannot be 00
            if (group == 0) return false;

            // Serial cannot be 0000
            if (serial == 0) return false;

            return true;
        }

        private static bool AllSame(string digits)
        {
            for (int i = 1; i < digits.Length; i++)
                if (digits[i] != digits[0]) return false;
            return true;
        }
    }
}