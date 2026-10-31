using UrlShortener.Infrastructure.Services;
using Xunit;

namespace UrlShortener.UnitTests;

public class Base62EncoderTests
{
    private readonly Base62Encoder _encoder = new();

    [Fact]
    public void Encode_Zero_ReturnsZeroCharacter()
    {
        var result = _encoder.Encode(0);
        Assert.Equal("0", result);
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(61, "Z")]
    [InlineData(62, "10")]
    [InlineData(1000000, "4c92")]
    public void Encode_KnownNumbers_ReturnsExpectedCodes(long input, string expected)
    {
        var result = _encoder.Encode(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Encode_NegativeNumber_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _encoder.Encode(-1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(61)]
    [InlineData(62)]
    [InlineData(123456)]
    [InlineData(1000000)]
    [InlineData(9876543210L)]
    [InlineData(long.MaxValue / 100)]
    public void RoundTrip_EncodeAndDecode_MatchesOriginalValue(long original)
    {
        var code = _encoder.Encode(original);
        var decoded = _encoder.Decode(code);

        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Decode_NullOrWhitespace_ThrowsArgumentException(string? invalidCode)
    {
        Assert.Throws<ArgumentException>(() => _encoder.Decode(invalidCode!));
    }

    [Theory]
    [InlineData("abc-def")]
    [InlineData("123#")]
    [InlineData("hello!")]
    public void Decode_InvalidCharacters_ThrowsArgumentException(string invalidCode)
    {
        Assert.Throws<ArgumentException>(() => _encoder.Decode(invalidCode));
    }
}
