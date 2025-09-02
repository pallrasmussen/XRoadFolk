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

            // Validate Include contains only known flags
            GetPersonInclude knownMask = GetPersonInclude.None;
            foreach (GetPersonInclude f in Enum.GetValues<GetPersonInclude>())
            {
                knownMask |= f;
            }
            GetPersonInclude unknown = options.Include & ~knownMask;
            if (unknown != 0)
            {
                return ValidateOptionsResult.Fail($"Include contains undefined flag(s): {(int)unknown}.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}