using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using XRoadFolkRaw.Lib;

namespace XRoadFolkWeb.Validation
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RequireSsnOrNameDobAttribute : ValidationAttribute
    {
        private readonly string _ssn;
        private readonly string _first;
        private readonly string _last;
        private readonly string _dob;

        public RequireSsnOrNameDobAttribute(string ssnProperty, string firstNameProperty, string lastNameProperty, string dobProperty)
        {
            _ssn = ssnProperty ?? throw new ArgumentNullException(nameof(ssnProperty));
            _first = firstNameProperty ?? throw new ArgumentNullException(nameof(firstNameProperty));
            _last = lastNameProperty ?? throw new ArgumentNullException(nameof(lastNameProperty));
            _dob = dobProperty ?? throw new ArgumentNullException(nameof(dobProperty));

            // Fallback message if no localizer is available.
            ErrorMessage = "Provide SSN or First/Last name with DOB.";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            ArgumentNullException.ThrowIfNull(validationContext);

            if (value is null)
            {
                return ValidationResult.Success;
            }

            string? ssn = GetString(validationContext.ObjectInstance, _ssn);
            string? first = GetString(validationContext.ObjectInstance, _first);
            string? last = GetString(validationContext.ObjectInstance, _last);
            string? dob = GetString(validationContext.ObjectInstance, _dob);

            // 1) If SSN is not empty, use SSN (let field-level validators handle its format)
            if (!string.IsNullOrWhiteSpace(ssn))
            {
                return ValidationResult.Success;
            }

            // 2) If SSN is empty, require First + Last + DOB (presence only; per-field validators check validity)
            bool hasAll = !string.IsNullOrWhiteSpace(first)
                          && !string.IsNullOrWhiteSpace(last)
                          && !string.IsNullOrWhiteSpace(dob);

            if (!hasAll)
            {
                IStringLocalizer<InputValidation>? loc =
                    (IStringLocalizer<InputValidation>?)validationContext.GetService(typeof(IStringLocalizer<InputValidation>));

                string msg = loc is not null
                    ? loc[InputValidation.Errors.ProvideSsnOrNameDob]
                    : (ErrorMessage ?? "Provide SSN or First/Last name with DOB.");

                // Attach to all involved members so summary and field highlights work.
                return new ValidationResult(msg, new[] { _ssn, _first, _last, _dob });
            }

            return ValidationResult.Success;
        }

        private static string? GetString(object instance, string propName)
        {
            System.Reflection.PropertyInfo? pi = instance.GetType().GetProperty(
                propName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase);

            return pi is null ? null : (string?)pi.GetValue(instance);
        }
    }
}