using System.Globalization;
using Microsoft.Extensions.Localization;
using XRoadFolkRaw.Lib;
using Xunit;

namespace XRoadFolkRaw.Lib.Tests
{
    public sealed class InputValidationTests
    {
        private sealed class NoopLocalizer : IStringLocalizer<InputValidation>
        {
            public LocalizedString this[string name]
                => new(name, name, resourceNotFound: false);
            public LocalizedString this[string name, params object[] arguments]
                => new(name, string.Format(CultureInfo.InvariantCulture, name, arguments), resourceNotFound: false);
            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
            public IStringLocalizer WithCulture(CultureInfo culture) => this;
        }

        private static readonly IStringLocalizer<InputValidation> L = new NoopLocalizer();

        [Theory]
        [InlineData("John", true)]
        [InlineData("Óli", true)]
        [InlineData("A", false)]
        [InlineData("ThisNameIsWayTooLongBeyondFiftyCharactersAndShouldFail", false)]
        [InlineData("John3", false)]
        [InlineData(" John ", true)]
        public void IsValidName_Works(string name, bool expected)
        {
            Assert.Equal(expected, InputValidation.IsValidName(name));
        }

        [Theory]
        [InlineData("12-34-56-789", "123456789")]
        [InlineData(" 12 34 56 789 ", "123456789")]
        [InlineData("abc123", "123")]
        [InlineData("", "")]
        public void NormalizeDigits_StripsNonDigits(string input, string expected)
        {
            Assert.Equal(expected, InputValidation.NormalizeDigits(input));
        }

        [Theory]
        [InlineData("010203123", 2003, 02, 01, true)]
        [InlineData("311299999", 1999, 12, 31, true)]
        [InlineData("320199999", 0, 0, 0, false)] // invalid day
        [InlineData("011399999", 0, 0, 0, false)] // invalid month
        [InlineData("01010199", 0, 0, 0, false)]  // invalid length
        public void LooksLikeValidSsn_Parses(string ssn, int y, int m, int d, bool ok)
        {
            bool r = InputValidation.LooksLikeValidSsn(ssn, out DateTimeOffset? embedded);
            Assert.Equal(ok, r);
            if (ok)
            {
                Assert.True(embedded.HasValue);
                Assert.Equal(new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).Date, embedded!.Value.Date);
            }
        }

        [Theory]
        [InlineData("2000-01-02", true)]
        [InlineData("02-01-2000", true)]
        [InlineData("02/01/2000", true)]
        [InlineData("02.01.2000", true)]
        [InlineData("20000102", true)]
        [InlineData("02-31-2000", false)]
        [InlineData("1899-12-31", false)]
        public void TryParseDob_Works(string input, bool expected)
        {
            bool r = InputValidation.TryParseDob(input, out DateTimeOffset? dob);
            Assert.Equal(expected, r);
            if (expected)
            {
                Assert.NotNull(dob);
            }
        }

        [Fact]
        public void ValidateCriteria_SsnOnly_Ok()
        {
            var (ok, errors, ssnNorm, dob) = InputValidation.ValidateCriteria("010203123", null, null, null, L);
            Assert.True(ok);
            Assert.Empty(errors);
            Assert.Equal("010203123", ssnNorm);
            Assert.Null(dob);
        }

        [Fact]
        public void ValidateCriteria_NamesAndDob_Ok()
        {
            var (ok, errors, ssnNorm, dob) = InputValidation.ValidateCriteria(null, "John", "Doe", "2000-01-02", L);
            Assert.True(ok);
            Assert.Empty(errors);
            Assert.Null(ssnNorm);
            Assert.Equal(new DateTimeOffset(2000, 1, 2, 0, 0, 0, TimeSpan.Zero).Date, dob!.Value.Date);
        }

        [Fact]
        public void ValidateCriteria_InvalidSsn_AddsErrors()
        {
            var (ok, errors, _, _) = InputValidation.ValidateCriteria("bad-ssn", null, null, null, L);
            Assert.False(ok);
            Assert.Contains(InputValidation.Errors.InvalidSsn, errors);
        }

        [Fact]
        public void ValidateCriteria_Missing_AllowsListContainsProvideMessage()
        {
            var (ok, errors, _, _) = InputValidation.ValidateCriteria(null, null, null, null, L);
            Assert.False(ok);
            Assert.Contains(InputValidation.Errors.ProvideSsnOrNameDob, errors);
        }

        [Fact]
        public void ValidateCriteria_DobSsnMismatch_AddsError()
        {
            var (ok, errors, _, _) = InputValidation.ValidateCriteria("010203123", "John", "Doe", "2001-01-02", L);
            Assert.False(ok);
            Assert.Contains(InputValidation.Errors.DobSsnMismatch, errors.First(e => e.StartsWith(InputValidation.Errors.DobSsnMismatch, StringComparison.Ordinal)));
        }
    }
}
