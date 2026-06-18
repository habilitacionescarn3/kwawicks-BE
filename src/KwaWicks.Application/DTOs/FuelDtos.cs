namespace KwaWicks.Application.DTOs;

public class FuelIssueDto
{
    public string IssueId { get; set; } = "";
    public string VehicleId { get; set; } = "";
    public string FleetNumber { get; set; } = "";
    public string FuelSource { get; set; } = "tank";
    public string TankId { get; set; } = "";
    public string TankName { get; set; } = "";
    public string TankIssuedBy { get; set; } = "";
    public string SupplierStation { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string SiteName { get; set; } = "";
    public decimal Litres { get; set; }
    public decimal? OdometerKm { get; set; }
    public decimal? CostPerLitre { get; set; }
    public decimal? TotalCost { get; set; }
    public string Reference { get; set; } = "";
    public string IssuedByName { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public string? SlipS3Key { get; set; }
    public string? SlipUrl { get; set; }
}

public class ConfirmFuelSlipRequest
{
    public string S3Key { get; set; } = "";
}

public class MonthlyFuelDto
{
    public string Month { get; set; } = "";
    public decimal Litres { get; set; }
    public decimal? Cost { get; set; }
    public int FillCount { get; set; }
}

public class VehicleFuelReportDto
{
    public string VehicleId { get; set; } = "";
    public string FleetNumber { get; set; } = "";
    public string Registration { get; set; } = "";
    public string OdoType { get; set; } = "km";
    public decimal? ExpectedConsumption { get; set; }
    public int FillCount { get; set; }
    public decimal TotalLitres { get; set; }
    public decimal? TotalCost { get; set; }
    public decimal? AvgConsumptionPer100 { get; set; }
    public bool IsOutOfRange { get; set; }
    public string TopIssuedBy { get; set; } = "";
    public List<MonthlyFuelDto> Monthly { get; set; } = [];
}

public class CreateFuelIssueRequest
{
    public string VehicleId { get; set; } = "";
    public string FuelSource { get; set; } = "tank";   // "tank" | "offsite"
    public string? TankId { get; set; }
    public string? TankIssuedBy { get; set; }
    public string? SupplierStation { get; set; }
    public string? SiteId { get; set; }
    public decimal Litres { get; set; }
    public decimal? OdometerKm { get; set; }
    public decimal? CostPerLitre { get; set; }
    public string? Reference { get; set; }
}
