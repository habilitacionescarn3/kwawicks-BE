using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/clients/{clientId}/credit")]
[Authorize(Policy = "FinancialAccess")]
public class ClientCreditController : ControllerBase
{
    private readonly IClientCreditService _service;

    public ClientCreditController(IClientCreditService service) => _service = service;

    // GET /api/clients/{clientId}/credit — full ledger with balance
    [HttpGet]
    public async Task<IActionResult> GetLedger(string clientId, CancellationToken ct)
    {
        var ledger = await _service.GetLedgerAsync(clientId, ct);
        return Ok(ledger);
    }

    // GET /api/clients/{clientId}/credit/balance — balance only (used at checkout)
    [HttpGet("balance")]
    [Authorize(Policy = "HubStaffOnly")]
    public async Task<IActionResult> GetBalance(string clientId, CancellationToken ct)
    {
        var balance = await _service.GetBalanceAsync(clientId, ct);
        return Ok(new { balance });
    }

    // GET /api/clients/{clientId}/credit/proof-upload-url?contentType=image/jpeg
    [HttpGet("proof-upload-url")]
    public async Task<IActionResult> GetProofUploadUrl(
        string clientId,
        [FromQuery] string contentType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return BadRequest(new { error = "contentType is required." });

        var result = await _service.GetProofUploadUrlAsync(clientId, contentType, ct);
        return Ok(result);
    }

    // POST /api/clients/{clientId}/credit/charges — admin manual charge (corrects wrong data)
    [HttpPost("charges")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AddManualCharge(
        string clientId,
        [FromBody] AddManualChargeRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirstValue("username") ??
                         User.FindFirstValue(ClaimTypes.Name) ??
                         User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         "admin";
            var entry = await _service.AddManualChargeAsync(clientId, request.Amount, request.Notes ?? "", userId, ct);
            return Ok(entry);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)         { return StatusCode(500, new { error = ex.Message }); }
    }

    // DELETE /api/clients/{clientId}/credit/entries/{entryId} — admin corrects wrong entries
    [HttpDelete("entries/{entryId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteEntry(string clientId, string entryId, CancellationToken ct)
    {
        try
        {
            await _service.DeleteEntryAsync(clientId, entryId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)                 { return StatusCode(500, new { error = ex.Message }); }
    }

    // POST /api/clients/{clientId}/credit — add a deposit (payment received from client)
    [HttpPost]
    public async Task<IActionResult> AddDeposit(
        string clientId,
        [FromBody] AddCreditDepositRequest request,
        CancellationToken ct)
    {
        try
        {
            // Capture who made the deposit from JWT
            request.CreatedByUserId =
                User.FindFirstValue("username") ??
                User.FindFirstValue(ClaimTypes.Name) ??
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                "unknown";

            var entry = await _service.AddDepositAsync(clientId, request, ct);
            return Ok(entry);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
