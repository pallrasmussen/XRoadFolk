using Microsoft.Extensions.Options;

namespace XRoadFolkRaw.Lib.Options
{
    public sealed class GetPersonRequestOptionsValidator : IValidateOptions<GetPersonRequestOptions>
    {
        // Compute once per type initialization to avoid per-call allocations
        private static readonly GetPersonInclude KnownMask = BuildKnownMask();

        private static GetPersonInclude BuildKnownMask()
        {
            GetPersonInclude mask = GetPersonInclude.None;
            foreach (GetPersonInclude f in Enum.GetValues<GetPersonInclude>())
            {
                mask |= f;
            }
            return mask;
        }

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

            // Validate Include contains only known flags using cached mask
            GetPersonInclude unknown = options.Include & ~KnownMask;
            return unknown != GetPersonInclude.None
                ? ValidateOptionsResult.Fail($"Include contains undefined flag(s): {(int)unknown}.")
                : ValidateOptionsResult.Success;
        }
    }
}