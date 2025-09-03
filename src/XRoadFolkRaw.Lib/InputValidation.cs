using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;

namespace XRoadFolkRaw.Lib
{
    public partial class InputValidation
    {
        private static readonly string[] DobFormats = [
            // ISO first
            "yyyy-MM-dd",
            // DMY styles next
            "dd-MM-yyyy",
            "dd/MM/yyyy",
            "dd.MM.yyyy",
            // YMD with slashes
            "yyyy/MM/dd",
            // US style last to avoid ambiguous swaps
            "MM/dd/yyyy",
            // compact variants
            "yyyyMMdd",
            "ddMMyyyy",
        ];

        [GeneratedRegex("^[\\p{L}][\\p{L}\\p{M}\\s\\-']{1,49}$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex NameRegex();

        public static class Errors
        {
            public const string ProvideSsnOrNameDob = "ProvideSsnOrNameDob";
            public const string InvalidSsn = "InvalidSsn";
            public const string InvalidFirstName = "InvalidFirstName";
            public const string InvalidLastName = "InvalidLastName";
            public const string DobSsnMismatch = "DobSsnMismatch";
        }

        public static (bool Ok, IReadOnlyList<string> Errors, string? SsnNorm, DateTimeOffset? Dob)
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
                errs.Add(loc[Errors.DobSsnMismatch,
                    dob.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ssnDob.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)]);
            }

            if (haveSsn)
            {
                ssnNorm = NormalizeDigits(ssn);
            }

            return (errs.Count == 0, errs.AsReadOnly(), ssnNorm, dob);
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
            if (string.IsNullOrEmpty(s)) return string.Empty;
            ReadOnlySpan<char> src = s.AsSpan();

            // Count digits first
            int count = 0;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if ((uint)(c - '0') <= 9)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return string.Empty;
            }

            // Fast path: already all digits
            if (count == src.Length)
            {
                return s;
            }

            // Allocate result and populate with digits only
            return string.Create(count, s, static (dest, state) =>
            {
                ReadOnlySpan<char> span = state.AsSpan();
                int j = 0;
                for (int i = 0; i < span.Length; i++)
                {
                    char c = span[i];
                    if ((uint)(c - '0') <= 9)
                    {
                        dest[j++] = c;
                    }
                }
            });
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

            // Parse all numeric segments using InvariantCulture
            if (!int.TryParse(d.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int DD))
            {
                return false;
            }
            if (!int.TryParse(d.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int MM))
            {
                return false;
            }
            if (!int.TryParse(d.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int YY))
            {
                return false;
            }
            if (!int.TryParse(d.AsSpan(6, 3), NumberStyles.None, CultureInfo.InvariantCulture, out int _))
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

                embedded = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset);
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

            dval = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset);
            return true;
        }
    }
}
