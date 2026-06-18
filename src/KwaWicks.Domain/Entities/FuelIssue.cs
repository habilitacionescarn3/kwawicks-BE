namespace KwaWicks.Domain.Entities;

public class FuelIssue
{
    public string IssueId { get; set; } = Guid.NewGuid().ToString("N");
    public string VehicleId { get; set; } = "";
    public string FuelSource { get; set; } = "tank";   // "tank" | "offsite"
    public string TankId { get; set; } = "";
    public string TankIssuedBy { get; set; } = "";     // staff member who dispensed from tank
    public string SupplierStation { get; set; } = "";  // off-site station/supplier name
    public string SiteId { get; set; } = "";
    public decimal Litres { get; set; }
    public decimal? OdometerKm { get; set; }
    public decimal? CostPerLitre { get; set; }
    public decimal? TotalCost { get; set; }
    public string Reference { get; set; } = "";
    public string IssuedByName { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public string? SlipS3Key { get; set; }
}
