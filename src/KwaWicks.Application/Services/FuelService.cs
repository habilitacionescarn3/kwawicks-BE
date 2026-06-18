using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class FuelService
{
    private readonly IFuelRepository _repo;
    private readonly IVehicleRepository _vehicleRepo;
    private readonly IDipTankRepository _tankRepo;
    private readonly ISiteRepository _siteRepo;
    private readonly IS3Service _s3;

    public FuelService(IFuelRepository repo, IVehicleRepository vehicleRepo,
                       IDipTankRepository tankRepo, ISiteRepository siteRepo,
                       IS3Service s3)
    {
        _repo        = repo;
        _vehicleRepo = vehicleRepo;
        _tankRepo    = tankRepo;
        _siteRepo    = siteRepo;
        _s3          = s3;
    }

    public async Task<FuelIssueDto> CreateAsync(CreateFuelIssueRequest req, string issuedByName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.VehicleId)) throw new ArgumentException("VehicleId is required.");
        if (req.Litres <= 0) throw new ArgumentException("Litres must be > 0.");

        var vehicle = await _vehicleRepo.GetAsync(req.VehicleId, ct);
        DipTank? tank = req.TankId is not null ? await _tankRepo.GetTankAsync(req.TankId, ct) : null;
        Site? site = req.SiteId is not null ? await _siteRepo.GetAsync(req.SiteId, ct) : null;

        var totalCost = req.CostPerLitre.HasValue ? req.Litres * req.CostPerLitre.Value : (decimal?)null;

        var issue = new FuelIssue
        {
            VehicleId       = req.VehicleId,
            FuelSource      = req.FuelSource == "offsite" ? "offsite" : "tank",
            TankId          = req.TankId ?? "",
            TankIssuedBy    = req.TankIssuedBy?.Trim() ?? "",
            SupplierStation = req.SupplierStation?.Trim() ?? "",
            SiteId          = req.SiteId ?? tank?.SiteId ?? "",
            Litres       = req.Litres,
            OdometerKm   = req.OdometerKm,
            CostPerLitre = req.CostPerLitre,
            TotalCost    = totalCost,
            Reference    = req.Reference?.Trim() ?? "",
            IssuedByName = issuedByName,
        };
        await _repo.CreateAsync(issue, ct);

        return ToDto(issue, vehicle?.FleetNumber ?? "", tank?.Name ?? "", site?.Name ?? "");
    }

    // ── Slip upload ────────────────────────────────────────────────────────────

    public async Task<(string UploadUrl, string S3Key)> GetSlipUploadUrlAsync(
        string issueId, string contentType, CancellationToken ct)
    {
        var ext = contentType.Contains("pdf") ? "pdf" : "jpg";
        var key = $"fuel-slips/{issueId}/{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}";
        var url = await _s3.GeneratePresignedUploadUrlAsync(key, contentType, ct);
        return (url, key);
    }

    public async Task<FuelIssueDto?> ConfirmSlipUploadedAsync(string issueId, string s3Key, CancellationToken ct)
    {
        var issue = await _repo.GetAsync(issueId, ct);
        if (issue is null) return null;
        issue.SlipS3Key = s3Key;
        await _repo.UpdateAsync(issue, ct);

        var vehicles = (await _vehicleRepo.ListAsync(ct)).ToDictionary(v => v.VehicleId);
        var tanks    = (await _tankRepo.ListTanksAsync(ct)).ToDictionary(t => t.TankId);
        var sites    = (await _siteRepo.ListAsync(ct)).ToDictionary(s => s.SiteId);

        vehicles.TryGetValue(issue.VehicleId, out var v);
        tanks.TryGetValue(issue.TankId, out var t);
        sites.TryGetValue(issue.SiteId, out var s);

        var dto = ToDto(issue, v?.FleetNumber ?? "", t?.Name ?? "", s?.Name ?? "");
        if (issue.SlipS3Key is not null)
            dto.SlipUrl = await _s3.GeneratePresignedViewUrlAsync(issue.SlipS3Key, 60, ct);
        return dto;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    public async Task<List<FuelIssueDto>> ListAsync(CancellationToken ct)
    {
        var issues   = await _repo.ListAsync(ct);
        var vehicles = (await _vehicleRepo.ListAsync(ct)).ToDictionary(v => v.VehicleId);
        var tanks    = (await _tankRepo.ListTanksAsync(ct)).ToDictionary(t => t.TankId);
        var sites    = (await _siteRepo.ListAsync(ct)).ToDictionary(s => s.SiteId);

        var result = new List<FuelIssueDto>();
        foreach (var i in issues.OrderByDescending(x => x.IssuedAt))
        {
            vehicles.TryGetValue(i.VehicleId, out var v);
            tanks.TryGetValue(i.TankId, out var t);
            sites.TryGetValue(i.SiteId, out var s);
            var dto = ToDto(i, v?.FleetNumber ?? i.VehicleId, t?.Name ?? "", s?.Name ?? "");
            if (i.SlipS3Key is not null)
                dto.SlipUrl = await _s3.GeneratePresignedViewUrlAsync(i.SlipS3Key, 60, ct);
            result.Add(dto);
        }
        return result;
    }

    // ── Report ────────────────────────────────────────────────────────────────

    public async Task<List<VehicleFuelReportDto>> GetReportAsync(
        string? vehicleId, string? fromDate, string? toDate, CancellationToken ct)
    {
        var allIssues = await _repo.ListAsync(ct);
        var vehicles  = (await _vehicleRepo.ListAsync(ct)).ToDictionary(v => v.VehicleId);

        // Apply vehicle filter
        if (!string.IsNullOrWhiteSpace(vehicleId))
            allIssues = allIssues.Where(i => i.VehicleId == vehicleId).ToList();

        // Apply date filter
        if (DateTime.TryParse(fromDate, out var from))
            allIssues = allIssues.Where(i => i.IssuedAt >= from).ToList();
        if (DateTime.TryParse(toDate, out var to))
            allIssues = allIssues.Where(i => i.IssuedAt <= to.AddDays(1)).ToList();

        var byVehicle = allIssues.GroupBy(i => i.VehicleId);

        var report = new List<VehicleFuelReportDto>();
        foreach (var grp in byVehicle.OrderBy(g => g.Key))
        {
            vehicles.TryGetValue(grp.Key, out var v);

            var fills        = grp.OrderBy(i => i.IssuedAt).ToList();
            var totalLitres  = fills.Sum(i => i.Litres);
            var totalCost    = fills.Any(i => i.TotalCost.HasValue) ? fills.Sum(i => i.TotalCost ?? 0) : (decimal?)null;

            // Compute avg consumption from consecutive odometer readings
            decimal? avgConsumption = null;
            var withOdo = fills.Where(i => i.OdometerKm.HasValue).OrderBy(i => i.OdometerKm).ToList();
            if (withOdo.Count >= 2)
            {
                var intervals = new List<decimal>();
                for (int idx = 1; idx < withOdo.Count; idx++)
                {
                    var kmDiff = withOdo[idx].OdometerKm!.Value - withOdo[idx - 1].OdometerKm!.Value;
                    if (kmDiff > 0)
                        intervals.Add(withOdo[idx].Litres / kmDiff * 100);
                }
                if (intervals.Count > 0)
                    avgConsumption = Math.Round(intervals.Average(), 2);
            }

            var expected = v?.ExpectedConsumption;
            var isOutOfRange = avgConsumption.HasValue && expected.HasValue
                && avgConsumption.Value > expected.Value * 1.15m;

            var topIssuer = fills
                .GroupBy(i => i.IssuedByName)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "";

            var monthly = fills
                .GroupBy(i => i.IssuedAt.ToString("yyyy-MM"))
                .OrderBy(g => g.Key)
                .Select(g => new MonthlyFuelDto
                {
                    Month     = g.Key,
                    Litres    = g.Sum(i => i.Litres),
                    Cost      = g.Any(i => i.TotalCost.HasValue) ? g.Sum(i => i.TotalCost ?? 0) : null,
                    FillCount = g.Count(),
                }).ToList();

            report.Add(new VehicleFuelReportDto
            {
                VehicleId            = grp.Key,
                FleetNumber          = v?.FleetNumber ?? grp.Key,
                Registration         = v?.Registration ?? "",
                OdoType              = v?.OdoType ?? "km",
                ExpectedConsumption  = expected,
                FillCount            = fills.Count,
                TotalLitres          = totalLitres,
                TotalCost            = totalCost,
                AvgConsumptionPer100 = avgConsumption,
                IsOutOfRange         = isOutOfRange,
                TopIssuedBy          = topIssuer,
                Monthly              = monthly,
            });
        }

        return report;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FuelIssueDto ToDto(FuelIssue i, string fleetNumber, string tankName, string siteName) => new()
    {
        IssueId         = i.IssueId,
        VehicleId       = i.VehicleId,
        FleetNumber     = fleetNumber,
        FuelSource      = i.FuelSource,
        TankId          = i.TankId,
        TankName        = tankName,
        TankIssuedBy    = i.TankIssuedBy,
        SupplierStation = i.SupplierStation,
        SiteId          = i.SiteId,
        SiteName        = siteName,
        Litres       = i.Litres,
        OdometerKm   = i.OdometerKm,
        CostPerLitre = i.CostPerLitre,
        TotalCost    = i.TotalCost,
        Reference    = i.Reference,
        IssuedByName = i.IssuedByName,
        IssuedAt     = i.IssuedAt,
        SlipS3Key    = i.SlipS3Key,
    };
}
