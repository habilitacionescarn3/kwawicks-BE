using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Interfaces;

public interface IDeliveryOrderRepository
{
    Task<DeliveryOrder> CreateAsync(DeliveryOrder deliveryOrder, CancellationToken ct);
    Task<DeliveryOrder?> GetAsync(string deliveryOrderId, CancellationToken ct);
    Task<DeliveryOrder> UpdateAsync(DeliveryOrder deliveryOrder, CancellationToken ct);
    Task<List<DeliveryOrder>> ListAsync(string? driverId, string? hubId, string? status, CancellationToken ct);
    Task DeleteAsync(string deliveryOrderId, CancellationToken ct);
}
