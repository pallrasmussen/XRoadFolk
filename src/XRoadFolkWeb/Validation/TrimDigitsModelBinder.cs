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
            if (value is null)
            {
                return Task.CompletedTask;
            }
            // Only allow ASCII digits 0-9; reject other Unicode digit classes
            string digits = new([.. value.Where(static c => c >= '0' && c <= '9')]);
            bindingContext.Result = ModelBindingResult.Success(string.IsNullOrEmpty(digits) ? value : digits);
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