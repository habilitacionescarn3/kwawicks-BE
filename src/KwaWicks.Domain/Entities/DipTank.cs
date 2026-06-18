namespace KwaWicks.Domain.Entities;

public class DipTank
{
    public string TankId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string FuelType { get; set; } = "diesel";
    public decimal CapacityLitres { get; set; }
    public decimal? LowQtyLitres { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DipReading
{
    public string ReadingId { get; set; } = Guid.NewGuid().ToString("N");
    public string TankId { get; set; } = "";
    public decimal ReadingLitres { get; set; }
    public decimal? ReadingMm { get; set; }
    public decimal? PctFull { get; set; }
    public string Notes { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
