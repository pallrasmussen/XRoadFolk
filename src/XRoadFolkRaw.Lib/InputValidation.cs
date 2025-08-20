using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;

namespace XRoadFolkRaw.Lib
{
    public class InputValidation
    {
        private static readonly Regex NameRegex = new(@"^[\p{L}][\p{L}\p{M}\s\-']{1,49}$", RegexOptions.Compiled);

        public static class Errors
        {
            public const string ProvideSsnOrNameDob = "ProvideSsnOrNameDob";
            public const string InvalidSsn = "InvalidSsn";
            public const string InvalidFirstName = "InvalidFirstName";
            public const string InvalidLastName = "InvalidLastName";
            public const string DobSsnMismatch = "DobSsnMismatch";
        }

        public static (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob)
            ValidateCriteria(string? ssn, string? firstName, string? lastName, string? dobInput, IStringLocalizer<InputValidation> loc)
        {
            ArgumentNullException.ThrowIfNull(loc);

            List<string> errs = [];
            string? ssnNorm = null;

            bool haveNames = IsValidName(firstName) && IsValidName(lastName);
            bool haveDob = TryParseDob(dobInput, out DateTimeOffset? dob);
            bool haveSsn = LooksLikeValidSsn(ssn, out DateTimeOffset? ssnDob);
            bool ssnProvided = !string.IsNullOrWhiteSpace(ssn);

            // Strict presence rule: SSN OR (names + dob)
            if (!haveSsn && !(haveNames && haveDob))
            {
                errs.Add(loc[Errors.ProvideSsnOrNameDob]);
            }

            // Only validate SSN if provided
            if (ssnProvided && !haveSsn)
            {
                errs.Add(loc[Errors.InvalidSsn]);
            }

            // Name errors (only if provided)
            if (!string.IsNullOrWhiteSpace(firstName) && !IsValidName(firstName))
            {
                errs.Add(loc[Errors.InvalidFirstName]);
            }

            if (!string.IsNullOrWhiteSpace(lastName) && !IsValidName(lastName))
            {
                errs.Add(loc[Errors.InvalidLastName]);
            }

            // Cross-check DOB vs SSN-embedded date
            if (haveSsn && haveDob && ssnDob.HasValue && dob.HasValue && ssnDob.Value.Date != dob.Value.Date)
            {
                errs.Add(loc[Errors.DobSsnMismatch, dob.Value.ToString("yyyy-MM-dd"), ssnDob.Value.ToString("yyyy-MM-dd")]);
            }

            if (haveSsn)
            {
                ssnNorm = NormalizeDigits(ssn);
            }

            return (errs.Count == 0, errs, ssnNorm, dob);
        }

        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            name = name.Trim();
            // Unicode letters + marks, spaces, hyphen, apostrophe; 2-50 chars
            return NameRegex.IsMatch(name);
        }

        public static string NormalizeDigits(string? s)
        {
            return new([.. (s ?? "").Where(char.IsDigit)]);
        }

        public static bool LooksLikeValidSsn(string? s, out DateTimeOffset? embedded)
        {
            embedded = null;
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            string d = NormalizeDigits(s);
            if (d.Length != 9)
            {
                return false;
            }

            if (!int.TryParse(d.AsSpan(0, 2), out int DD))
            {
                return false;
            }

            if (!int.TryParse(d.AsSpan(2, 2), out int MM))
            {
                return false;
            }

            if (!int.TryParse(d.AsSpan(4, 2), out int YY))
            {
                return false;
            }

            // Infer century 1900/2000 based on current year
            int currYY = DateTimeOffset.UtcNow.Year % 100;
            int year = YY + (YY <= currYY ? 2000 : 1900);

            try
            {
                DateTimeOffset dt = new(year, MM, DD, 0, 0, 0, TimeSpan.Zero);
                if (dt.Date > DateTimeOffset.UtcNow.Date)
                {
                    return false;
                }
                // validate last 3 digits numeric
                if (!int.TryParse(d.AsSpan(6, 3), out _))
                {
                    return false;
                }

                embedded = dt.Date;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryParseDob(string? s, out DateTimeOffset? dval)
        {
            dval = null;
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            if (!DateTimeOffset.TryParseExact(
                    s,
                    new[] { "yyyy-MM-dd", "dd-MM-yyyy" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset dt))
            {
                return false;
            }

            DateTimeOffset min = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
            if (dt < min)
            {
                return false;
            }

            if (dt.Date > DateTimeOffset.UtcNow.Date)
            {
                return false;
            }

            dval = dt.Date;
            return true;
        }
    }
}
