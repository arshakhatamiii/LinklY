namespace UrlShortener.Domain.Common;

public interface IBase62Encoder
{
    string Encode(long number);
    long Decode(string code);
}
