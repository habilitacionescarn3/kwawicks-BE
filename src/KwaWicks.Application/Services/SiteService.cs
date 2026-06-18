using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class SiteService
{
    private readonly ISiteRepository _repo;
    public SiteService(ISiteRepository repo) => _repo = repo;

    public async Task<SiteDto> CreateAsync(CreateSiteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ArgumentException("Name is required.");
        var site = new Site
        {
            Name         = req.Name.Trim(),
            Address      = req.Address?.Trim() ?? "",
            ContactName  = req.ContactName?.Trim() ?? "",
            ContactPhone = req.ContactPhone?.Trim() ?? "",
        };
        await _repo.CreateAsync(site, ct);
        return ToDto(site);
    }

    public async Task<List<SiteDto>> ListAsync(CancellationToken ct)
    {
        var all = await _repo.ListAsync(ct);
        return all.Where(s => s.IsActive).OrderBy(s => s.Name).Select(ToDto).ToList();
    }

    private static SiteDto ToDto(Site s) => new()
    {
        SiteId       = s.SiteId,
        Name         = s.Name,
        Address      = s.Address,
        ContactName  = s.ContactName,
        ContactPhone = s.ContactPhone,
        IsActive     = s.IsActive,
    };
}
