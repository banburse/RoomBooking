using Microsoft.EntityFrameworkCore;
using RoomBooking.Domain;

namespace RoomBooking.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RoomBookingDbContext>();

        await db.Database.MigrateAsync(cancellationToken);

        if (await db.Resources.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Resources.AddRange(
            Create("Alfa", ResourceType.Room, 8),
            Create("Beta", ResourceType.Room, 4),
            Create("Gamma", ResourceType.Room, 12),
            Create("D1", ResourceType.Desk, 1),
            Create("D2", ResourceType.Desk, 1),
            Create("D3", ResourceType.Desk, 1),
            Create("D4", ResourceType.Desk, 1));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Resource Create(string name, ResourceType type, int capacity) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = Resource.Normalize(name),
        Type = type,
        Capacity = capacity,
        IsActive = true,
    };
}
