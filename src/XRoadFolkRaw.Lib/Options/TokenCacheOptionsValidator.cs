using Microsoft.Extensions.Options;

namespace XRoadFolkRaw.Lib.Options
{
    /// <summary>
    /// Strong validation for TokenCacheOptions used in library-only contexts.
    /// Enforces non-negative RefreshSkewSeconds and positive DefaultTtlSeconds.
    /// </summary>
    public sealed class TokenCacheOptionsValidator : IValidateOptions<TokenCacheOptions>
    {
        public ValidateOptionsResult Validate(string? name, TokenCacheOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("Options are required.");
            }

            if (options.RefreshSkewSeconds < 0)
            {
                return ValidateOptionsResult.Fail("TokenCache: RefreshSkewSeconds must be >= 0.");
            }

            if (options.DefaultTtlSeconds < 1)
            {
                return ValidateOptionsResult.Fail("TokenCache: DefaultTtlSeconds must be >= 1.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
