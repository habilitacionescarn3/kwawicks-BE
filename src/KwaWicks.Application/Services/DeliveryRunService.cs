using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class DeliveryRunService : IDeliveryRunService
{
    private readonly IDeliveryRunRepository _repo;
    private readonly IDeliveryOrderRepository _deliveryRepo;
    private readonly ISpeciesRepository _speciesRepo;
    private readonly IClientRepository _clientRepo;
    private readonly IInvoiceService _invoiceService;

    public DeliveryRunService(
        IDeliveryRunRepository repo,
        IDeliveryOrderRepository deliveryRepo,
        ISpeciesRepository speciesRepo,
        IClientRepository clientRepo,
        IInvoiceService invoiceService)
    {
        _repo = repo;
        _deliveryRepo = deliveryRepo;
        _speciesRepo = speciesRepo;
        _clientRepo = clientRepo;
        _invoiceService = invoiceService;
    }

    public async Task<DeliveryRunResponse> CreateAsync(CreateDeliveryRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AssignedDriverId))
            throw new ArgumentException("AssignedDriverId is required.");

        var run = new DeliveryRun
        {
            HubId = request.HubId ?? "hub-001",
            AssignedDriverId = request.AssignedDriverId,
            AssignedDriverName = request.AssignedDriverName ?? "",
            Notes = request.Notes ?? "",
            Status = "Open",
        };

        await _repo.CreateAsync(run, ct);
        return MapToResponse(run);
    }

    public async Task<DeliveryRunResponse?> GetAsync(string id, CancellationToken ct)
    {
        var run = await _repo.GetAsync(id, ct);
        return run == null ? null : MapToResponse(run);
    }

    public async Task<List<DeliveryRunResponse>> ListAsync(string? driverId, string? status, CancellationToken ct)
    {
        var runs = await _repo.ListAsync(driverId, status, ct);
        return runs.Select(MapToResponse).ToList();
    }

    public async Task<DeliveryRunResponse> AddAllocationAsync(string id, AddDeliveryRunAllocationRequest request, CancellationToken ct)
    {
        var run = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Delivery run not found: {id}");

        if (run.Status == "Completed")
            throw new InvalidOperationException("Cannot add allocations to a completed delivery run.");

        if (string.IsNullOrWhiteSpace(request.ClientId))
            throw new ArgumentException("ClientId is required.");

        if (request.Lines == null || request.Lines.Count == 0)
            throw new ArgumentException("At least one line is required.");

        var client = await _clientRepo.GetAsync(request.ClientId, ct)
            ?? throw new InvalidOperationException($"Client not found: {request.ClientId}");

        // Validate stock and book out
        var bookedOut = new List<(string speciesId, int qty)>();
        var doLines = new List<DeliveryOrderLine>();
        var allocationLines = new List<DeliveryRunAllocationLine>();

        try
        {
            foreach (var reqLine in request.Lines)
            {
                if (reqLine.Qty <= 0)
                    throw new ArgumentException($"Quantity must be greater than zero for species {reqLine.SpeciesId}.");

                ct.ThrowIfCancellationRequested();
                var species = await _speciesRepo.GetAsync(reqLine.SpeciesId, ct)
                    ?? throw new InvalidOperationException($"Species not found: {reqLine.SpeciesId}");

                // QtyOnHandHub is already net of any booked deliveries — no further subtraction needed.
                var available = species.QtyOnHandHub;
                if (available < reqLine.Qty)
                    throw new InvalidOperationException(
                        $"Insufficient stock for {species.Name}. Available: {available}, requested: {reqLine.Qty}.");

                // Atomic book-out: deduct on-hand, add to booked
                await _speciesRepo.AdjustStockAsync(reqLine.SpeciesId, -reqLine.Qty, +reqLine.Qty, ct, minOnHandRequired: reqLine.Qty);
                bookedOut.Add((reqLine.SpeciesId, reqLine.Qty));

                var unitPrice = reqLine.UnitPrice > 0 ? reqLine.UnitPrice : (species.SellPrice ?? 0m);

                doLines.Add(new DeliveryOrderLine
                {
                    SpeciesId = reqLine.SpeciesId,
                    Quantity = reqLine.Qty,
                    UnitPrice = unitPrice,
                });

                allocationLines.Add(new DeliveryRunAllocationLine
                {
                    SpeciesId = reqLine.SpeciesId,
                    SpeciesName = species.Name ?? reqLine.SpeciesId,
                    Qty = reqLine.Qty,
                    UnitPrice = unitPrice,
                });
            }

            // Create delivery order — visible to driver immediately if already dispatched
            var deliveryOrder = new DeliveryOrder
            {
                AssignedDriverId = run.AssignedDriverId,
                AssignedDriverName = run.AssignedDriverName,
                CustomerId = client.ClientId,
                HubId = run.HubId,
                Status = run.Status == "OutForDelivery" ? "OutForDelivery" : "Open",
                DeliveryAddressLine1 = client.ClientAddress,
                City = client.ClientCity,
                Province = client.ClientProvince,
                PostalCode = client.ClientPostalCode,
                Lines = doLines,
            };

            await _deliveryRepo.CreateAsync(deliveryOrder, ct);

            run.Allocations.Add(new DeliveryRunAllocation
            {
                DeliveryOrderId = deliveryOrder.DeliveryOrderId,
                ClientId = client.ClientId,
                ClientName = client.ClientName,
                DeliveryStatus = run.Status == "OutForDelivery" ? "OutForDelivery" : "Open",
                Lines = allocationLines,
            });

            await _repo.UpdateAsync(run, ct);
        }
        catch
        {
            // Compensating rollback
            foreach (var (speciesId, qty) in bookedOut)
            {
                try
                {
                    await _speciesRepo.AdjustStockAsync(speciesId, +qty, -qty, CancellationToken.None);
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }

        return MapToResponse(run);
    }

    public async Task<DeliveryRunResponse> RemoveAllocationAsync(string id, string deliveryOrderId, CancellationToken ct)
    {
        var run = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Delivery run not found: {id}");

        var allocation = run.Allocations.FirstOrDefault(a => a.DeliveryOrderId == deliveryOrderId)
            ?? throw new InvalidOperationException($"Allocation '{deliveryOrderId}' not found on this delivery run.");

        if (allocation.DeliveryStatus == "Delivered")
            throw new InvalidOperationException("Cannot remove an allocation that has already been delivered.");

        if (!string.IsNullOrWhiteSpace(allocation.InvoiceId))
            throw new InvalidOperationException("Cannot remove an allocation that has already been invoiced.");

        // Reverse stock booking
        var reversed = new List<(string speciesId, int qty)>();
        try
        {
            foreach (var line in allocation.Lines)
            {
                ct.ThrowIfCancellationRequested();
                await _speciesRepo.AdjustStockAsync(line.SpeciesId, +line.Qty, -line.Qty, ct);
                reversed.Add((line.SpeciesId, line.Qty));
            }

            await _deliveryRepo.DeleteAsync(deliveryOrderId, ct);

            run.Allocations.Remove(allocation);
            await _repo.UpdateAsync(run, ct);
        }
        catch
        {
            // Compensating rollback — re-book the stock
            foreach (var (speciesId, qty) in reversed)
            {
                try
                {
                    await _speciesRepo.AdjustStockAsync(speciesId, -qty, +qty, CancellationToken.None);
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }

        return MapToResponse(run);
    }

    public async Task<DeliveryRunResponse> DispatchAsync(string id, CancellationToken ct)
    {
        var run = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Delivery run not found: {id}");

        if (run.Status != "Open")
            throw new InvalidOperationException($"Cannot dispatch a run with status '{run.Status}'.");

        if (run.Allocations.Count == 0)
            throw new InvalidOperationException("Cannot dispatch a run with no allocations.");

        run.Status = "OutForDelivery";

        // Promote all delivery orders to OutForDelivery
        foreach (var allocation in run.Allocations.Where(a => a.DeliveryStatus != "Delivered"))
        {
            try
            {
                var deliveryOrder = await _deliveryRepo.GetAsync(allocation.DeliveryOrderId, ct);
                if (deliveryOrder != null && deliveryOrder.Status == "Open")
                {
                    deliveryOrder.Status = "OutForDelivery";
                    await _deliveryRepo.UpdateAsync(deliveryOrder, ct);
                }
                allocation.DeliveryStatus = "OutForDelivery";
            }
            catch { /* non-fatal */ }
        }

        await _repo.UpdateAsync(run, ct);
        return MapToResponse(run);
    }

    public async Task<DeliveryRunResponse> ConfirmDeliveryAsync(
        string id, string deliveryOrderId, ConfirmDeliveryRunDeliveryRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var run = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Delivery run not found: {id}");

        var allocation = run.Allocations.FirstOrDefault(a => a.DeliveryOrderId == deliveryOrderId)
            ?? throw new InvalidOperationException($"Allocation '{deliveryOrderId}' not found on this delivery run.");

        if (allocation.DeliveryStatus == "Delivered")
            throw new InvalidOperationException("This allocation has already been delivered.");

        var doOrder = await _deliveryRepo.GetAsync(deliveryOrderId, ct)
            ?? throw new InvalidOperationException($"Delivery order not found: {deliveryOrderId}");

        if (!string.IsNullOrEmpty(doOrder.InvoiceId))
            throw new InvalidOperationException("This delivery has already been invoiced.");

        // Validate delivered quantities
        foreach (var l in request.Lines)
        {
            if (l.DeliveredQty < 0)
                throw new ArgumentException($"DeliveredQty cannot be negative for species {l.SpeciesId}.");

            var doLine = doOrder.Lines.FirstOrDefault(dl => dl.SpeciesId == l.SpeciesId)
                ?? throw new InvalidOperationException($"Species {l.SpeciesId} is not on delivery order {deliveryOrderId}.");

            if (l.DeliveredQty > doLine.Quantity)
                throw new ArgumentException(
                    $"DeliveredQty {l.DeliveredQty} exceeds ordered qty {doLine.Quantity} for species {l.SpeciesId}.");
        }

        // Build invoice lines — undelivered qty becomes NotWanted return
        var invoiceLines = doOrder.Lines.Select(doLine =>
        {
            var reqLine = request.Lines.FirstOrDefault(l => l.SpeciesId == doLine.SpeciesId);
            var deliveredQty = reqLine?.DeliveredQty ?? 0;
            var unitPrice = (reqLine?.UnitPrice ?? 0) > 0 ? reqLine!.UnitPrice : doLine.UnitPrice;
            return new CreateInvoiceFromDeliveryLine
            {
                SpeciesId            = doLine.SpeciesId,
                DeliveredQty         = deliveredQty,
                ReturnedNotWantedQty = doLine.Quantity - deliveredQty,
                ReturnedDeadQty      = 0,
                ReturnedMutilatedQty = 0,
                UnitPrice            = unitPrice,
                VatRate              = 0m,
            };
        }).ToList();

        // Force to OutForDelivery so CreateFromDeliveryAsync accepts it
        if (doOrder.Status == "Open")
        {
            doOrder.Status = "OutForDelivery";
            await _deliveryRepo.UpdateAsync(doOrder, ct);
        }

        var invoiceRequest = new CreateInvoiceFromDeliveryRequest
        {
            CreatedByDriverId = "admin",
            Lines = invoiceLines,
        };

        var invoiceId = await _invoiceService.CreateFromDeliveryAsync(deliveryOrderId, invoiceRequest, ct);

        if (!string.IsNullOrWhiteSpace(request.PaymentType))
        {
            await _invoiceService.RecordPaymentAsync(invoiceId,
                new RecordPaymentRequest { PaymentType = request.PaymentType }, ct);
        }

        // Update allocation
        allocation.DeliveryStatus = "Delivered";
        allocation.InvoiceId = invoiceId;
        allocation.PaymentType = request.PaymentType ?? "";

        foreach (var reqLine in request.Lines)
        {
            var allocLine = allocation.Lines.FirstOrDefault(l => l.SpeciesId == reqLine.SpeciesId);
            if (allocLine != null)
                allocLine.DeliveredQty = reqLine.DeliveredQty;
        }

        // Auto-complete run if all allocations are delivered
        if (run.Allocations.All(a => a.DeliveryStatus == "Delivered"))
            run.Status = "Completed";

        await _repo.UpdateAsync(run, ct);
        return MapToResponse(run);
    }

    private static DeliveryRunResponse MapToResponse(DeliveryRun run) => new()
    {
        DeliveryRunId = run.DeliveryRunId,
        HubId = run.HubId,
        AssignedDriverId = run.AssignedDriverId,
        AssignedDriverName = run.AssignedDriverName,
        Status = run.Status,
        Notes = run.Notes,
        Allocations = run.Allocations.Select(a => new DeliveryRunAllocationDto
        {
            DeliveryOrderId = a.DeliveryOrderId,
            ClientId = a.ClientId,
            ClientName = a.ClientName,
            DeliveryStatus = a.DeliveryStatus,
            InvoiceId = a.InvoiceId,
            PaymentType = a.PaymentType,
            Lines = a.Lines.Select(l => new DeliveryRunAllocationLineDto
            {
                SpeciesId = l.SpeciesId,
                SpeciesName = l.SpeciesName,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                DeliveredQty = l.DeliveredQty,
            }).ToList(),
        }).ToList(),
        CreatedAt = run.CreatedAt.ToString("O"),
        UpdatedAt = run.UpdatedAt.ToString("O"),
    };
}
