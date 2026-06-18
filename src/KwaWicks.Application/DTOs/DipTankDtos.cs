namespace KwaWicks.Application.DTOs;

public class DipTankDto
{
    public string TankId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string FuelType { get; set; } = "";
    public decimal CapacityLitres { get; set; }
    public decimal? LowQtyLitres { get; set; }
    public bool IsActive { get; set; }
}

public class CreateDipTankRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? SiteId { get; set; }
    public string FuelType { get; set; } = "diesel";
    public decimal CapacityLitres { get; set; }
    public decimal? LowQtyLitres { get; set; }
}

public class DipReadingDto
{
    public string ReadingId { get; set; } = "";
    public string TankId { get; set; } = "";
    public decimal ReadingLitres { get; set; }
    public decimal? ReadingMm { get; set; }
    public decimal? PctFull { get; set; }
    public string Notes { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAt { get; set; }
}

public class CreateDipReadingRequest
{
    public string TankId { get; set; } = "";
    public decimal ReadingLitres { get; set; }
    public decimal? ReadingMm { get; set; }
    public string? Notes { get; set; }
}

public class TankSummaryDto
{
    public string TankId { get; set; } = "";
    public decimal? CurrentLitres { get; set; }
    public decimal? PctFull { get; set; }
    public bool IsLow { get; set; }
    public DateTime? LastReadingAt { get; set; }
}
