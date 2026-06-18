using KwaWicks.Application.DTOs;
using KwaWicks.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/sites")]
[Produces("application/json")]
[Authorize(Policy = "HubStaffOnly")]
public class SitesController : ControllerBase
{
    private readonly SiteService _service;
    public SitesController(SiteService service) => _service = service;

    // GET /api/sites
    [HttpGet]
    [ProducesResponseType(typeof(List<SiteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _service.ListAsync(ct));

    // POST /api/sites
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSiteRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await _service.CreateAsync(req, ct);
            return Ok(dto);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
