using KwaWicks.Application.DTOs;

namespace KwaWicks.Application.Interfaces;

public interface IVehicleTrackingService
{
    Task RecordLocationAsync(string driverId, string driverName, RecordLocationRequest request, CancellationToken ct = default);
    Task<List<LiveVehicleResponse>> GetLiveVehiclesAsync(CancellationToken ct = default);
    Task<DriverRouteResponse> GetDriverRouteAsync(string driverId, int hours, CancellationToken ct = default);
}
