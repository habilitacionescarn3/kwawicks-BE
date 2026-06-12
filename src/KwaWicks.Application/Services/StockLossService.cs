using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class StockLossService : IStockLossService
{
    private readonly IStockLossRepository _repo;
    private readonly ISpeciesRepository _speciesRepo;

    public StockLossService(IStockLossRepository repo, ISpeciesRepository speciesRepo)
    {
        _repo = repo;
        _speciesRepo = speciesRepo;
    }

    public async Task<StockLossResponse> RecordLossAsync(
        RecordStockLossRequest request, string recordedByUserId, CancellationToken ct = default)
    {
        if (request.Qty <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");

        var species = await _speciesRepo.GetAsync(request.SpeciesId, ct)
            ?? throw new InvalidOperationException($"Species '{request.SpeciesId}' not found.");

        if (request.Qty > species.QtyOnHandHub)
            throw new ArgumentException(
                $"Cannot record a loss of {request.Qty} — only {species.QtyOnHandHub} units are on hand at the hub.");

        // Atomically decrement hub stock
        await _speciesRepo.AdjustStockAsync(request.SpeciesId, -request.Qty, 0, ct, minOnHandRequired: request.Qty);

        // Persist the audit record
        var loss = new StockLoss
        {
            SpeciesId       = species.SpeciesId,
            SpeciesName     = species.Name,
            Qty             = request.Qty,
            Notes           = request.Notes?.Trim() ?? "",
            RecordedByUserId = recordedByUserId,
        };

        await _repo.AddAsync(loss, ct);

        return Map(loss, species.QtyOnHandHub);
    }

    public async Task<List<StockLossResponse>> ListAsync(
        string? speciesId = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var losses = await _repo.ListAsync(speciesId, from, to, ct);
        return losses.Select(l => Map(l, -1)).ToList(); // QtyOnHandHubAfter not stored — return -1 for historical entries
    }

    private static StockLossResponse Map(StockLoss l, int qtyAfter) => new()
    {
        LossId             = l.LossId,
        SpeciesId          = l.SpeciesId,
        SpeciesName        = l.SpeciesName,
        Qty                = l.Qty,
        Notes              = l.Notes,
        RecordedByUserId   = l.RecordedByUserId,
        CreatedAt          = l.CreatedAt,
        QtyOnHandHubAfter  = qtyAfter,
    };
}
