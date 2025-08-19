
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace XRoadFolkRaw.Tests
{
    public static class ValidatorMirror
    {
        public static (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) ValidateCriteria(
            string? ssn, string? firstName, string? lastName, string? dobInput)
        {
            var errs = new List<string>();
            string? ssnNorm = null;
            DateTimeOffset? dob = null;

            bool IsValidName(string? name)
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                name = name.Trim();
                return Regex.IsMatch(name, @"^[\p{L}][\p{L}\p{M}\s\-']{1,49}$");
            }

            string NormalizeDigits(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());

            bool LooksLikeValidSsn(string? s, out DateTimeOffset? embedded)
            {
                embedded = null;
                var d = NormalizeDigits(s);
                if (d.Length != 9) return false;
                var dd = d.Substring(0,2);
                var mm = d.Substring(2,2);
                var yy = d.Substring(4,2);
                if (!int.TryParse(dd, out var DD) || !int.TryParse(mm, out var MM) || !int.TryParse(yy, out var YY)) return false;
                int year = YY + (YY >= 0 && YY <= 99 ? (YY <= DateTimeOffset.UtcNow.Year % 100 ? 2000 : 1900) : 1900);
                var ok = int.TryParse(d.Substring(6,3), out _);
                try
                {
                    var dt = new DateTimeOffset(year, MM, DD, 0, 0, 0, TimeSpan.Zero);
                    if (dt > DateTimeOffset.UtcNow.Date) return false;
                    embedded = dt;
                    return ok;
                }
                catch { return false; }
            }

            bool TryParseDob(string? s, out DateTimeOffset? dval)
            {
                dval = null;
                if (string.IsNullOrWhiteSpace(s)) return false;
                if (!DateTimeOffset.TryParse(s, out var dt)) return false;
                if (dt < new DateTimeOffset(1900,1,1,0,0,0,TimeSpan.Zero)) return false;
                if (dt.Date > DateTimeOffset.UtcNow.Date) return false;
                dval = dt.Date;
                return true;
            }

            var haveNames = IsValidName(firstName) && IsValidName(lastName);
            var haveDob = TryParseDob(dobInput, out dob);
            var haveSsn = LooksLikeValidSsn(ssn, out var ssnDob);
            bool ssnProvided = !string.IsNullOrWhiteSpace(ssn);

            if (!haveSsn && !(haveNames && haveDob))
                errs.Add("Enter either SSN (9 digits) OR FirstName + LastName + DateOfBirth.");
            if (ssnProvided && !haveSsn)
                errs.Add("SSN must be 9 digits (allowing spaces/hyphens) and start with a valid date (ddMMyy).");
            if (!string.IsNullOrWhiteSpace(firstName) && !IsValidName(firstName))
                errs.Add("FirstName must be 2–50 letters (Unicode), allowing spaces, hyphens, apostrophes.");
            if (!string.IsNullOrWhiteSpace(lastName) && !IsValidName(lastName))
                errs.Add("LastName must be 2–50 letters (Unicode), allowing spaces, hyphens, apostrophes.");

            if (haveSsn && haveDob && ssnDob.HasValue && dob.HasValue && ssnDob.Value.Date != dob.Value.Date)
                errs.Add($"DateOfBirth ({dob:yyyy-MM-dd}) does not match SSN date ({ssnDob:yyyy-MM-dd}).");

            if (haveSsn) ssnNorm = NormalizeDigits(ssn);
            return (errs.Count == 0, errs, ssnNorm, dob);
        }
    }

    public class ValidatorTests
    {
        [Fact]
        public void Accepts_SSN_Only()
        {
            var r = ValidatorMirror.ValidateCriteria("121299-123", null, null, null);
            Assert.True(r.Ok);
            Assert.Equal("121299123", r.SsnNorm);
        }

        [Fact]
        public void Accepts_Name_And_Dob()
        {
            var r = ValidatorMirror.ValidateCriteria(null, "Páll", "Rasmussen", "1966-09-03");
            Assert.True(r.Ok);
            Assert.Null(r.SsnNorm);
            Assert.NotNull(r.Dob);
        }

        [Fact]
        public void Rejects_Partial_Name_No_Dob()
        {
            var r = ValidatorMirror.ValidateCriteria(null, "Anna", "Olsen", null);
            Assert.False(r.Ok);
        }

        [Fact]
        public void Rejects_Bad_SSN_When_Provided()
        {
            var r = ValidatorMirror.ValidateCriteria("12345", "Anna", "Olsen", "1990-05-01");
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.StartsWith("SSN must be"));
        }

        [Fact]
        public void Crosschecks_Dob_From_SSN()
        {
            var r = ValidatorMirror.ValidateCriteria("010199-123", "A", "B", "1999-01-02");
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.StartsWith("DateOfBirth ("));
        }
    }
}
