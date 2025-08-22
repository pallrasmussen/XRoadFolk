using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;

namespace XRoadFolkRaw.Lib
{
    public partial class InputValidation
    {
        private static readonly string[] DobFormats = [
            "yyyy-MM-dd",
            "dd-MM-yyyy",
            "yyyy/MM/dd",
            "dd.MM.yyyy",
            "MM/dd/yyyy"
        ];

        [GeneratedRegex("^[\\p{L}][\\p{L}\\p{M}\\s\\-']{1,49}$")]
        private static partial Regex NameRegex();

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
            return NameRegex().IsMatch(name);
        }

        public static string NormalizeDigits(string? s)
        {
            return new([.. (s ?? "").Where(char.IsDigit)]);
        }

        public static bool LooksLikeValidSsn(string? s, out DateTimeOffset? embedded)
        {
            embedded = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
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
            int currYY = now.Year % 100;
            int year = YY + (YY <= currYY ? 2000 : 1900);

            try
            {
                DateTimeOffset dt = new(year, MM, DD, 0, 0, 0, TimeSpan.Zero);
                if (dt.Date > now.Date)
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

            bool ok = DateTimeOffset.TryParseExact(
                s,
                DobFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset dt);

            if (!ok)
            {
                ok = DateTimeOffset.TryParse(
                    s,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out dt);
            }

            if (!ok)
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
