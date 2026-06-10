using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class VehicleTrackingService : IVehicleTrackingService
{
    private readonly IVehicleTrackingRepository _repo;

    public VehicleTrackingService(IVehicleTrackingRepository repo)
    {
        _repo = repo;
    }

    public async Task RecordLocationAsync(string driverId, string driverName, RecordLocationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driverId))
            throw new ArgumentException("DriverId is required.", nameof(driverId));

        var loc = new VehicleLocation
        {
            DriverId        = driverId,
            DriverName      = string.IsNullOrWhiteSpace(driverName) ? driverId : driverName,
            Latitude        = request.Latitude,
            Longitude       = request.Longitude,
            Accuracy        = request.Accuracy,
            Speed           = request.Speed,
            RecordedAt      = DateTime.UtcNow,
            DeliveryOrderId = request.DeliveryOrderId
        };

        // Write live position and append history in parallel
        await Task.WhenAll(
            _repo.UpsertLiveAsync(loc, ct),
            _repo.AppendHistoryAsync(loc, ct)
        );
    }

    public async Task<List<LiveVehicleResponse>> GetLiveVehiclesAsync(CancellationToken ct = default)
    {
        var vehicles = await _repo.GetAllLiveAsync(ct);
        var now = DateTime.UtcNow;

        return vehicles
            .Select(v => new LiveVehicleResponse
            {
                DriverId             = v.DriverId,
                DriverName           = v.DriverName,
                Latitude             = v.Latitude,
                Longitude            = v.Longitude,
                Accuracy             = v.Accuracy,
                Speed                = v.Speed,
                LastSeen             = v.RecordedAt,
                DeliveryOrderId      = v.DeliveryOrderId,
                MinutesSinceLastPing = (int)(now - v.RecordedAt).TotalMinutes
            })
            .OrderBy(v => v.DriverName)
            .ToList();
    }

    public async Task<DriverRouteResponse> GetDriverRouteAsync(string driverId, int hours, CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, 1, 48);
        var from = DateTime.UtcNow.AddHours(-hours);
        var to   = DateTime.UtcNow;

        var history = await _repo.GetHistoryAsync(driverId, from, to, ct);

        var driverName = history.LastOrDefault()?.DriverName ?? driverId;

        return new DriverRouteResponse
        {
            DriverId   = driverId,
            DriverName = driverName,
            Points     = history
                .OrderBy(p => p.RecordedAt)
                .Select(p => new LocationHistoryPoint
                {
                    Latitude   = p.Latitude,
                    Longitude  = p.Longitude,
                    RecordedAt = p.RecordedAt
                })
                .ToList()
        };
    }
}
