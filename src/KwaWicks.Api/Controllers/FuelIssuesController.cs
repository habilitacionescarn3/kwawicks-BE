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
}
