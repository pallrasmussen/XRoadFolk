using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace XRoadFolkWeb.Validation
{
    /// <summary>
    /// Normalizes SSN by stripping non-ASCII digits before validation
    /// </summary>
    public sealed class TrimDigitsModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            ArgumentNullException.ThrowIfNull(bindingContext);

            string? value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            if (string.IsNullOrEmpty(value))
            {
                return Task.CompletedTask;
            }

            // Count ASCII digits first to decide if we need to allocate
            int digitCount = 0;
            foreach (char c in value)
            {
                if (c >= '0' && c <= '9')
                {
                    digitCount++;
                }
            }

            if (digitCount == 0 || digitCount == value.Length)
            {
                // No digits found -> keep original (let validation handle)
                // All chars are digits -> reuse original string to avoid allocation
                bindingContext.Result = ModelBindingResult.Success(value);
                return Task.CompletedTask;
            }

            string digits = string.Create(digitCount, value, static (dest, src) =>
            {
                int i = 0;
                foreach (char ch in src)
                {
                    if (ch >= '0' && ch <= '9')
                    {
                        dest[i++] = ch;
                    }
                }
            });

            bindingContext.Result = ModelBindingResult.Success(digits);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Marker attribute to request TrimDigitsModelBinder for a property
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class TrimDigitsAttribute : Attribute, IBinderTypeProviderMetadata, IBindingSourceMetadata
    {
        public Type BinderType => typeof(TrimDigitsModelBinder);
        public BindingSource BindingSource => BindingSource.ModelBinding;
    }

    public sealed class TrimDigitsModelBinderProvider : IModelBinderProvider
    {
        private static readonly IModelBinder CachedBinder = new TrimDigitsModelBinder();

        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Metadata is DefaultModelMetadata dmm && dmm.Attributes?.PropertyAttributes is not null)
            {
                bool has = dmm.Attributes.PropertyAttributes.Any(a => a is TrimDigitsAttribute);
                if (has)
                {
                    return CachedBinder;
                }
            }
            return null;
        }
    }
}