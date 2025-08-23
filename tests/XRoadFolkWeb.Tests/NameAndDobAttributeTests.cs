using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using XRoadFolkWeb.Validation;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class NameAndDobAttributeTests
{
    [Theory]
    [InlineData("María-João")]
    [InlineData("O'Neill")]
    public void Name_Valid_Samples(string name)
    {
        var attr = new NameAttribute();
        attr.GetValidationResult(name, new ValidationContext(new object()))
            .Should().Be(ValidationResult.Success);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("A@")]
    public void Name_Invalid_Samples(string name)
    {
        var attr = new NameAttribute();
        attr.GetValidationResult(name, new ValidationContext(new object()))
            .Should().NotBe(ValidationResult.Success);
    }

    [Theory]
    [InlineData("1990-12-01")]
    public void Dob_Valid_Samples(string dob)
    {
        var attr = new DobAttribute();
        attr.GetValidationResult(dob, new ValidationContext(new object()))
            .Should().Be(ValidationResult.Success);
    }

    [Theory]
    [InlineData("2099-01-01")]
    [InlineData("1990/12/01")]
    public void Dob_Invalid_Samples(string dob)
    {
        var attr = new DobAttribute();
        attr.GetValidationResult(dob, new ValidationContext(new object()))
            .Should().NotBe(ValidationResult.Success);
    }
}