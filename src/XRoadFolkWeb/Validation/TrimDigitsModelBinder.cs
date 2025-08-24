using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Threading.Tasks;

namespace XRoadFolkWeb.Validation
{
    // Normalizes SSN by stripping non-digits before validation
    public sealed class TrimDigitsModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            string? value = context.ValueProvider.GetValue(context.ModelName).FirstValue;
            if (value is null)
            {
                return Task.CompletedTask;
            }
            string digits = new string(value.Where(char.IsDigit).ToArray());
            context.Result = ModelBindingResult.Success(string.IsNullOrEmpty(digits) ? value : digits);
            return Task.CompletedTask;
        }
    }

    public sealed class TrimDigitsModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext ctx)
        {
            if (ctx.Metadata.ContainerType == typeof(Pages.IndexModel) && ctx.Metadata.PropertyName == "Ssn")
            {
                return new TrimDigitsModelBinder();
            }
            return null;
        }
    }
}