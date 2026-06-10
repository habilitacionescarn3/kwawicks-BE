namespace KwaWicks.Domain.Entities;

public class VehicleLocation
{
    public string DriverId   { get; set; } = "";
    public string DriverName { get; set; } = "";
    public double Latitude   { get; set; }
    public double Longitude  { get; set; }
    public double? Accuracy  { get; set; } // metres
    public double? Speed     { get; set; } // m/s
    public DateTime RecordedAt      { get; set; } = DateTime.UtcNow;
    public string? DeliveryOrderId  { get; set; }
}
