using KwaWicks.Application.DTOs;
using KwaWicks.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/fuel")]
[Produces("application/json")]
[Authorize(Policy = "HubStaffOnly")]
public class FuelIssuesController : ControllerBase
{
    private readonly FuelService _service;
    public FuelIssuesController(FuelService service) => _service = service;

    private string CallerName =>
        User.Identity?.Name ?? User.FindFirst("cognito:username")?.Value ?? "unknown";

    // GET /api/fuel
    [HttpGet]
    [ProducesResponseType(typeof(List<FuelIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _service.ListAsync(ct));

    // POST /api/fuel
    [HttpPost]
    [ProducesResponseType(typeof(FuelIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateFuelIssueRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.CreateAsync(req, CallerName, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // GET /api/fuel/{id}/slip-upload-url?contentType=image/jpeg
    [HttpGet("{id}/slip-upload-url")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSlipUploadUrl(
        string id, [FromQuery] string contentType, CancellationToken ct)
    {
        var ct2 = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;
        var (url, key) = await _service.GetSlipUploadUrlAsync(id, ct2, ct);
        return Ok(new { uploadUrl = url, s3Key = key });
    }

    // PUT /api/fuel/{id}/slip
    [HttpPut("{id}/slip")]
    [ProducesResponseType(typeof(FuelIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmSlip(string id, [FromBody] ConfirmFuelSlipRequest req, CancellationToken ct)
    {
        var dto = await _service.ConfirmSlipUploadedAsync(id, req.S3Key, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // GET /api/fuel/report?vehicleId=&from=2025-01-01&to=2025-12-31
    [HttpGet("report")]
    [ProducesResponseType(typeof(List<VehicleFuelReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(
        [FromQuery] string? vehicleId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct) =>
        Ok(await _service.GetReportAsync(vehicleId, from, to, ct));
}
