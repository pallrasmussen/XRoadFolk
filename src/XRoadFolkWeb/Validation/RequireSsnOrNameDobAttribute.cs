using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Localization;
using XRoadFolkRaw.Lib;

namespace XRoadFolkWeb.Validation
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RequireSsnOrNameDobAttribute : ValidationAttribute
    {
        private readonly string _ssnProp;
        private readonly string _firstProp;
        private readonly string _lastProp;
        private readonly string _dobProp;

        public RequireSsnOrNameDobAttribute(string ssnProperty, string firstNameProperty, string lastNameProperty, string dobProperty)
        {
            _ssnProp = ssnProperty;
            _firstProp = firstNameProperty;
            _lastProp = lastNameProperty;
            _dobProp = dobProperty;
            ErrorMessage = "Provide SSN or First/Last name with DOB.";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
        {
            if (value is null) return ValidationResult.Success;

            string? ssn = Get<string?>(ctx, _ssnProp);
            string? first = Get<string?>(ctx, _firstProp);
            string? last = Get<string?>(ctx, _lastProp);
            string? dobInput = Get<string?>(ctx, _dobProp);

            bool haveSsn = InputValidation.LooksLikeValidSsn(ssn, out var ssnDob);
            bool haveNames = InputValidation.IsValidName(first) && InputValidation.IsValidName(last);
            bool haveDob = InputValidation.TryParseDob(dobInput, out var dob);

            // Localize via IStringLocalizer<InputValidation> if available
            var loc = (IStringLocalizer<InputValidation>?)ctx.GetService(typeof(IStringLocalizer<InputValidation>));

            if (!haveSsn && !(haveNames && haveDob))
            {
                string msg = loc?[InputValidation.Errors.ProvideSsnOrNameDob] ?? ErrorMessage!;
                return new ValidationResult(msg, new[] { _ssnProp, _firstProp, _lastProp, _dobProp });
            }

            if (haveSsn && haveDob && ssnDob.HasValue && dob.HasValue && ssnDob.Value.Date != dob.Value.Date)
            {
                string msg = loc?[InputValidation.Errors.DobSsnMismatch, dob.Value.ToString("yyyy-MM-dd"), ssnDob.Value.ToString("yyyy-MM-dd")]
                             ?? "DOB does not match SSN.";
                return new ValidationResult(msg, new[] { _ssnProp, _dobProp });
            }

            return ValidationResult.Success;
        }

        private static T? Get<T>(ValidationContext ctx, string name)
        {
            var pi = ctx.ObjectType.GetProperty(name);
            if (pi is null) return default;
            return (T?)pi.GetValue(ctx.ObjectInstance);
        }
    }
}