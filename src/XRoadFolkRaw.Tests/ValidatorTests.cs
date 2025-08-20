using System.Text.RegularExpressions;
using System.Globalization;
using Xunit;

namespace XRoadFolkRaw.Tests
{
    public static class ValidatorMirror
    {
        private static readonly string[] DobFormats = { "yyyy-MM-dd", "dd-MM-yyyy" };
        public static (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) ValidateCriteria(
            string? ssn, string? firstName, string? lastName, string? dobInput)
        {
            List<string> errs = [];
            string? ssnNorm = null;

            bool IsValidName(string? name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                name = name.Trim();
                return Regex.IsMatch(name, @"^[\p{L}][\p{L}\p{M}\s\-']{1,49}$");
            }

            string NormalizeDigits(string? s) => new([.. (s ?? "").Where(char.IsDigit)]);

            bool LooksLikeValidSsn(string? s, out DateTimeOffset? embedded)
            {
                embedded = null;
                string d = NormalizeDigits(s);
                if (d.Length != 9)
                {
                    return false;
                }

                string dd = d.Substring(0, 2);
                string mm = d.Substring(2, 2);
                string yy = d.Substring(4, 2);
                if (!int.TryParse(dd, out int DD) || !int.TryParse(mm, out int MM) || !int.TryParse(yy, out int YY))
                {
                    return false;
                }

                int year = YY + (YY >= 0 && YY <= 99 ? (YY <= DateTimeOffset.UtcNow.Year % 100 ? 2000 : 1900) : 1900);
                bool ok = int.TryParse(d.Substring(6, 3), out _);
                try
                {
                    DateTimeOffset dt = new(year, MM, DD, 0, 0, 0, TimeSpan.Zero);
                    if (dt > DateTimeOffset.UtcNow.Date)
                    {
                        return false;
                    }

                    embedded = dt;
                    return ok;
                }
                catch { return false; }
            }

            bool TryParseDob(string? s, out DateTimeOffset? dval)
            {
                dval = null;
                if (string.IsNullOrWhiteSpace(s))
                {
                    return false;
                }

                if (!DateTimeOffset.TryParseExact(
                        s,
                        DobFormats,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTimeOffset dt))
                {
                    return false;
                }

                if (dt < new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero))
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

            bool haveNames = IsValidName(firstName) && IsValidName(lastName);
            bool haveDob = TryParseDob(dobInput, out DateTimeOffset? dob);
            bool haveSsn = LooksLikeValidSsn(ssn, out DateTimeOffset? ssnDob);
            bool ssnProvided = !string.IsNullOrWhiteSpace(ssn);

            if (!haveSsn && !(haveNames && haveDob))
            {
                errs.Add("Enter either SSN (9 digits) OR FirstName + LastName + DateOfBirth.");
            }

            if (ssnProvided && !haveSsn)
            {
                errs.Add("SSN must be 9 digits (allowing spaces/hyphens) and start with a valid date (ddMMyy).");
            }

            if (!string.IsNullOrWhiteSpace(firstName) && !IsValidName(firstName))
            {
                errs.Add("FirstName must be 2–50 letters (Unicode), allowing spaces, hyphens, apostrophes.");
            }

            if (!string.IsNullOrWhiteSpace(lastName) && !IsValidName(lastName))
            {
                errs.Add("LastName must be 2–50 letters (Unicode), allowing spaces, hyphens, apostrophes.");
            }

            if (haveSsn && haveDob && ssnDob.HasValue && dob.HasValue && ssnDob.Value.Date != dob.Value.Date)
            {
                errs.Add($"DateOfBirth ({dob:yyyy-MM-dd}) does not match SSN date ({ssnDob:yyyy-MM-dd}).");
            }

            if (haveSsn)
            {
                ssnNorm = NormalizeDigits(ssn);
            }

            return (errs.Count == 0, errs, ssnNorm, dob);
        }
    }

    public class ValidatorTests
    {
        [Fact]
        public void AcceptsSSNOnly()
        {
            (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = ValidatorMirror.ValidateCriteria("121299-123", null, null, null);
            Assert.True(Ok);
            Assert.Equal("121299123", SsnNorm);
        }

        [Fact]
        public void AcceptsNameAndDob()
        {
            (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = ValidatorMirror.ValidateCriteria(null, "Páll", "Rasmussen", "1966-09-03");
            Assert.True(Ok);
            Assert.Null(SsnNorm);
            Assert.NotNull(Dob);
        }

        [Fact]
        public void AcceptsNameAndDobAlternateFormat()
        {
            (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = ValidatorMirror.ValidateCriteria(null, "Páll", "Rasmussen", "03-09-1966");
            Assert.True(Ok);
            Assert.Null(SsnNorm);
            Assert.NotNull(Dob);
        }

        [Fact]
        public void RejectsPartialNameNoDob()
        {
            (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = ValidatorMirror.ValidateCriteria(null, "Anna", "Olsen", null);
            Assert.False(Ok);
        }

        [Fact]
        public void RejectsBadSSNWhenProvided()
        {
            (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = ValidatorMirror.ValidateCriteria("12345", "Anna", "Olsen", "1990-05-01");
            Assert.False(Ok);
            Assert.Contains(Errors, e => e.StartsWith("SSN must be"));
        }

        [Fact]
        public void CrosschecksDobFromSSN()
        {
            (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = ValidatorMirror.ValidateCriteria("010199-123", "A", "B", "1999-01-02");
            Assert.False(Ok);
            Assert.Contains(Errors, e => e.StartsWith("DateOfBirth ("));
        }
    }
}
