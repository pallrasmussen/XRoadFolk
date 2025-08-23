using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace XRoadFolkWeb.Validation
{
    public sealed class SsnAttribute : ValidationAttribute, IClientModelValidator
    {
        public SsnAttribute()
        {
            ErrorMessageResourceType = typeof(global::XRoadFolkWeb.Resources.ValidationMessages);
            ErrorMessageResourceName = "Ssn_Invalid";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext context)
        {
            var s = value as string;
            return XRoadFolkRaw.Lib.InputValidation.LooksLikeValidSsn(s, out _)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(context.DisplayName));
        }

        public void AddValidation(ClientModelValidationContext ctx)
        {
            ctx.Attributes["data-val"] = "true";
            ctx.Attributes["data-val-ssn"] = ErrorMessageString; // localized via resx
        }
    }
}