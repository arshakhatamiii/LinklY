using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public const string SequenceName = "short_link_seq";

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasSequence<long>(SequenceName)
            .StartsAt(1_000_000)
            .IncrementsBy(1);

        modelBuilder.Entity<ShortLink>(builder =>
        {
            builder.ToTable("short_links");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.Code)
                .IsRequired()
                .HasMaxLength(16);

            builder.HasIndex(x => x.Code)
                .IsUnique();

            builder.Property(x => x.OriginalUrl)
                .IsRequired()
                .HasMaxLength(2048);

            builder.Property(x => x.CreatedAtUtc)
                .IsRequired();
        });
    }
}
