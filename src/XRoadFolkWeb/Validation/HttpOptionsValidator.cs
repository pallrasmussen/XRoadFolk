using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Validation
{
    /// <summary>
    /// Validates <see cref="HttpOptions"/> at startup. Prevents insecure settings outside Development.
    /// </summary>
    public sealed class HttpOptionsValidator : IValidateOptions<HttpOptions>
    {
        private readonly IHostEnvironment _env;
        public HttpOptionsValidator(IHostEnvironment env) => _env = env;

        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, HttpOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("Http options cannot be null.");
            }

            if (!_env.IsDevelopment() && options.BypassServerCertificateValidation)
            {
                return ValidateOptionsResult.Fail("Http:BypassServerCertificateValidation must be false outside Development.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
