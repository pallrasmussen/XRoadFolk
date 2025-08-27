using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace XRoadFolkWeb.Validation
{
    // Normalizes SSN by stripping non-digits before validation
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
            string digits = new([.. value.Where(char.IsDigit)]);
            bindingContext.Result = ModelBindingResult.Success(string.IsNullOrEmpty(digits) ? value : digits);
            return Task.CompletedTask;
        }
    }

    public sealed class TrimDigitsModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // IDE0046: 'if' statement can be simplified
            return (context.Metadata.ContainerType == typeof(Pages.IndexModel) && context.Metadata.PropertyName == "Ssn")
                ? new TrimDigitsModelBinder()
                : null;
        }
    }
}