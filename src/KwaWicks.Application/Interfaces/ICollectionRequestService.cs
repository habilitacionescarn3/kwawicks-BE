using KwaWicks.Application.DTOs;

namespace KwaWicks.Application.Interfaces;

public interface ICollectionRequestService
{
    Task<CollectionRequestResponse> CreateAsync(CreateCollectionRequestRequest request, CancellationToken ct = default);
    Task<CollectionRequestResponse?> GetAsync(string id, CancellationToken ct = default);
    Task<List<CollectionRequestResponse>> ListAsync(string? driverId = null, string? status = null, string? procurementOrderId = null, CancellationToken ct = default);
    Task<CollectionRequestResponse> DriverLoadAsync(string id, DriverLoadingUpdateRequest request, CancellationToken ct = default);
    Task<CollectionRequestResponse> DispatchAsync(string id, CancellationToken ct = default);
    Task<CollectionRequestResponse> ArriveAsync(string id, CancellationToken ct = default);
    Task<CollectionRequestResponse> HubConfirmAsync(string id, HubConfirmReceiptRequest request, CancellationToken ct = default);
    Task<CollectionRequestResponse> FinanceAcknowledgeAsync(string id, string invoiceS3Key, CancellationToken ct = default);
    Task<CollectionInvoiceUploadUrlResponse> GetInvoiceUploadUrlAsync(string id, CancellationToken ct = default);
    Task<CollectionInvoiceUploadUrlResponse> GetDeliveryNoteUploadUrlAsync(string id, CancellationToken ct = default);
    Task<string> GetDeliveryNoteViewUrlAsync(string id, CancellationToken ct = default);
    Task<CollectionRequestResponse> AddDeliveryAllocationAsync(string id, AddDeliveryAllocationRequest request, CancellationToken ct = default);
    Task<CollectionRequestResponse> EditDeliveryAllocationAsync(string id, string deliveryOrderId, EditAllocationRequest request, CancellationToken ct = default);
    Task<CollectionRequestResponse> SetRoadsideSalesAsync(string id, SetRoadsideSalesRequest request, CancellationToken ct = default);
    /// <summary>Admin records the actual qty delivered to a client + payment type, creating the invoice on their behalf.</summary>
    Task<CollectionRequestResponse> ConfirmDeliveryAsync(string crId, string deliveryOrderId, AdminConfirmDeliveryRequest request, CancellationToken ct = default);
    /// <summary>Hub staff physically accepts HUB-allocated stock into inventory, updating QtyOnHandHub.</summary>
    Task<CollectionRequestResponse> HubAcceptAllocationAsync(string crId, HubAcceptAllocationRequest request, CancellationToken ct = default);
    /// <summary>Removes a delivery allocation that was added by mistake, reversing all stock bookings and deleting the delivery order.</summary>
    Task<CollectionRequestResponse> RemoveDeliveryAllocationAsync(string crId, string deliveryOrderId, CancellationToken ct = default);
    Task<List<CollectionShortfallReportItem>> GetShortfallReportAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}
