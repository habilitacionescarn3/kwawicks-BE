using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class ProcurementOrderService : IProcurementOrderService
{
    private readonly IProcurementOrderRepository _repo;
    private readonly ISupplierRepository _supplierRepo;
    private readonly ISpeciesRepository _speciesRepo;
    private readonly IS3Service _s3;

    public ProcurementOrderService(
        IProcurementOrderRepository repo,
        ISupplierRepository supplierRepo,
        ISpeciesRepository speciesRepo,
        IS3Service s3)
    {
        _repo = repo;
        _supplierRepo = supplierRepo;
        _speciesRepo = speciesRepo;
        _s3 = s3;
    }

    public async Task<ProcurementOrderResponse> CreateAsync(CreateProcurementOrderRequest request, string createdByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SupplierId)) throw new ArgumentException("SupplierId is required.");
        if (request.Lines == null || request.Lines.Count == 0) throw new ArgumentException("At least one line is required.");

        string supplierName;
        if (string.Equals(request.SupplierId, "HUB", StringComparison.OrdinalIgnoreCase))
        {
            supplierName = "Hub";
        }
        else
        {
            var supplier = await _supplierRepo.GetAsync(request.SupplierId, ct)
                ?? throw new InvalidOperationException($"Supplier not found: {request.SupplierId}");
            supplierName = supplier.Name;
        }

        var order = new ProcurementOrder
        {
            SupplierId = request.SupplierId,
            SupplierName = supplierName,
            SupplierOrderReference = request.SupplierOrderReference ?? "",
            Notes = request.Notes ?? "",
            Status = "Draft",
            CreatedByUserId = createdByUserId,
            Lines = new List<ProcurementOrderLine>()
        };

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.SpeciesId)) throw new ArgumentException("SpeciesId is required on all lines.");
            if (line.OrderedQty <= 0) throw new ArgumentException("OrderedQty must be greater than 0.");

            var species = await _speciesRepo.GetAsync(line.SpeciesId, ct)
                ?? throw new InvalidOperationException($"Species not found: {line.SpeciesId}");

            order.Lines.Add(new ProcurementOrderLine
            {
                SpeciesId = line.SpeciesId,
                SpeciesName = species.Name,
                OrderedQty = line.OrderedQty,
                UnitCost = line.UnitCost ?? species.UnitCost
            });
        }

        await _repo.CreateAsync(order, ct);
        return MapToResponse(order);
    }

    public async Task<ProcurementOrderResponse?> GetAsync(string id, CancellationToken ct = default)
    {
        var order = await _repo.GetAsync(id, ct);
        return order == null ? null : MapToResponse(order);
    }

    public async Task<List<ProcurementOrderResponse>> ListAsync(string? status = null, string? supplierId = null, CancellationToken ct = default)
    {
        var orders = await _repo.ListAsync(status, supplierId, ct);
        return orders.Select(MapToResponse).ToList();
    }

    public async Task<ProcurementOrderResponse> UpdateDraftAsync(string id, CreateProcurementOrderRequest request, CancellationToken ct = default)
    {
        var order = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Procurement order not found: {id}");
        if (order.Status != "Draft")
            throw new InvalidOperationException("Only Draft orders can be edited.");

        if (string.IsNullOrWhiteSpace(request.SupplierId)) throw new ArgumentException("SupplierId is required.");
        if (request.Lines == null || request.Lines.Count == 0) throw new ArgumentException("At least one line is required.");

        string supplierName;
        if (string.Equals(request.SupplierId, "HUB", StringComparison.OrdinalIgnoreCase))
        {
            supplierName = "Hub";
        }
        else
        {
            var supplier = await _supplierRepo.GetAsync(request.SupplierId, ct)
                ?? throw new InvalidOperationException($"Supplier not found: {request.SupplierId}");
            supplierName = supplier.Name;
        }

        order.SupplierId = request.SupplierId;
        order.SupplierName = supplierName;
        order.SupplierOrderReference = request.SupplierOrderReference ?? "";
        order.Notes = request.Notes ?? "";
        order.Lines = new List<ProcurementOrderLine>();

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.SpeciesId)) throw new ArgumentException("SpeciesId is required on all lines.");
            if (line.OrderedQty <= 0) throw new ArgumentException("OrderedQty must be greater than 0.");

            var species = await _speciesRepo.GetAsync(line.SpeciesId, ct)
                ?? throw new InvalidOperationException($"Species not found: {line.SpeciesId}");

            order.Lines.Add(new ProcurementOrderLine
            {
                SpeciesId = line.SpeciesId,
                SpeciesName = species.Name,
                OrderedQty = line.OrderedQty,
                UnitCost = line.UnitCost ?? species.UnitCost
            });
        }

        await _repo.UpdateAsync(order, ct);
        return MapToResponse(order);
    }

    public async Task DeleteDraftAsync(string id, CancellationToken ct = default)
    {
        var order = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Procurement order not found: {id}");
        if (order.Status != "Draft")
            throw new InvalidOperationException("Only Draft orders can be deleted.");
        await _repo.DeleteAsync(id, ct);
    }

    public async Task SubmitAsync(string id, CancellationToken ct = default)
    {
        var order = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Procurement order not found: {id}");
        if (order.Status != "Draft") throw new InvalidOperationException("Only Draft orders can be submitted.");
        order.Status = "Submitted";
        await _repo.UpdateAsync(order, ct);
    }

    public async Task CompleteAsync(string id, CancellationToken ct = default)
    {
        var order = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Procurement order not found: {id}");
        if (order.Status != "DeliveredToHub") throw new InvalidOperationException("Order must be in DeliveredToHub status to complete.");
        order.Status = "Completed";
        await _repo.UpdateAsync(order, ct);
    }

    public async Task<ProcurementInvoiceUploadUrlResponse> GetInvoiceUploadUrlAsync(string id, CancellationToken ct = default)
    {
        var order = await _repo.GetAsync(id, ct)
            ?? throw new InvalidOperationException($"Procurement order not found: {id}");

        var key = $"procurement/invoices/{id}/{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var url = await _s3.GeneratePresignedUploadUrlAsync(key, "application/pdf", ct);

        order.InvoiceS3Key = key;
        await _repo.UpdateAsync(order, ct);

        return new ProcurementInvoiceUploadUrlResponse { UploadUrl = url, S3Key = key };
    }

    private static ProcurementOrderResponse MapToResponse(ProcurementOrder o) => new()
    {
        ProcurementOrderId = o.ProcurementOrderId,
        SupplierId = o.SupplierId,
        SupplierName = o.SupplierName,
        SupplierOrderReference = o.SupplierOrderReference,
        Status = o.Status,
        Notes = o.Notes,
        InvoiceS3Key = o.InvoiceS3Key,
        CreatedByUserId = o.CreatedByUserId,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        Lines = o.Lines.Select(l => new ProcurementOrderLineResponse
        {
            SpeciesId = l.SpeciesId,
            SpeciesName = l.SpeciesName,
            OrderedQty = l.OrderedQty,
            UnitCost = l.UnitCost
        }).ToList()
    };
}
