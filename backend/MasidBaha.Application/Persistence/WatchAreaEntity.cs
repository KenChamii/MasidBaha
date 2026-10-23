using NetTopologySuite.Geometries;

namespace MasidBaha.Application.Persistence;

// A place a visitor wants alerts about: new reports within RadiusMeters of Location
// are pushed to that visitor's browser. Tied to the anonymous session cookie, so
// nobody signs up and nobody can see or change another visitor's areas.
public class WatchArea
{
    public Guid Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // geography(Point, 4326): X is longitude, Y is latitude
    public Point Location { get; set; } = null!;

    public int RadiusMeters { get; set; }
    public DateTime CreatedAt { get; set; }
}
