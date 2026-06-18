namespace KwaWicks.Application.DTOs;

public class FuelIssueDto
{
    public string IssueId { get; set; } = "";
    public string VehicleId { get; set; } = "";
    public string FleetNumber { get; set; } = "";
    public string TankId { get; set; } = "";
    public string TankName { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string SiteName { get; set; } = "";
    public decimal Litres { get; set; }
    public decimal? OdometerKm { get; set; }
    public decimal? CostPerLitre { get; set; }
    public decimal? TotalCost { get; set; }
    public string Reference { get; set; } = "";
    public string IssuedByName { get; set; } = "";
    public DateTime IssuedAt { get; set; }
}

public class CreateFuelIssueRequest
{
    public string VehicleId { get; set; } = "";
    public string? TankId { get; set; }
    public string? SiteId { get; set; }
    public decimal Litres { get; set; }
    public decimal? OdometerKm { get; set; }
    public decimal? CostPerLitre { get; set; }
    public string? Reference { get; set; }
}
