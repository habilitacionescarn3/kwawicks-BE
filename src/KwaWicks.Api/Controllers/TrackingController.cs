using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/tracking")]
[Produces("application/json")]
[Authorize]
public class TrackingController : ControllerBase
{
    private readonly IVehicleTrackingService _service;

    public TrackingController(IVehicleTrackingService service)
    {
        _service = service;
    }

    // POST /api/tracking/location
    // Driver tablets call this every 5 minutes to record their GPS position.
    [HttpPost("location")]
    [Authorize(Policy = "DriverOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordLocation([FromBody] RecordLocationRequest request, CancellationToken ct)
    {
        var driverId   = User.FindFirstValue("username")
                      ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? "";
        var driverName = User.FindFirstValue("name")
                      ?? User.FindFirstValue("given_name")
                      ?? driverId;

        if (string.IsNullOrWhiteSpace(driverId)) return Unauthorized();

        try
        {
            await _service.RecordLocationAsync(driverId, driverName, request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /api/tracking/live
    // Admin map: latest position for every driver that has pinged in.
    [HttpGet("live")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(List<LiveVehicleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLive(CancellationToken ct)
    {
        var vehicles = await _service.GetLiveVehiclesAsync(ct);
        return Ok(vehicles);
    }

    // GET /api/tracking/history/{driverId}?hours=24
    // Admin map: breadcrumb trail for a specific driver (up to 48 hours).
    [HttpGet("history/{driverId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(DriverRouteResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(string driverId, [FromQuery] int hours = 24, CancellationToken ct = default)
    {
        var route = await _service.GetDriverRouteAsync(driverId, hours, ct);
        return Ok(route);
    }
}
