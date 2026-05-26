namespace KwaWicks.Domain.Entities;

public class StockLoss
{
    public string LossId { get; set; } = Guid.NewGuid().ToString("N");
    public string SpeciesId { get; set; } = "";
    public string SpeciesName { get; set; } = "";

    /// <summary>Number of units that died / were lost. Always positive.</summary>
    public int Qty { get; set; }

    public string Notes { get; set; } = "";
    public string RecordedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
