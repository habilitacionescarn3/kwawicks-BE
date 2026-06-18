using KwaWicks.Application.DTOs;
using KwaWicks.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/dip-tanks")]
[Produces("application/json")]
[Authorize(Policy = "HubStaffOnly")]
public class DipTanksController : ControllerBase
{
    private readonly DipTankService _service;
    public DipTanksController(DipTankService service) => _service = service;

    private string CallerName =>
        User.Identity?.Name ?? User.FindFirst("cognito:username")?.Value ?? "unknown";

    // GET /api/dip-tanks
    [HttpGet]
    [ProducesResponseType(typeof(List<DipTankDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTanks(CancellationToken ct) =>
        Ok(await _service.ListTanksAsync(ct));

    // POST /api/dip-tanks
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(DipTankDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTank([FromBody] CreateDipTankRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.CreateTankAsync(req, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // GET /api/dip-tanks/summary
    [HttpGet("summary")]
    [ProducesResponseType(typeof(List<TankSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        Ok(await _service.GetSummaryAsync(ct));

    // GET /api/dip-tanks/readings
    [HttpGet("readings")]
    [ProducesResponseType(typeof(List<DipReadingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListReadings(CancellationToken ct) =>
        Ok(await _service.ListReadingsAsync(ct));

    // POST /api/dip-tanks/readings
    [HttpPost("readings")]
    [ProducesResponseType(typeof(DipReadingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateReading([FromBody] CreateDipReadingRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.CreateReadingAsync(req, CallerName, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }
}
