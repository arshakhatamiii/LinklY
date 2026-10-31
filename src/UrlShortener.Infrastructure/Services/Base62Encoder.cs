using System.Text;
using UrlShortener.Domain.Common;

namespace UrlShortener.Infrastructure.Services;

public class Base62Encoder : IBase62Encoder
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly int Base = Alphabet.Length;

    public string Encode(long number)
    {
        if (number < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Value must be non-negative.");
        }

        if (number == 0)
        {
            return Alphabet[0].ToString();
        }

        Span<char> buffer = stackalloc char[16];
        var position = buffer.Length;

        while (number > 0)
        {
            var remainder = (int)(number % Base);
            buffer[--position] = Alphabet[remainder];
            number /= Base;
        }

        return new string(buffer[position..]);
    }

    public long Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code cannot be null or empty.", nameof(code));
        }

        long result = 0;
        foreach (var c in code)
        {
            var index = Alphabet.IndexOf(c);
            if (index == -1)
            {
                throw new ArgumentException($"Invalid character '{c}' in Base62 code.", nameof(code));
            }

            result = checked((result * Base) + index);
        }

        return result;
    }
}
