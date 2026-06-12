using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class DriverStockAllocationService : IDriverStockAllocationService
{
    private readonly IDriverStockAllocationRepository _repo;
    private readonly ISpeciesRepository _speciesRepo;

    public DriverStockAllocationService(
        IDriverStockAllocationRepository repo,
        ISpeciesRepository speciesRepo)
    {
        _repo = repo;
        _speciesRepo = speciesRepo;
    }

    public async Task<DriverStockAllocationResponse> CreateAsync(CreateDriverStockAllocationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DriverId))
            throw new ArgumentException("DriverId is required.");

        if (request.Lines == null || request.Lines.Count == 0)
            throw new ArgumentException("At least one line is required.");

        // Validate all lines and decrement hub stock with compensating rollback
        var decremented = new List<(string speciesId, int qty)>();
        try
        {
            foreach (var line in request.Lines)
            {
                ct.ThrowIfCancellationRequested();

                if (line.Qty <= 0)
                    throw new ArgumentException($"Qty must be greater than zero for species {line.SpeciesId}.");

                var species = await _speciesRepo.GetAsync(line.SpeciesId, ct)
                    ?? throw new InvalidOperationException($"Species not found: {line.SpeciesId}");

                if (species.QtyOnHandHub < line.Qty)
                    throw new InvalidOperationException(
                        $"Insufficient hub stock for {species.Name}. Available: {species.QtyOnHandHub}, requested: {line.Qty}.");

                await _speciesRepo.AdjustStockAsync(line.SpeciesId, -line.Qty, 0, ct, minOnHandRequired: line.Qty);
                decremented.Add((line.SpeciesId, line.Qty));
            }

            // Build allocation entity — resolve species names
            var allocationLines = new List<DriverStockAllocationLine>();
            foreach (var line in request.Lines)
            {
                var species = await _speciesRepo.GetAsync(line.SpeciesId, ct);
                allocationLines.Add(new DriverStockAllocationLine
                {
                    SpeciesId = line.SpeciesId,
                    SpeciesName = species?.Name ?? line.SpeciesId,
                    AllocatedQty = line.Qty,
                    UnitPrice = line.UnitPrice
                });
            }

            var allocation = new DriverStockAllocation
            {
                DriverId = request.DriverId,
                DriverName = request.DriverName ?? "",
                HubId = request.HubId ?? "",
                Notes = request.Notes ?? "",
                Status = "Active",
                Lines = allocationLines
            };

            await _repo.CreateAsync(allocation, ct);
            return MapToResponse(allocation);
        }
        catch
        {
            // Compensating rollback — restore decremented stock
            foreach (var (speciesId, qty) in decremented)
            {
                try
                {
                    await _speciesRepo.AdjustStockAsync(speciesId, +qty, 0, CancellationToken.None);
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }
    }

    public async Task<DriverStockAllocationResponse?> GetAsync(string id, CancellationToken ct = default)
    {
        var allocation = await _repo.GetAsync(id, ct);
        return allocation == null ? null : MapToResponse(allocation);
    }

    public async Task<List<DriverStockAllocationResponse>> ListAsync(string? driverId, string? status, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(driverId, status, ct);
        return items.Select(MapToResponse).ToList();
    }

    public async Task<DriverStockAllocationResponse> RecordSaleAsync(string id, RecordDriverSaleRequest request, CancellationToken ct = default)
    {
        var allocation = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Driver stock allocation not found: {id}");

        if (allocation.Status != "Active")
            throw new InvalidOperationException($"Cannot record a sale on an allocation with status: {allocation.Status}");

        var line = allocation.Lines.FirstOrDefault(l => l.SpeciesId == request.SpeciesId)
            ?? throw new InvalidOperationException($"Species {request.SpeciesId} is not part of this allocation.");

        if (request.Qty <= 0)
            throw new ArgumentException("Qty must be greater than zero.");

        if (request.UnitPrice <= 0)
            throw new ArgumentException("UnitPrice must be greater than zero.");

        if (!new[] { "Cash", "EFT" }.Contains(request.PaymentType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("PaymentType must be Cash or EFT.");

        var soldQty = allocation.Sales
            .Where(s => s.SpeciesId == request.SpeciesId)
            .Sum(s => s.Qty);

        var remainingQty = line.AllocatedQty - soldQty;
        if (request.Qty > remainingQty)
            throw new InvalidOperationException(
                $"Sale qty {request.Qty} exceeds remaining qty {remainingQty} for species {line.SpeciesName}.");

        allocation.Sales.Add(new DriverSaleRecord
        {
            SpeciesId = request.SpeciesId,
            SpeciesName = line.SpeciesName,
            Qty = request.Qty,
            UnitPrice = request.UnitPrice,
            TotalAmount = request.Qty * request.UnitPrice,
            PaymentType = request.PaymentType,
            CustomerName = request.CustomerName ?? "",
            SoldAt = DateTime.UtcNow
        });

        await _repo.UpdateAsync(allocation, ct);
        return MapToResponse(allocation);
    }

    public async Task<DriverStockAllocationResponse> CompleteAsync(string id, CancellationToken ct = default)
    {
        var allocation = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Driver stock allocation not found: {id}");

        if (allocation.Status != "Active")
            throw new InvalidOperationException($"Cannot complete an allocation with status: {allocation.Status}");

        await ReturnRemainingStockAsync(allocation, ct);

        allocation.Status = "Completed";
        await _repo.UpdateAsync(allocation, ct);
        return MapToResponse(allocation);
    }

    public async Task<DriverStockAllocationResponse> CancelAsync(string id, CancellationToken ct = default)
    {
        var allocation = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Driver stock allocation not found: {id}");

        if (allocation.Status != "Active")
            throw new InvalidOperationException($"Cannot cancel an allocation with status: {allocation.Status}");

        await ReturnRemainingStockAsync(allocation, ct);

        allocation.Status = "Cancelled";
        await _repo.UpdateAsync(allocation, ct);
        return MapToResponse(allocation);
    }

    // Returns unsold remaining stock to the hub with compensating rollback on failure.
    private async Task ReturnRemainingStockAsync(DriverStockAllocation allocation, CancellationToken ct)
    {
        var returned = new List<(string speciesId, int qty)>();
        try
        {
            foreach (var line in allocation.Lines)
            {
                ct.ThrowIfCancellationRequested();

                var soldQty = allocation.Sales
                    .Where(s => s.SpeciesId == line.SpeciesId)
                    .Sum(s => s.Qty);

                var remainingQty = line.AllocatedQty - soldQty;
                if (remainingQty <= 0)
                    continue;

                await _speciesRepo.AdjustStockAsync(line.SpeciesId, +remainingQty, 0, ct);
                returned.Add((line.SpeciesId, remainingQty));
            }
        }
        catch
        {
            // Compensating rollback
            foreach (var (speciesId, qty) in returned)
            {
                try
                {
                    await _speciesRepo.AdjustStockAsync(speciesId, -qty, 0, CancellationToken.None);
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }
    }

    private static DriverStockAllocationResponse MapToResponse(DriverStockAllocation allocation)
    {
        var lineResponses = allocation.Lines.Select(line =>
        {
            var soldQty = allocation.Sales
                .Where(s => s.SpeciesId == line.SpeciesId)
                .Sum(s => s.Qty);

            return new DriverStockAllocationLineResponse
            {
                SpeciesId = line.SpeciesId,
                SpeciesName = line.SpeciesName,
                AllocatedQty = line.AllocatedQty,
                SoldQty = soldQty,
                RemainingQty = line.AllocatedQty - soldQty,
                UnitPrice = line.UnitPrice
            };
        }).ToList();

        var saleResponses = allocation.Sales.Select(s => new DriverSaleRecordResponse
        {
            SaleId = s.SaleId,
            SpeciesId = s.SpeciesId,
            SpeciesName = s.SpeciesName,
            Qty = s.Qty,
            UnitPrice = s.UnitPrice,
            TotalAmount = s.TotalAmount,
            PaymentType = s.PaymentType,
            CustomerName = s.CustomerName,
            SoldAt = s.SoldAt
        }).ToList();

        return new DriverStockAllocationResponse
        {
            AllocationId = allocation.AllocationId,
            DriverId = allocation.DriverId,
            DriverName = allocation.DriverName,
            HubId = allocation.HubId,
            Status = allocation.Status,
            Notes = allocation.Notes,
            Lines = lineResponses,
            Sales = saleResponses,
            CreatedAt = allocation.CreatedAt,
            UpdatedAt = allocation.UpdatedAt
        };
    }
}
