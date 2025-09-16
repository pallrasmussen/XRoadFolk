using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using XRoadFolkRaw.Lib;
using System.Collections.Concurrent;

namespace XRoadFolkWeb.Validation
{
    /// <summary>
    /// Cross-field validator that enforces either a non-empty SSN or the tuple FirstName+LastName+DateOfBirth.
    /// Uses compiled accessors cached per model type for performance. Supports localization via IStringLocalizer.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RequireSsnOrNameDobAttribute : ValidationAttribute
    {
        private readonly string _ssn;
        private readonly string _first;
        private readonly string _last;
        private readonly string _dob;

        // Per-attribute-instance cache of compiled accessors keyed by model Type
        private readonly ConcurrentDictionary<Type, AccessorSet> _accessorCache = new();

        private readonly struct AccessorSet
        {
            public AccessorSet(Func<object, string?> ssn, Func<object, string?> first, Func<object, string?> last, Func<object, string?> dob)
            {
                Ssn = ssn; First = first; Last = last; Dob = dob;
            }
            public Func<object, string?> Ssn { get; }
            public Func<object, string?> First { get; }
            public Func<object, string?> Last { get; }
            public Func<object, string?> Dob { get; }
        }

        /// <summary>
        /// Construct the attribute with property names to bind to on the target model.
        /// </summary>
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

            Type modelType = value.GetType();
            AccessorSet acc = GetAccessors(modelType);

            string? ssn = acc.Ssn(value);
            string? first = acc.First(value);
            string? last = acc.Last(value);
            string? dob = acc.Dob(value);

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

        private AccessorSet GetAccessors(Type modelType)
        {
            return _accessorCache.GetOrAdd(modelType, t => new AccessorSet(
                CreateGetter(t, _ssn),
                CreateGetter(t, _first),
                CreateGetter(t, _last),
                CreateGetter(t, _dob)));
        }

        private static Func<object, string?> CreateGetter(Type targetType, string propName)
        {
            // Case-insensitive public instance property lookup to match previous behavior
            var pi = targetType.GetProperty(propName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (pi is null || pi.GetMethod is null)
            {
                return static _ => null;
            }

            // (object obj) => (string?)((T)obj).Prop
            ParameterExpression objParam = Expression.Parameter(typeof(object), "obj");
            UnaryExpression cast = Expression.Convert(objParam, targetType);
            MemberExpression prop = Expression.Property(cast, pi);
            UnaryExpression asString = Expression.TypeAs(prop, typeof(string));
            var lambda = Expression.Lambda<Func<object, string?>>(asString, objParam);
            return lambda.Compile();
        }
    }
}
