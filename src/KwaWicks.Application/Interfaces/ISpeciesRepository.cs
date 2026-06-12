using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Interfaces;

public interface ISpeciesRepository
{
    Task<Species> CreateAsync(Species species, CancellationToken ct);
    Task<List<Species>> ListAsync(CancellationToken ct);
    Task<Species?> GetAsync(string speciesId, CancellationToken ct);
    Task<Species?> UpdateAsync(Species species, CancellationToken ct);

    /// <summary>
    /// Atomically adjusts QtyOnHandHub and QtyBookedOutForDelivery by the given deltas.
    /// Uses DynamoDB ADD to eliminate stale read-modify-write races.
    /// Throws InvalidOperationException if minOnHandRequired is set and stock would go below it.
    /// </summary>
    Task AdjustStockAsync(string speciesId, int onHandDelta, int bookedDelta, CancellationToken ct, int minOnHandRequired = 0);

    Task DeleteAsync(string speciesId, CancellationToken ct);
}
