using System;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkRaw.Lib.Logging;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class SoapSanitizerMalformedXmlTests
    {
        [Theory]
        [InlineData("<Envelope><Bad></Envelope>")] // mismatched
        [InlineData("")] // empty
        [InlineData("<>")] // invalid token
        [InlineData("<Envelope attr='>")] // invalid attribute
        public void Sanitizer_Does_Not_Throw_On_Malformed(string input)
        {
            var logger = NullLogger.Instance;
            Action act = () => SafeSoapLogger.SafeSoapDebug(logger, input, "Test");
            act.Should().NotThrow();
        }
    }
}
