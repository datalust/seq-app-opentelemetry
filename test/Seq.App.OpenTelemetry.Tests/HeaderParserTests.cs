using Seq.App.OpenTelemetry.Settings;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class HeaderParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrEmptyInputReturnsNull(string? input)
    {
        Assert.Null(HeaderParser.ParseHeaders(input));
    }

    [Fact]
    public void SingleHeaderIsParsed()
    {
        var result = HeaderParser.ParseHeaders("api-key=abc123");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("api-key", result[0].Key);
        Assert.Equal("abc123", result[0].Value);
    }

    [Fact]
    public void MultipleHeadersAreParsed()
    {
        var result = HeaderParser.ParseHeaders("key1=val1,key2=val2");

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("key1", result[0].Key);
        Assert.Equal("val1", result[0].Value);
        Assert.Equal("key2", result[1].Key);
        Assert.Equal("val2", result[1].Value);
    }

    [Fact]
    public void WhitespaceIsTrimmed()
    {
        var result = HeaderParser.ParseHeaders(" key1 = val1 , key2 = val2 ");

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("key1", result[0].Key);
        Assert.Equal("val1", result[0].Value);
        Assert.Equal("key2", result[1].Key);
        Assert.Equal("val2", result[1].Value);
    }

    [Fact]
    public void ValueContainingEqualsSignIsPreserved()
    {
        var result = HeaderParser.ParseHeaders("Authorization=Basic dXNlcjpwYXNz==");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Authorization", result[0].Key);
        Assert.Equal("Basic dXNlcjpwYXNz==", result[0].Value);
    }

    [Theory]
    [InlineData("=value")]
    [InlineData("noequals")]
    [InlineData(",,,")]
    public void MalformedEntriesAreSkipped(string input)
    {
        Assert.Null(HeaderParser.ParseHeaders(input));
    }

    [Fact]
    public void MalformedEntriesAmongValidOnesAreSkipped()
    {
        var result = HeaderParser.ParseHeaders("good=value,=bad,also-good=yes");

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("good", result[0].Key);
        Assert.Equal("also-good", result[1].Key);
    }
}
