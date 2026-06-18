using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class DipTankService
{
    private readonly IDipTankRepository _repo;
    public DipTankService(IDipTankRepository repo) => _repo = repo;

    public async Task<DipTankDto> CreateTankAsync(CreateDipTankRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ArgumentException("Name is required.");
        if (req.CapacityLitres <= 0) throw new ArgumentException("CapacityLitres must be > 0.");

        var tank = new DipTank
        {
            Name           = req.Name.Trim(),
            Description    = req.Description?.Trim() ?? "",
            SiteId         = req.SiteId?.Trim() ?? "",
            FuelType       = req.FuelType,
            CapacityLitres = req.CapacityLitres,
            LowQtyLitres   = req.LowQtyLitres,
        };
        await _repo.CreateTankAsync(tank, ct);
        return TankToDto(tank);
    }

    public async Task<List<DipTankDto>> ListTanksAsync(CancellationToken ct)
    {
        var all = await _repo.ListTanksAsync(ct);
        return all.Where(t => t.IsActive).OrderBy(t => t.Name).Select(TankToDto).ToList();
    }

    public async Task<List<TankSummaryDto>> GetSummaryAsync(CancellationToken ct)
    {
        var tanks    = await _repo.ListTanksAsync(ct);
        var readings = await _repo.ListReadingsAsync(ct);

        var latestByTank = readings
            .GroupBy(r => r.TankId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RecordedAt).First());

        return tanks.Where(t => t.IsActive).Select(t =>
        {
            latestByTank.TryGetValue(t.TankId, out var latest);
            var currentLitres = latest?.ReadingLitres;
            var pct = currentLitres.HasValue && t.CapacityLitres > 0
                ? Math.Round(currentLitres.Value / t.CapacityLitres * 100, 1)
                : (decimal?)null;
            var isLow = currentLitres.HasValue && t.LowQtyLitres.HasValue && currentLitres.Value < t.LowQtyLitres.Value;

            return new TankSummaryDto
            {
                TankId        = t.TankId,
                CurrentLitres = currentLitres,
                PctFull       = pct,
                IsLow         = isLow,
                LastReadingAt = latest?.RecordedAt,
            };
        }).ToList();
    }

    public async Task<DipReadingDto> CreateReadingAsync(CreateDipReadingRequest req, string recordedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.TankId)) throw new ArgumentException("TankId is required.");
        if (req.ReadingLitres < 0) throw new ArgumentException("ReadingLitres must be >= 0.");

        var tank = await _repo.GetTankAsync(req.TankId, ct)
            ?? throw new KeyNotFoundException($"Tank {req.TankId} not found.");

        var pct = tank.CapacityLitres > 0
            ? Math.Round(req.ReadingLitres / tank.CapacityLitres * 100, 1)
            : (decimal?)null;

        var reading = new DipReading
        {
            TankId        = req.TankId,
            ReadingLitres = req.ReadingLitres,
            ReadingMm     = req.ReadingMm,
            PctFull       = pct,
            Notes         = req.Notes?.Trim() ?? "",
            RecordedBy    = recordedBy,
        };
        await _repo.CreateReadingAsync(reading, ct);
        return ReadingToDto(reading);
    }

    public async Task<List<DipReadingDto>> ListReadingsAsync(CancellationToken ct)
    {
        var all = await _repo.ListReadingsAsync(ct);
        return all.OrderByDescending(r => r.RecordedAt).Select(ReadingToDto).ToList();
    }

    private static DipTankDto TankToDto(DipTank t) => new()
    {
        TankId         = t.TankId,
        Name           = t.Name,
        Description    = t.Description,
        SiteId         = t.SiteId,
        FuelType       = t.FuelType,
        CapacityLitres = t.CapacityLitres,
        LowQtyLitres   = t.LowQtyLitres,
        IsActive       = t.IsActive,
    };

    private static DipReadingDto ReadingToDto(DipReading r) => new()
    {
        ReadingId     = r.ReadingId,
        TankId        = r.TankId,
        ReadingLitres = r.ReadingLitres,
        ReadingMm     = r.ReadingMm,
        PctFull       = r.PctFull,
        Notes         = r.Notes,
        RecordedBy    = r.RecordedBy,
        RecordedAt    = r.RecordedAt,
    };
}
