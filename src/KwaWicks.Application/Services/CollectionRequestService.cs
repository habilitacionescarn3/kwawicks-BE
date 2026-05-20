using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class CollectionRequestService : ICollectionRequestService
{
    private readonly ICollectionRequestRepository _repo;
    private readonly IProcurementOrderRepository _poRepo;
    private readonly ISpeciesRepository _speciesRepo;
    private readonly IDeliveryOrderRepository _deliveryRepo;
    private readonly IClientRepository _clientRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IInvoiceService _invoiceService;
    private readonly IS3Service _s3;

    public CollectionRequestService(
        ICollectionRequestRepository repo,
        IProcurementOrderRepository poRepo,
        ISpeciesRepository speciesRepo,
        IDeliveryOrderRepository deliveryRepo,
        IClientRepository clientRepo,
        IInvoiceRepository invoiceRepo,
        IInvoiceService invoiceService,
        IS3Service s3)
    {
        _repo = repo;
        _poRepo = poRepo;
        _speciesRepo = speciesRepo;
        _deliveryRepo = deliveryRepo;
        _clientRepo = clientRepo;
        _invoiceRepo = invoiceRepo;
        _invoiceService = invoiceService;
        _s3 = s3;
    }

    public async Task<CollectionRequestResponse> CreateAsync(CreateCollectionRequestRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProcurementOrderId)) throw new ArgumentException("ProcurementOrderId is required.");
        if (string.IsNullOrWhiteSpace(request.AssignedDriverId)) throw new ArgumentException("AssignedDriverId is required.");

        var po = await _poRepo.GetAsync(request.ProcurementOrderId, ct)
            ?? throw new InvalidOperationException($"Procurement order not found: {request.ProcurementOrderId}");

        if (po.Status != "Submitted" && po.Status != "CollectionScheduled")
            throw new InvalidOperationException($"Procurement order must be Submitted or CollectionScheduled to create a collection request. Current status: {po.Status}");

        var cr = new CollectionRequest
        {
            ProcurementOrderId = request.ProcurementOrderId,
            SupplierId = po.SupplierId,
            SupplierName = po.SupplierName,
            AssignedDriverId = request.AssignedDriverId,
            AssignedDriverName = request.AssignedDriverName ?? "",
            HubId = request.HubId ?? "hub-001",
            Notes = request.Notes ?? "",
            CollectionDate = request.CollectionDate,
            Status = "Pending",
            Lines = po.Lines.Select(l => new CollectionRequestLine
            {
                SpeciesId = l.SpeciesId,
                SpeciesName = l.SpeciesName,
                OrderedQty = l.OrderedQty,
                LoadedQty = 0,
                LoadingNotes = "",
                ReceivedQty = 0,
                DiscrepancyNotes = ""
            }).ToList()
        };

        await _repo.CreateAsync(cr, ct);

        // Advance PO to CollectionScheduled
        if (po.Status == "Submitted")
        {
            po.Status = "CollectionScheduled";
            await _poRepo.UpdateAsync(po, ct);
        }

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse?> GetAsync(string id, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct);
        return cr == null ? null : await MapToResponseAsync(cr, ct);
    }

    public async Task<List<CollectionRequestResponse>> ListAsync(string? driverId = null, string? status = null, string? procurementOrderId = null, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(driverId, status, procurementOrderId, ct);
        var results = new List<CollectionRequestResponse>(items.Count);
        foreach (var item in items)
            results.Add(await MapToResponseAsync(item, ct));
        return results;
    }

    public async Task<CollectionRequestResponse> DriverLoadAsync(string id, DriverLoadingUpdateRequest request, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        if (cr.Status != "Pending" && cr.Status != "Loading")
            throw new InvalidOperationException($"Cannot update loading for status: {cr.Status}");

        foreach (var update in request.Lines)
        {
            var line = cr.Lines.FirstOrDefault(l => l.SpeciesId == update.SpeciesId);
            if (line != null)
            {
                line.LoadedQty = update.LoadedQty;
                line.LoadingNotes = update.LoadingNotes ?? "";
            }
        }

        cr.Status = "Loading";
        cr.ShortfallFlagged = cr.Lines.Any(l => l.LoadedQty < l.OrderedQty);
        await _repo.UpdateAsync(cr, ct);
        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> DispatchAsync(string id, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        if (cr.Status != "Loading" && cr.Status != "Pending")
            throw new InvalidOperationException($"Cannot dispatch from status: {cr.Status}");

        cr.Status = "InTransit";
        await _repo.UpdateAsync(cr, ct);

        // Stock is now on the driver's vehicle — activate any delivery orders that were
        // waiting for collection to begin (AwaitingCollection → OutForDelivery).
        // HUB allocations (sentinel DeliveryOrderId = "HUB") have no delivery order — skip them.
        foreach (var allocation in cr.DeliveryAllocations.Where(a => a.DeliveryOrderId != "HUB"))
        {
            try
            {
                var deliveryOrder = await _deliveryRepo.GetAsync(allocation.DeliveryOrderId, ct);
                if (deliveryOrder != null && deliveryOrder.Status == "AwaitingCollection")
                {
                    deliveryOrder.Status = "OutForDelivery";
                    await _deliveryRepo.UpdateAsync(deliveryOrder, ct);
                }
            }
            catch { /* non-fatal — delivery order visibility degrades gracefully */ }
        }

        // Advance PO to InTransit
        var po = await _poRepo.GetAsync(cr.ProcurementOrderId, ct);
        if (po != null && po.Status == "CollectionScheduled")
        {
            po.Status = "InTransit";
            await _poRepo.UpdateAsync(po, ct);
        }

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> ArriveAsync(string id, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        if (cr.Status != "InTransit")
            throw new InvalidOperationException($"Cannot mark arrived from status: {cr.Status}");

        cr.Status = "ArrivedAtHub";
        await _repo.UpdateAsync(cr, ct);
        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> HubConfirmAsync(string id, HubConfirmReceiptRequest request, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        if (cr.Status != "ArrivedAtHub" && cr.Status != "InTransit")
            throw new InvalidOperationException($"Cannot confirm receipt from status: {cr.Status}");

        // Guard against double-confirmation
        if (cr.Status == "HubConfirmed")
            throw new InvalidOperationException("Collection request is already confirmed.");

        // Update received quantities
        foreach (var update in request.Lines)
        {
            var line = cr.Lines.FirstOrDefault(l => l.SpeciesId == update.SpeciesId);
            if (line != null)
            {
                line.ReceivedQty = update.ReceivedQty;
                line.DiscrepancyNotes = update.DiscrepancyNotes ?? "";
            }
        }

        cr.Status = "HubConfirmed";

        // Book stock into hub with compensating rollback
        var bookedIn = new List<(string speciesId, int qty)>();
        try
        {
            foreach (var line in cr.Lines.Where(l => l.ReceivedQty > 0))
            {
                ct.ThrowIfCancellationRequested();
                var species = await _speciesRepo.GetAsync(line.SpeciesId, ct);
                if (species != null)
                {
                    species.QtyOnHandHub += line.ReceivedQty;
                    await _speciesRepo.UpdateAsync(species, ct);
                    bookedIn.Add((line.SpeciesId, line.ReceivedQty));
                }
            }

            await _repo.UpdateAsync(cr, ct);

            // Advance PO to DeliveredToHub
            var po = await _poRepo.GetAsync(cr.ProcurementOrderId, ct);
            if (po != null && (po.Status == "InTransit" || po.Status == "CollectionScheduled"))
            {
                po.Status = "DeliveredToHub";
                await _poRepo.UpdateAsync(po, ct);
            }
        }
        catch
        {
            // Compensating rollback
            foreach (var (speciesId, qty) in bookedIn)
            {
                try
                {
                    var s = await _speciesRepo.GetAsync(speciesId, ct);
                    if (s != null)
                    {
                        s.QtyOnHandHub = Math.Max(0, s.QtyOnHandHub - qty);
                        await _speciesRepo.UpdateAsync(s, ct);
                    }
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> FinanceAcknowledgeAsync(string id, string invoiceS3Key, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        if (cr.Status != "HubConfirmed")
            throw new InvalidOperationException($"Collection request must be HubConfirmed to acknowledge. Current: {cr.Status}");

        cr.Status = "FinanceAcknowledged";
        cr.InvoiceS3Key = invoiceS3Key ?? cr.InvoiceS3Key;
        await _repo.UpdateAsync(cr, ct);

        // Complete the PO
        var po = await _poRepo.GetAsync(cr.ProcurementOrderId, ct);
        if (po != null && po.Status == "DeliveredToHub")
        {
            po.Status = "Completed";
            await _poRepo.UpdateAsync(po, ct);
        }

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionInvoiceUploadUrlResponse> GetInvoiceUploadUrlAsync(string id, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        var key = $"collection/invoices/{id}/{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var url = await _s3.GeneratePresignedUploadUrlAsync(key, "application/pdf", ct);

        cr.InvoiceS3Key = key;
        await _repo.UpdateAsync(cr, ct);

        return new CollectionInvoiceUploadUrlResponse { UploadUrl = url, S3Key = key };
    }

    public async Task<string> GetDeliveryNoteViewUrlAsync(string id, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        if (string.IsNullOrWhiteSpace(cr.DeliveryNoteS3Key))
            throw new InvalidOperationException("No delivery note has been uploaded for this collection request.");

        return await _s3.GeneratePresignedViewUrlAsync(cr.DeliveryNoteS3Key, 15, ct);
    }

    public async Task<CollectionInvoiceUploadUrlResponse> GetDeliveryNoteUploadUrlAsync(string id, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        var key = $"collection/delivery-notes/{id}/{DateTime.UtcNow:yyyyMMddHHmmss}.jpg";
        var url = await _s3.GeneratePresignedUploadUrlAsync(key, "image/jpeg", ct);

        cr.DeliveryNoteS3Key = key;
        await _repo.UpdateAsync(cr, ct);

        return new CollectionInvoiceUploadUrlResponse { UploadUrl = url, S3Key = key };
    }

    public async Task<CollectionRequestResponse> AddDeliveryAllocationAsync(string id, AddDeliveryAllocationRequest request, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        var allocatableStatuses = new[] { "Pending", "Loading", "InTransit", "ArrivedAtHub", "HubConfirmed" };
        if (!allocatableStatuses.Contains(cr.Status))
            throw new InvalidOperationException($"Allocations can only be added before the collection is finalised. Current: {cr.Status}");

        if (string.IsNullOrWhiteSpace(request.ClientId))
            throw new ArgumentException("ClientId is required.");

        if (request.Lines == null || request.Lines.Count == 0)
            throw new ArgumentException("At least one allocation line is required.");

        // ── Hub-direct allocation (sentinel clientId = "HUB") ──────────────────
        // Stock earmarked for the hub stays on site — no delivery order, no QtyBookedOutForDelivery.
        if (request.ClientId.Equals("HUB", StringComparison.OrdinalIgnoreCase))
        {
            // Validate quantities (same rules as client allocations)
            foreach (var reqLine in request.Lines)
            {
                var crLine = cr.Lines.FirstOrDefault(l => l.SpeciesId == reqLine.SpeciesId)
                    ?? throw new InvalidOperationException($"Species {reqLine.SpeciesId} is not part of this collection.");

                if (reqLine.Qty <= 0)
                    throw new ArgumentException($"Quantity for species {reqLine.SpeciesId} must be greater than zero.");

                var alreadyAllocated = cr.DeliveryAllocations
                    .SelectMany(a => a.Lines)
                    .Where(l => l.SpeciesId == reqLine.SpeciesId)
                    .Sum(l => l.Qty);

                var effectiveQty = crLine.LoadedQty > 0 ? crLine.LoadedQty : crLine.OrderedQty;
                if (alreadyAllocated + reqLine.Qty > effectiveQty)
                    throw new InvalidOperationException(
                        $"Hub allocation of {reqLine.Qty} for species {reqLine.SpeciesId} exceeds available qty. " +
                        $"{(crLine.LoadedQty > 0 ? "Loaded" : "Ordered")}: {effectiveQty}, already allocated: {alreadyAllocated}.");
            }

            var hubLines = new List<CollectionAllocationLine>();
            foreach (var reqLine in request.Lines)
            {
                var species = await _speciesRepo.GetAsync(reqLine.SpeciesId, ct);
                hubLines.Add(new CollectionAllocationLine
                {
                    SpeciesId   = reqLine.SpeciesId,
                    SpeciesName = species?.Name ?? reqLine.SpeciesId,
                    Qty         = reqLine.Qty,
                    UnitPrice   = 0m, // hub stock has no outgoing sale price
                });
            }

            cr.DeliveryAllocations.Add(new CollectionDeliveryAllocation
            {
                DeliveryOrderId = "HUB", // sentinel — not a real delivery order
                ClientId        = "HUB",
                ClientName      = "Hub Stock",
                Lines           = hubLines,
            });

            await _repo.UpdateAsync(cr, ct);
            return await MapToResponseAsync(cr, ct);
        }

        // ── Regular client allocation ───────────────────────────────────────────
        var client = await _clientRepo.GetAsync(request.ClientId, ct)
            ?? throw new InvalidOperationException($"Client not found: {request.ClientId}");

        // Validate quantities: cannot allocate more than ordered across all allocations per species
        foreach (var reqLine in request.Lines)
        {
            var crLine = cr.Lines.FirstOrDefault(l => l.SpeciesId == reqLine.SpeciesId)
                ?? throw new InvalidOperationException($"Species {reqLine.SpeciesId} is not part of this collection.");

            if (reqLine.Qty <= 0)
                throw new ArgumentException($"Quantity for species {reqLine.SpeciesId} must be greater than zero.");

            // Sum already-allocated qty for this species across existing allocations
            var alreadyAllocated = cr.DeliveryAllocations
                .SelectMany(a => a.Lines)
                .Where(l => l.SpeciesId == reqLine.SpeciesId)
                .Sum(l => l.Qty);

            // Use loaded qty as the cap once the driver has loaded (more accurate than ordered qty when there's a shortfall)
            var effectiveQty = crLine.LoadedQty > 0 ? crLine.LoadedQty : crLine.OrderedQty;
            if (alreadyAllocated + reqLine.Qty > effectiveQty)
                throw new InvalidOperationException(
                    $"Allocation of {reqLine.Qty} for species {reqLine.SpeciesId} exceeds available qty. " +
                    $"{(crLine.LoadedQty > 0 ? "Loaded" : "Ordered")}: {effectiveQty}, already allocated: {alreadyAllocated}.");
        }

        // Build the DeliveryOrder lines, resolving species names
        var doLines = new List<DeliveryOrderLine>();
        var allocationLines = new List<CollectionAllocationLine>();

        foreach (var reqLine in request.Lines)
        {
            var species = await _speciesRepo.GetAsync(reqLine.SpeciesId, ct);
            var speciesName = species?.Name ?? reqLine.SpeciesId;
            var unitPrice = reqLine.UnitPrice ?? 0m;

            doLines.Add(new DeliveryOrderLine
            {
                SpeciesId = reqLine.SpeciesId,
                Quantity = reqLine.Qty,
                UnitPrice = unitPrice
            });

            allocationLines.Add(new CollectionAllocationLine
            {
                SpeciesId = reqLine.SpeciesId,
                SpeciesName = speciesName,
                Qty = reqLine.Qty,
                UnitPrice = unitPrice
            });

            // Book out of hub inventory as en-route: only QtyBookedOutForDelivery increases
            // (stock is on the truck, not physically at hub — QtyOnHandHub is NOT decremented here)
            if (species != null)
            {
                species.QtyBookedOutForDelivery += reqLine.Qty;
                await _speciesRepo.UpdateAsync(species, ct);
            }
        }

        // Delivery orders are hidden from drivers until stock is actually collected from the supplier.
        // Once the driver dispatches (InTransit), DispatchAsync promotes them to OutForDelivery.
        // If the allocation is added after dispatch the stock is already on the vehicle — visible immediately.
        var stockAlreadyCollected = cr.Status is "InTransit" or "ArrivedAtHub" or "HubConfirmed";
        var deliveryOrder = new KwaWicks.Domain.Entities.DeliveryOrder
        {
            AssignedDriverId = cr.AssignedDriverId,
            AssignedDriverName = cr.AssignedDriverName,
            CustomerId = client.ClientId,
            HubId = cr.HubId,
            Status = stockAlreadyCollected ? "OutForDelivery" : "AwaitingCollection",
            DeliveryAddressLine1 = client.ClientAddress,
            City = client.ClientCity,
            Province = client.ClientProvince,
            PostalCode = client.ClientPostalCode,
            Lines = doLines
        };

        await _deliveryRepo.CreateAsync(deliveryOrder, ct);

        // Record allocation on the collection request
        cr.DeliveryAllocations.Add(new CollectionDeliveryAllocation
        {
            DeliveryOrderId = deliveryOrder.DeliveryOrderId,
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            Lines = allocationLines
        });

        await _repo.UpdateAsync(cr, ct);

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> EditDeliveryAllocationAsync(
        string id, string deliveryOrderId, EditAllocationRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Lines == null || request.Lines.Count == 0)
            throw new ArgumentException("At least one line is required.");

        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        var allocation = cr.DeliveryAllocations.FirstOrDefault(a => a.DeliveryOrderId == deliveryOrderId)
            ?? throw new InvalidOperationException($"Allocation for delivery order {deliveryOrderId} not found on this collection request.");

        // ── Hub-direct allocation edit (no delivery order, no stock adjustment) ──
        if (deliveryOrderId == "HUB")
        {
            foreach (var edit in request.Lines)
            {
                if (edit.Qty <= 0)
                    throw new ArgumentException($"Quantity must be greater than 0 for species {edit.SpeciesId}.");

                // Validate against cap (excluding this allocation's own current qty)
                var crLine = cr.Lines.FirstOrDefault(l => l.SpeciesId == edit.SpeciesId);
                if (crLine != null)
                {
                    var otherAllocated = cr.DeliveryAllocations
                        .Where(a => a.DeliveryOrderId != "HUB")
                        .SelectMany(a => a.Lines)
                        .Where(l => l.SpeciesId == edit.SpeciesId)
                        .Sum(l => l.Qty);
                    var effectiveQty = crLine.LoadedQty > 0 ? crLine.LoadedQty : crLine.OrderedQty;
                    if (otherAllocated + edit.Qty > effectiveQty)
                        throw new InvalidOperationException(
                            $"Hub allocation {edit.Qty} for species {edit.SpeciesId} combined with client allocations ({otherAllocated}) " +
                            $"exceeds the {(crLine.LoadedQty > 0 ? "loaded" : "ordered")} qty ({effectiveQty}).");
                }

                var hubLine = allocation.Lines.FirstOrDefault(l => l.SpeciesId == edit.SpeciesId);
                if (hubLine != null) hubLine.Qty = edit.Qty;
            }

            cr.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(cr, ct);
            return await MapToResponseAsync(cr, ct);
        }

        // ── Regular client allocation edit ─────────────────────────────────────
        var deliveryOrder = await _deliveryRepo.GetAsync(deliveryOrderId, ct)
            ?? throw new InvalidOperationException($"Delivery order not found: {deliveryOrderId}");

        var terminatedStatuses = new[] { "Delivered", "MarkedAtHub" };
        if (terminatedStatuses.Contains(deliveryOrder.Status))
            throw new InvalidOperationException("Cannot edit an allocation that has already been delivered.");

        foreach (var edit in request.Lines)
        {
            if (edit.Qty <= 0)
                throw new ArgumentException($"Quantity must be greater than 0 for species {edit.SpeciesId}.");
        }

        // Validate: total allocation across all clients (excluding this one) + new qty <= ordered qty
        foreach (var edit in request.Lines)
        {
            var crLine = cr.Lines.FirstOrDefault(l => l.SpeciesId == edit.SpeciesId);
            if (crLine == null) continue;

            var otherAllocated = cr.DeliveryAllocations
                .Where(a => a.DeliveryOrderId != deliveryOrderId)
                .SelectMany(a => a.Lines)
                .Where(l => l.SpeciesId == edit.SpeciesId)
                .Sum(l => l.Qty);

            // Use loaded qty as the cap once the driver has loaded (more accurate than ordered qty when there's a shortfall)
            var effectiveEditQty = crLine.LoadedQty > 0 ? crLine.LoadedQty : crLine.OrderedQty;
            if (otherAllocated + edit.Qty > effectiveEditQty)
                throw new InvalidOperationException(
                    $"New qty {edit.Qty} for species {edit.SpeciesId} combined with other allocations ({otherAllocated}) " +
                    $"exceeds the {(crLine.LoadedQty > 0 ? "loaded" : "ordered")} qty ({effectiveEditQty}).");
        }

        // Adjust QtyBookedOutForDelivery for each changed line
        var adjustedSpecies = new List<(string speciesId, int delta)>();
        try
        {
            foreach (var edit in request.Lines)
            {
                ct.ThrowIfCancellationRequested();

                var allocationLine = allocation.Lines.FirstOrDefault(l => l.SpeciesId == edit.SpeciesId);
                if (allocationLine == null) continue;

                int delta = edit.Qty - allocationLine.Qty;
                if (delta != 0)
                {
                    var species = await _speciesRepo.GetAsync(edit.SpeciesId, ct);
                    if (species != null)
                    {
                        species.QtyBookedOutForDelivery = Math.Max(0, species.QtyBookedOutForDelivery + delta);
                        await _speciesRepo.UpdateAsync(species, ct);
                        adjustedSpecies.Add((edit.SpeciesId, delta));
                    }
                }

                // Update the delivery order line
                var doLine = deliveryOrder.Lines.FirstOrDefault(l => l.SpeciesId == edit.SpeciesId);
                if (doLine != null)
                {
                    doLine.Quantity = edit.Qty;
                    doLine.UnitPrice = edit.UnitPrice;
                }

                // Update the allocation record on the CR
                allocationLine.Qty = edit.Qty;
                allocationLine.UnitPrice = edit.UnitPrice;
            }

            deliveryOrder.UpdatedAt = DateTime.UtcNow;
            await _deliveryRepo.UpdateAsync(deliveryOrder, ct);

            cr.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(cr, ct);
        }
        catch
        {
            // Rollback QtyBookedOutForDelivery adjustments
            foreach (var (speciesId, delta) in adjustedSpecies)
            {
                try
                {
                    var s = await _speciesRepo.GetAsync(speciesId, ct);
                    if (s != null)
                    {
                        s.QtyBookedOutForDelivery = Math.Max(0, s.QtyBookedOutForDelivery - delta);
                        await _speciesRepo.UpdateAsync(s, ct);
                    }
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> SetRoadsideSalesAsync(string id, SetRoadsideSalesRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var cr = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {id}");

        if (cr.Status == "FinanceAcknowledged")
            throw new InvalidOperationException("Cannot record roadside sales after the collection has been finance-acknowledged.");

        // Validate each line
        foreach (var line in request.Lines)
        {
            if (line.Qty <= 0)
                throw new ArgumentException($"Quantity must be greater than zero for species {line.SpeciesId}.");

            if (!new[] { "Cash", "EFT" }.Contains(line.PaymentType, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"PaymentType must be Cash or EFT for species {line.SpeciesId}.");

            if (!cr.Lines.Any(l => l.SpeciesId == line.SpeciesId))
                throw new InvalidOperationException($"Species {line.SpeciesId} is not part of this collection request.");
        }

        // Validate per-species: total roadside qty <= hub return available (loaded - client allocated)
        var bySpecies = request.Lines.GroupBy(l => l.SpeciesId);
        foreach (var group in bySpecies)
        {
            var crLine = cr.Lines.First(l => l.SpeciesId == group.Key);
            var baseQty = crLine.LoadedQty > 0 ? crLine.LoadedQty : crLine.OrderedQty;
            var clientAllocated = cr.DeliveryAllocations
                .SelectMany(a => a.Lines)
                .Where(l => l.SpeciesId == group.Key)
                .Sum(l => l.Qty);
            var hubReturn = baseQty - clientAllocated;
            var roadsaleTotal = group.Sum(l => l.Qty);

            if (roadsaleTotal > hubReturn)
                throw new InvalidOperationException(
                    $"Roadside sales of {roadsaleTotal} for {crLine.SpeciesName} exceed the hub return available ({hubReturn}).");
        }

        // Replace roadside sales (PUT semantics — idempotent re-save)
        cr.RoadsideSales = request.Lines.Select(l =>
        {
            var crLine = cr.Lines.First(x => x.SpeciesId == l.SpeciesId);
            return new Domain.Entities.CollectionRoadsaleLine
            {
                SpeciesId   = l.SpeciesId,
                SpeciesName = crLine.SpeciesName,
                Qty         = l.Qty,
                UnitPrice   = l.UnitPrice,
                PaymentType = l.PaymentType,
            };
        }).ToList();

        await _repo.UpdateAsync(cr, ct);
        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> ConfirmDeliveryAsync(
        string crId, string deliveryOrderId, AdminConfirmDeliveryRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var cr = await _repo.GetAsync(crId, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {crId}");

        var allocation = cr.DeliveryAllocations.FirstOrDefault(a => a.DeliveryOrderId == deliveryOrderId)
            ?? throw new InvalidOperationException($"Allocation for delivery order {deliveryOrderId} not found on this collection request.");

        if (deliveryOrderId == "HUB")
            throw new InvalidOperationException("Hub-direct allocations do not require delivery confirmation — stock stays at the hub.");

        var doOrder = await _deliveryRepo.GetAsync(deliveryOrderId, ct)
            ?? throw new InvalidOperationException($"Delivery order not found: {deliveryOrderId}");

        if (!string.IsNullOrEmpty(doOrder.InvoiceId))
            throw new InvalidOperationException("This delivery has already been invoiced. Use the invoice management flow to make changes.");

        if (doOrder.Status != "OutForDelivery" && doOrder.Status != "AwaitingCollection")
            throw new InvalidOperationException($"Cannot confirm delivery for an order with status '{doOrder.Status}'.");

        // Validate requested delivered quantities
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

        // Build invoice lines: include ALL delivery order lines so stock reconciliation is complete.
        // Any species not in the admin request defaults to deliveredQty=0 (all returned as NotWanted).
        var invoiceLines = doOrder.Lines.Select(doLine =>
        {
            var reqLine = request.Lines.FirstOrDefault(l => l.SpeciesId == doLine.SpeciesId);
            var deliveredQty = reqLine?.DeliveredQty ?? 0;
            var unitPrice    = (reqLine?.UnitPrice ?? 0) > 0 ? reqLine!.UnitPrice : doLine.UnitPrice;
            return new CreateInvoiceFromDeliveryLine
            {
                SpeciesId              = doLine.SpeciesId,
                DeliveredQty           = deliveredQty,
                ReturnedNotWantedQty   = doLine.Quantity - deliveredQty,
                ReturnedDeadQty        = 0,
                ReturnedMutilatedQty   = 0,
                UnitPrice              = unitPrice,
                VatRate                = 0m, // admin confirmations use VAT-inclusive prices
            };
        }).ToList();

        // Force status to OutForDelivery so CreateFromDeliveryAsync accepts it
        if (doOrder.Status == "AwaitingCollection")
        {
            doOrder.Status = "OutForDelivery";
            await _deliveryRepo.UpdateAsync(doOrder, ct);
        }

        var invoiceRequest = new CreateInvoiceFromDeliveryRequest
        {
            CreatedByDriverId = "admin",
            Lines             = invoiceLines,
        };

        var invoiceId = await _invoiceService.CreateFromDeliveryAsync(deliveryOrderId, invoiceRequest, ct);

        // Record payment type on the invoice
        if (!string.IsNullOrWhiteSpace(request.PaymentType))
        {
            await _invoiceService.RecordPaymentAsync(invoiceId,
                new RecordPaymentRequest { PaymentType = request.PaymentType }, ct);
        }

        // Re-fetch CR so MapToResponseAsync picks up the fresh delivery order data
        var updatedCr = await _repo.GetAsync(crId, ct) ?? cr;
        return await MapToResponseAsync(updatedCr, ct);
    }

    public async Task<CollectionRequestResponse> HubAcceptAllocationAsync(
        string crId, HubAcceptAllocationRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var cr = await _repo.GetAsync(crId, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {crId}");

        var hubAlloc = cr.DeliveryAllocations.FirstOrDefault(a => a.DeliveryOrderId == "HUB")
            ?? throw new InvalidOperationException("No HUB allocation found on this collection request.");

        if (hubAlloc.HubAcceptanceStatus == "Accepted")
            throw new InvalidOperationException("Hub allocation has already been accepted.");

        if (request.Lines == null || request.Lines.Count == 0)
            throw new ArgumentException("At least one line is required.");

        // Validate each line
        foreach (var reqLine in request.Lines)
        {
            if (reqLine.AcceptedQty < 0)
                throw new ArgumentException($"AcceptedQty cannot be negative for species {reqLine.SpeciesId}.");

            var allocLine = hubAlloc.Lines.FirstOrDefault(l => l.SpeciesId == reqLine.SpeciesId)
                ?? throw new InvalidOperationException($"Species {reqLine.SpeciesId} is not in the hub allocation.");

            if (reqLine.AcceptedQty > allocLine.Qty)
                throw new ArgumentException(
                    $"AcceptedQty {reqLine.AcceptedQty} exceeds allocated qty {allocLine.Qty} for species {reqLine.SpeciesId}.");
        }

        // Increment hub inventory and record accepted qty per line
        var bookedIn = new List<(string speciesId, int qty)>();
        try
        {
            foreach (var reqLine in request.Lines.Where(l => l.AcceptedQty > 0))
            {
                ct.ThrowIfCancellationRequested();
                var species = await _speciesRepo.GetAsync(reqLine.SpeciesId, ct);
                if (species != null)
                {
                    species.QtyOnHandHub += reqLine.AcceptedQty;
                    await _speciesRepo.UpdateAsync(species, ct);
                    bookedIn.Add((reqLine.SpeciesId, reqLine.AcceptedQty));
                }

                var allocLine = hubAlloc.Lines.First(l => l.SpeciesId == reqLine.SpeciesId);
                allocLine.AcceptedQty = reqLine.AcceptedQty;
            }

            hubAlloc.HubAcceptanceStatus = "Accepted";
            hubAlloc.HubAcceptedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(cr, ct);
        }
        catch
        {
            // Compensating rollback
            foreach (var (speciesId, qty) in bookedIn)
            {
                try
                {
                    var s = await _speciesRepo.GetAsync(speciesId, ct);
                    if (s != null)
                    {
                        s.QtyOnHandHub = Math.Max(0, s.QtyOnHandHub - qty);
                        await _speciesRepo.UpdateAsync(s, ct);
                    }
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<CollectionRequestResponse> RemoveDeliveryAllocationAsync(
        string crId, string deliveryOrderId, CancellationToken ct = default)
    {
        var cr = await _repo.GetAsync(crId, ct)
            ?? throw new InvalidOperationException($"Collection request not found: {crId}");

        var allocation = cr.DeliveryAllocations.FirstOrDefault(a => a.DeliveryOrderId == deliveryOrderId)
            ?? throw new InvalidOperationException($"Allocation '{deliveryOrderId}' not found on this collection request.");

        // ── HUB allocation ──────────────────────────────────────────────────────
        if (deliveryOrderId == "HUB")
        {
            if (allocation.HubAcceptanceStatus == "Accepted")
                throw new InvalidOperationException(
                    "Cannot remove a HUB allocation that has already been accepted — stock has been added to hub inventory.");

            cr.DeliveryAllocations.Remove(allocation);
            cr.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(cr, ct);
            return await MapToResponseAsync(cr, ct);
        }

        // ── Client delivery allocation ──────────────────────────────────────────
        var deliveryOrder = await _deliveryRepo.GetAsync(deliveryOrderId, ct)
            ?? throw new InvalidOperationException($"Delivery order not found: {deliveryOrderId}");

        if (!string.IsNullOrWhiteSpace(deliveryOrder.InvoiceId))
            throw new InvalidOperationException(
                "Cannot remove this allocation — the delivery has already been invoiced.");

        var blockedStatuses = new[] { "Delivered", "MarkedAtHub" };
        if (blockedStatuses.Contains(deliveryOrder.Status))
            throw new InvalidOperationException(
                $"Cannot remove this allocation — the delivery order has status '{deliveryOrder.Status}'.");

        // Reverse QtyBookedOutForDelivery for each species in the allocation
        var reversed = new List<(string speciesId, int qty)>();
        try
        {
            foreach (var line in allocation.Lines)
            {
                ct.ThrowIfCancellationRequested();
                var species = await _speciesRepo.GetAsync(line.SpeciesId, ct);
                if (species != null)
                {
                    species.QtyBookedOutForDelivery = Math.Max(0, species.QtyBookedOutForDelivery - line.Qty);
                    await _speciesRepo.UpdateAsync(species, ct);
                    reversed.Add((line.SpeciesId, line.Qty));
                }
            }

            // Delete the delivery order — it was created solely for this allocation
            await _deliveryRepo.DeleteAsync(deliveryOrderId, ct);

            // Remove the allocation record from the collection request
            cr.DeliveryAllocations.Remove(allocation);
            cr.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(cr, ct);
        }
        catch
        {
            // Compensating rollback — re-book the species quantities
            foreach (var (speciesId, qty) in reversed)
            {
                try
                {
                    var s = await _speciesRepo.GetAsync(speciesId, ct);
                    if (s != null)
                    {
                        s.QtyBookedOutForDelivery += qty;
                        await _speciesRepo.UpdateAsync(s, ct);
                    }
                }
                catch { /* swallow rollback errors */ }
            }
            throw;
        }

        return await MapToResponseAsync(cr, ct);
    }

    public async Task<List<CollectionShortfallReportItem>> GetShortfallReportAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var all = await _repo.ListAsync(null, null, null, ct);

        var shortfallItems = all
            .Where(cr => cr.ShortfallFlagged)
            .Where(cr => from == null || cr.CreatedAt >= from.Value)
            .Where(cr => to == null || cr.CreatedAt <= to.Value.AddDays(1))
            .OrderByDescending(cr => cr.CreatedAt)
            .ToList();

        return shortfallItems.Select(cr => new CollectionShortfallReportItem
        {
            CollectionRequestId = cr.CollectionRequestId,
            SupplierName = cr.SupplierName,
            AssignedDriverName = cr.AssignedDriverName,
            CollectionDate = cr.CollectionDate,
            CreatedAt = cr.CreatedAt,
            Status = cr.Status,
            ShortfallLines = cr.Lines
                .Where(l => l.LoadedQty < l.OrderedQty)
                .Select(l => new CollectionShortfallLine
                {
                    SpeciesId = l.SpeciesId,
                    SpeciesName = l.SpeciesName,
                    OrderedQty = l.OrderedQty,
                    LoadedQty = l.LoadedQty,
                    ShortfallQty = l.OrderedQty - l.LoadedQty,
                    LoadingNotes = l.LoadingNotes
                }).ToList()
        }).ToList();
    }

    private async Task<CollectionRequestResponse> MapToResponseAsync(CollectionRequest cr, CancellationToken ct)
    {
        var response = new CollectionRequestResponse
        {
            CollectionRequestId = cr.CollectionRequestId,
            ProcurementOrderId  = cr.ProcurementOrderId,
            SupplierId          = cr.SupplierId,
            SupplierName        = cr.SupplierName,
            AssignedDriverId    = cr.AssignedDriverId,
            AssignedDriverName  = cr.AssignedDriverName,
            HubId               = cr.HubId,
            Status              = cr.Status,
            Notes               = cr.Notes,
            CollectionDate      = cr.CollectionDate,
            InvoiceS3Key        = cr.InvoiceS3Key,
            DeliveryNoteS3Key   = cr.DeliveryNoteS3Key,
            ShortfallFlagged    = cr.ShortfallFlagged,
            CreatedAt           = cr.CreatedAt,
            UpdatedAt           = cr.UpdatedAt,
            Lines = cr.Lines.Select(l => new CollectionRequestLineResponse
            {
                SpeciesId        = l.SpeciesId,
                SpeciesName      = l.SpeciesName,
                OrderedQty       = l.OrderedQty,
                LoadedQty        = l.LoadedQty,
                LoadingNotes     = l.LoadingNotes,
                ReceivedQty      = l.ReceivedQty,
                DiscrepancyNotes = l.DiscrepancyNotes
            }).ToList(),
            RoadsideSales = cr.RoadsideSales.Select(r => new RoadsaleLineResponse
            {
                SpeciesId   = r.SpeciesId,
                SpeciesName = r.SpeciesName,
                Qty         = r.Qty,
                UnitPrice   = r.UnitPrice,
                PaymentType = r.PaymentType,
            }).ToList()
        };

        // Enrich delivery allocations with actual delivery + invoice data
        var enrichedAllocations = new List<CollectionDeliveryAllocationResponse>();
        foreach (var a in cr.DeliveryAllocations)
        {
            // Hub-direct allocations have no delivery order or invoice — enrich with acceptance status
            if (a.DeliveryOrderId == "HUB")
            {
                enrichedAllocations.Add(new CollectionDeliveryAllocationResponse
                {
                    DeliveryOrderId      = "HUB",
                    ClientId             = "HUB",
                    ClientName           = a.ClientName,
                    DeliveryStatus       = "HubDirect",
                    PaymentType          = "",
                    HubAcceptanceStatus  = a.HubAcceptanceStatus ?? "",
                    Lines = a.Lines.Select(l => new CollectionAllocationLineResponse
                    {
                        SpeciesId    = l.SpeciesId,
                        SpeciesName  = l.SpeciesName,
                        Qty          = l.Qty,
                        UnitPrice    = 0m,
                        DeliveredQty = 0,
                        AcceptedQty  = l.AcceptedQty,
                    }).ToList()
                });
                continue;
            }

            Domain.Entities.DeliveryOrder? doOrder = null;
            string paymentType = "";
            try
            {
                doOrder = await _deliveryRepo.GetAsync(a.DeliveryOrderId, ct);

                if (doOrder != null && !string.IsNullOrEmpty(doOrder.InvoiceId))
                {
                    var invoice = await _invoiceRepo.GetAsync(doOrder.InvoiceId, ct);
                    paymentType = invoice?.PaymentType ?? "";
                }
            }
            catch { /* non-fatal — degrade gracefully if delivery order is unavailable */ }

            enrichedAllocations.Add(new CollectionDeliveryAllocationResponse
            {
                DeliveryOrderId = a.DeliveryOrderId,
                ClientId        = a.ClientId,
                ClientName      = a.ClientName,
                DeliveryStatus  = doOrder?.Status ?? "",
                PaymentType     = paymentType,
                Lines = a.Lines.Select(l => new CollectionAllocationLineResponse
                {
                    SpeciesId    = l.SpeciesId,
                    SpeciesName  = l.SpeciesName,
                    Qty          = l.Qty,
                    UnitPrice    = l.UnitPrice,
                    DeliveredQty = doOrder?.Lines
                        .FirstOrDefault(dl => dl.SpeciesId == l.SpeciesId)
                        ?.DeliveredQty ?? 0,
                }).ToList()
            });
        }

        response.DeliveryAllocations = enrichedAllocations;
        return response;
    }
}
