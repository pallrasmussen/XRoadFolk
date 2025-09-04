using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using XRoadFolkWeb.Features.Index;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class PersonDetailsProviderTests
    {
        private static MethodInfo GetStaticMethod(string name)
        {
            var t = typeof(PersonDetailsProvider);
            var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) throw new InvalidOperationException($"Method {name} not found on PersonDetailsProvider");
            return m;
        }

        private sealed class TestLocalizer : IStringLocalizer
        {
            public LocalizedString this[string name]
                => new LocalizedString(name, name, resourceNotFound: false);

            public LocalizedString this[string name, params object[] arguments]
            {
                get
                {
                    // Return the first argument's string representation to simulate a format resource
                    var val = (arguments != null && arguments.Length > 0) ? arguments[0]?.ToString() ?? string.Empty : name;
                    return new LocalizedString(name, val, resourceNotFound: false);
                }
            }

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
            public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
        }

        [Fact]
        public void Segments_Trims_Whitespace_And_Strips_Indexes()
        {
            var mi = GetStaticMethod("Segments");
            var key = "  Person  .  Names[0] .  Value  ";
            var res = (IEnumerable<string>)mi.Invoke(null, new object?[] { key })!;
            res.Should().Equal(new[] { "Person", "Names", "Value" });
        }

        [Fact]
        public void ApplyPrimaryFilter_Removes_RequestHeader_And_IdLike_Keys()
        {
            var mi = GetStaticMethod("ApplyPrimaryFilter");
            var pairs = new List<(string Key, string Value)>
            {
                ("requestHeader.token","x"),
                ("Person.Id","123"),
                ("Person.Names[0].Value","Jane"),
                ("RequestBody.foo","bar"),
                ("Person.AuthorityCode","999"),
            };
            var filtered = ((IEnumerable<(string Key, string Value)>)mi.Invoke(null, new object?[] { pairs })!).ToList();
            filtered.Select(p => p.Key).Should().Contain("Person.Names[0].Value");
            filtered.Select(p => p.Key).Should().NotContain(new[] { "requestHeader.token", "RequestBody.foo", "Person.Id", "Person.AuthorityCode" });
        }

        [Fact]
        public void ApplyAllowedFilter_Allows_Matching_Base_Segments()
        {
            var mi = GetStaticMethod("ApplyAllowedFilter");
            var pairs = new List<(string Key, string Value)>
            {
                ("Person.Names[0].Value","Jane"),
                ("Other.Data","x"),
                ("NamesHistory.Name.Value","Old"),
            };
            var allowed = new List<string> { "Names" };
            var filtered = ((IEnumerable<(string Key, string Value)>)mi.Invoke(null, new object?[] { pairs, allowed })!).ToList();
            filtered.Select(p => p.Key).Should().Contain("Person.Names[0].Value");
            filtered.Select(p => p.Key).Should().Contain("NamesHistory.Name.Value");
            filtered.Select(p => p.Key).Should().NotContain("Other.Data");
        }

        [Fact]
        public void ComputeSelectedNameSuffix_Joins_First_And_Last()
        {
            var mi = GetStaticMethod("ComputeSelectedNameSuffix");
            var pairs = new List<(string Key, string Value)>
            {
                ("Foo.FirstName","Jane"),
                ("Bar.LastName","Doe"),
            };
            var loc = new TestLocalizer();
            var res = (string)mi.Invoke(null, new object?[] { pairs, loc })!;
            res.Should().Contain("Jane");
            res.Should().Contain("Doe");
        }
    }
}
