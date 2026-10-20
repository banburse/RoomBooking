using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RoomBooking.Api.Data;

public sealed class RoomBookingDbContext(DbContextOptions<RoomBookingDbContext> options) : DbContext(options)
{
    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite nie porównuje ani nie sortuje DateTimeOffset w SQL. Trzymamy UTC ticks (long),
        // które zachowują porządek, więc filtry i ORDER BY działają po stronie bazy.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Resource>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(Resource.NameMaxLength);
            e.Property(r => r.NormalizedName).IsRequired().HasMaxLength(Resource.NameMaxLength);
            e.HasIndex(r => r.NormalizedName).IsUnique();
            e.Property(r => r.Type).HasConversion<string>().HasMaxLength(10);
            e.ToTable(t => t.HasCheckConstraint("CK_Resources_Capacity", "\"Capacity\" BETWEEN 1 AND 50"));
        });

        modelBuilder.Entity<Booking>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.BookedBy).IsRequired().HasMaxLength(Booking.BookedByMaxLength);
            e.Property(b => b.Title).HasMaxLength(Booking.TitleMaxLength);
            e.HasOne(b => b.Resource)
                .WithMany()
                .HasForeignKey(b => b.ResourceId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(b => new { b.ResourceId, b.StartUtc });
        });
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.UtcTicks,
        v => new DateTimeOffset(v, TimeSpan.Zero));
}
