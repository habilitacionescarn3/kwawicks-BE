using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Interfaces;

public interface IVehicleTrackingRepository
{
    /// <summary>Upsert the driver's latest position (one item per driver).</summary>
    Task UpsertLiveAsync(VehicleLocation loc, CancellationToken ct = default);

    /// <summary>Append a history point (TTL 7 days).</summary>
    Task AppendHistoryAsync(VehicleLocation loc, CancellationToken ct = default);

    /// <summary>Return the latest position for every active driver.</summary>
    Task<List<VehicleLocation>> GetAllLiveAsync(CancellationToken ct = default);

    /// <summary>Return ordered history points for one driver within [from, to].</summary>
    Task<List<VehicleLocation>> GetHistoryAsync(string driverId, DateTime from, DateTime to, CancellationToken ct = default);
}
