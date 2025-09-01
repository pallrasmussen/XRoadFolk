using Microsoft.Extensions.Options;

namespace XRoadFolkRaw.Lib.Options
{
    public sealed class GetPersonRequestOptionsValidator : IValidateOptions<GetPersonRequestOptions>
    {
        public ValidateOptionsResult Validate(string? name, GetPersonRequestOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("Options are required.");
            }

            int idCount = 0;
            if (!string.IsNullOrWhiteSpace(options.Id))
            {
                idCount++;
            }

            if (!string.IsNullOrWhiteSpace(options.PublicId))
            {
                idCount++;
            }

            if (!string.IsNullOrWhiteSpace(options.Ssn))
            {
                idCount++;
            }

            if (!string.IsNullOrWhiteSpace(options.ExternalId))
            {
                idCount++;
            }

            if (idCount == 0)
            {
                return ValidateOptionsResult.Fail("One of Id, PublicId, Ssn, or ExternalId must be provided.");
            }

            if (idCount > 1)
            {
                return ValidateOptionsResult.Fail("Only one of Id, PublicId, Ssn, or ExternalId can be provided.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}