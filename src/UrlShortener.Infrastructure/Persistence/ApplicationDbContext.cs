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
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

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
                .HasMaxLength(30);

            builder.HasIndex(x => x.Code)
                .IsUnique();

            builder.Property(x => x.OriginalUrl)
                .IsRequired()
                .HasMaxLength(2048);

            builder.Property(x => x.CreatedAtUtc)
                .IsRequired();

            builder.Property(x => x.ExpiresAtUtc)
                .IsRequired(false);
        });

        modelBuilder.Entity<ClickEvent>(builder =>
        {
            builder.ToTable("click_events");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .UseIdentityByDefaultColumn();

            builder.Property(x => x.LinkId)
                .IsRequired();

            builder.Property(x => x.ClickedAtUtc)
                .IsRequired();

            builder.Property(x => x.Referrer)
                .HasMaxLength(200);

            builder.Property(x => x.UserAgent)
                .HasMaxLength(200);

            builder.HasIndex(x => new { x.LinkId, x.ClickedAtUtc });

            builder.HasOne<ShortLink>()
                .WithMany()
                .HasForeignKey(x => x.LinkId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
