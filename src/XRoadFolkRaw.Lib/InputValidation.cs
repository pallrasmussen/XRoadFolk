using System.Text.RegularExpressions;

namespace XRoadFolkRaw.Lib
{
    public static class InputValidation
    {
        public static (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob)
            ValidateCriteria(string? ssn, string? firstName, string? lastName, string? dobInput)
        {
            List<string> errs = new();
            string? ssnNorm = null;
            DateTimeOffset? dob = null;

            bool haveNames = IsValidName(firstName) && IsValidName(lastName);
            bool haveDob = TryParseDob(dobInput, out dob);
            bool haveSsn = LooksLikeValidSsn(ssn, out DateTimeOffset? ssnDob);
            bool ssnProvided = !string.IsNullOrWhiteSpace(ssn);

            // Strict presence rule: SSN OR (names + dob)
            if (!haveSsn && !(haveNames && haveDob))
                errs.Add("Enter either SSN (9 digits) OR FirstName + LastName + DateOfBirth.");

            // Only validate SSN if provided
            if (ssnProvided && !haveSsn)
                errs.Add("SSN must be 9 digits (allowing spaces/hyphens) and start with a valid date (ddMMyy).");

            // Name errors (only if provided)
            if (!string.IsNullOrWhiteSpace(firstName) && !IsValidName(firstName))
                errs.Add("FirstName must be 2–50 letters (Unicode), allowing spaces, hyphens, apostrophes.");
            if (!string.IsNullOrWhiteSpace(lastName) && !IsValidName(lastName))
                errs.Add("LastName must be 2–50 letters (Unicode), allowing spaces, hyphens, apostrophes.");

            // Cross-check DOB vs SSN-embedded date
            if (haveSsn && haveDob && ssnDob.HasValue && dob.HasValue && ssnDob.Value.Date != dob.Value.Date)
                errs.Add($"DateOfBirth ({dob:yyyy-MM-dd}) does not match SSN date ({ssnDob:yyyy-MM-dd}).");

            if (haveSsn) ssnNorm = NormalizeDigits(ssn);
            return (errs.Count == 0, errs, ssnNorm, dob);
        }

        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            // Unicode letters + marks, spaces, hyphen, apostrophe; 2-50 chars
            return Regex.IsMatch(name, @"^[\p{L}][\p{L}\p{M}\s\-']{1,49}$");
        }

        public static string NormalizeDigits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

        public static bool LooksLikeValidSsn(string? s, out DateTimeOffset? embedded)
        {
            embedded = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string d = NormalizeDigits(s);
            if (d.Length != 9) return false;
            if (!int.TryParse(d.Substring(0, 2), out int DD)) return false;
            if (!int.TryParse(d.Substring(2, 2), out int MM)) return false;
            if (!int.TryParse(d.Substring(4, 2), out int YY)) return false;

            // Infer century 1900/2000 based on current year
            int currYY = DateTimeOffset.UtcNow.Year % 100;
            int year = YY + (YY <= currYY ? 2000 : 1900);

            try
            {
                DateTimeOffset dt = new(year, MM, DD, 0, 0, 0, TimeSpan.Zero);
                if (dt.Date > DateTimeOffset.UtcNow.Date) return false;
                // validate last 3 digits numeric
                if (!int.TryParse(d.Substring(6, 3), out _)) return false;
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
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!DateTimeOffset.TryParse(s, out DateTimeOffset dt)) return false;
            DateTimeOffset min = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
            if (dt < min) return false;
            if (dt.Date > DateTimeOffset.UtcNow.Date) return false;
            dval = dt.Date;
            return true;
        }
    }
}
