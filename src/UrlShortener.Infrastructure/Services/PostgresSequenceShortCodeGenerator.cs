using System.Data;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Common;
using UrlShortener.Domain.Services;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Services;

public class PostgresSequenceShortCodeGenerator : IShortCodeGenerator
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IBase62Encoder _encoder;

    public PostgresSequenceShortCodeGenerator(ApplicationDbContext dbContext, IBase62Encoder encoder)
    {
        _dbContext = dbContext;
        _encoder = encoder;
    }

    public async Task<(long Id, string Code)> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;

        if (wasClosed)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('{ApplicationDbContext.SequenceName}')";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var id = Convert.ToInt64(result);
            var code = _encoder.Encode(id);

            return (id, code);
        }
        finally
        {
            if (wasClosed)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }
}
