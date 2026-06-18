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

    public FuelService(IFuelRepository repo, IVehicleRepository vehicleRepo,
                       IDipTankRepository tankRepo, ISiteRepository siteRepo)
    {
        _repo        = repo;
        _vehicleRepo = vehicleRepo;
        _tankRepo    = tankRepo;
        _siteRepo    = siteRepo;
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
            VehicleId    = req.VehicleId,
            TankId       = req.TankId ?? "",
            SiteId       = req.SiteId ?? tank?.SiteId ?? "",
            Litres       = req.Litres,
            OdometerKm   = req.OdometerKm,
            CostPerLitre = req.CostPerLitre,
            TotalCost    = totalCost,
            Reference    = req.Reference?.Trim() ?? "",
            IssuedByName = issuedByName,
        };
        await _repo.CreateAsync(issue, ct);

        return new FuelIssueDto
        {
            IssueId      = issue.IssueId,
            VehicleId    = issue.VehicleId,
            FleetNumber  = vehicle?.FleetNumber ?? "",
            TankId       = issue.TankId,
            TankName     = tank?.Name ?? "",
            SiteId       = issue.SiteId,
            SiteName     = site?.Name ?? tank?.SiteId ?? "",
            Litres       = issue.Litres,
            OdometerKm   = issue.OdometerKm,
            CostPerLitre = issue.CostPerLitre,
            TotalCost    = issue.TotalCost,
            Reference    = issue.Reference,
            IssuedByName = issue.IssuedByName,
            IssuedAt     = issue.IssuedAt,
        };
    }

    public async Task<List<FuelIssueDto>> ListAsync(CancellationToken ct)
    {
        var issues   = await _repo.ListAsync(ct);
        var vehicles = (await _vehicleRepo.ListAsync(ct)).ToDictionary(v => v.VehicleId);
        var tanks    = (await _tankRepo.ListTanksAsync(ct)).ToDictionary(t => t.TankId);
        var sites    = (await _siteRepo.ListAsync(ct)).ToDictionary(s => s.SiteId);

        return issues.OrderByDescending(i => i.IssuedAt).Select(i =>
        {
            vehicles.TryGetValue(i.VehicleId, out var v);
            tanks.TryGetValue(i.TankId, out var t);
            sites.TryGetValue(i.SiteId, out var s);
            return new FuelIssueDto
            {
                IssueId      = i.IssueId,
                VehicleId    = i.VehicleId,
                FleetNumber  = v?.FleetNumber ?? i.VehicleId,
                TankId       = i.TankId,
                TankName     = t?.Name ?? "",
                SiteId       = i.SiteId,
                SiteName     = s?.Name ?? "",
                Litres       = i.Litres,
                OdometerKm   = i.OdometerKm,
                CostPerLitre = i.CostPerLitre,
                TotalCost    = i.TotalCost,
                Reference    = i.Reference,
                IssuedByName = i.IssuedByName,
                IssuedAt     = i.IssuedAt,
            };
        }).ToList();
    }
}
