using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed class ResponseViewerOptionsValidator : IValidateOptions<ResponseViewerOptions>
    {
        private readonly IHostEnvironment _env;
        public ResponseViewerOptionsValidator(IHostEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public ValidateOptionsResult Validate(string? name, ResponseViewerOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("ResponseViewer options are required.");
            }

            // In non-Production, require at least one of the XML tabs to be visible
            if (!_env.IsProduction())
            {
                if (!options.ShowRawXml && !options.ShowPrettyXml)
                {
                    return ValidateOptionsResult.Fail("Features:ResponseViewer must enable at least one of ShowRawXml or ShowPrettyXml in non-Production.");
                }
            }

            return ValidateOptionsResult.Success;
        }
    }
}
