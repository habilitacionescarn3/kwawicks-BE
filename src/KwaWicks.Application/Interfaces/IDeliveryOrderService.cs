using KwaWicks.Application.DTOs;

namespace KwaWicks.Application.Interfaces;

public interface IDeliveryOrderService
{
    Task<string> CreateAsync(CreateDeliveryOrderRequest request, CancellationToken ct);
    Task<DeliveryOrderResponse?> GetAsync(string deliveryOrderId, CancellationToken ct);
    Task<List<DeliveryOrderResponse>> ListAsync(string? driverId, string? hubId, string? status, CancellationToken ct);
    Task UpdateStatusAsync(string deliveryOrderId, string status, CancellationToken ct);
    Task<List<DriverStockItem>> GetDriverAvailableStockAsync(string driverId, CancellationToken ct);
    Task SubmitReturnAsync(string deliveryOrderId, SubmitReturnRequest request, CancellationToken ct);
    Task CheckInReturnAsync(string deliveryOrderId, CancellationToken ct);
    Task EditLinesAsync(string deliveryOrderId, EditDeliveryOrderLinesRequest request, CancellationToken ct);
    Task DeleteAsync(string deliveryOrderId, CancellationToken ct);
}
