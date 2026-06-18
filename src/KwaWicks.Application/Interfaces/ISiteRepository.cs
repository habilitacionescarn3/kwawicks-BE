using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Interfaces;

public interface ISiteRepository
{
    Task<Site> CreateAsync(Site site, CancellationToken ct);
    Task<Site?> GetAsync(string siteId, CancellationToken ct);
    Task<Site> UpdateAsync(Site site, CancellationToken ct);
    Task<List<Site>> ListAsync(CancellationToken ct);
}
