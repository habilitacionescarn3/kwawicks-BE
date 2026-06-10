namespace KwaWicks.Application.DTOs;

// ── Driver → API ───────────────────────────────────────────────────────────────
public class RecordLocationRequest
{
    public double  Latitude          { get; set; }
    public double  Longitude         { get; set; }
    public double? Accuracy          { get; set; } // metres
    public double? Speed             { get; set; } // m/s
    public string? DeliveryOrderId   { get; set; }
}

// ── API → Admin map ────────────────────────────────────────────────────────────
public class LiveVehicleResponse
{
    public string   DriverId              { get; set; } = "";
    public string   DriverName            { get; set; } = "";
    public double   Latitude              { get; set; }
    public double   Longitude             { get; set; }
    public double?  Accuracy              { get; set; }
    public double?  Speed                 { get; set; } // m/s
    public DateTime LastSeen              { get; set; }
    public string?  DeliveryOrderId       { get; set; }
    public int      MinutesSinceLastPing  { get; set; }
}

public class LocationHistoryPoint
{
    public double   Latitude    { get; set; }
    public double   Longitude   { get; set; }
    public DateTime RecordedAt  { get; set; }
}

public class DriverRouteResponse
{
    public string                   DriverId    { get; set; } = "";
    public string                   DriverName  { get; set; } = "";
    public List<LocationHistoryPoint> Points    { get; set; } = new();
}
