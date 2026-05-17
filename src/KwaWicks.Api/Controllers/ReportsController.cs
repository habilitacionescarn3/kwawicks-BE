using KwaWicks.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    // ── Admin ────────────────────────────────────────────────────────────────

    [HttpGet("revenue")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> Revenue(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetRevenueSummaryAsync(from, to, ct);
        return Ok(result);
    }

    [HttpGet("outstanding-payments")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> OutstandingPayments(CancellationToken ct)
    {
        var result = await _reports.GetOutstandingPaymentsAsync(ct);
        return Ok(result);
    }

    [HttpGet("driver-performance")]
    [Authorize(Policy = "OperationalAccess")]
    public async Task<IActionResult> DriverPerformance(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetDriverPerformanceAsync(from, to, ct);
        return Ok(result);
    }

    [HttpGet("returns")]
    [Authorize(Policy = "OperationalAccess")]
    public async Task<IActionResult> Returns(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetReturnsSummaryAsync(from, to, ct);
        return Ok(result);
    }

    [HttpGet("invoices")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> Invoices(
        [FromQuery] string? customerId,
        [FromQuery] string? paymentStatus,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetInvoicesAsync(customerId, paymentStatus, from, to, ct);
        return Ok(result);
    }

    [HttpGet("delivery-status")]
    [Authorize(Policy = "OperationalAccess")]
    public async Task<IActionResult> DeliveryStatus(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetDeliveryStatusSummaryAsync(from, to, ct);
        return Ok(result);
    }

    [HttpGet("statement")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> CustomerStatement(
        [FromQuery] string customerId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        try
        {
            var result = await _reports.GetCustomerStatementAsync(customerId, from, to, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("revenue-by-species")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> SpeciesRevenue(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetSpeciesRevenueAsync(from, to, ct);
        return Ok(result);
    }

    [HttpGet("statements")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> AllCustomerStatements(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetAllCustomerStatementsAsync(from, to, ct);
        return Ok(result);
    }

    [HttpGet("sales")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SalesReport(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _reports.GetSalesReportAsync(from, to, ct);
        return Ok(result);
    }

    // ── Driver ───────────────────────────────────────────────────────────────

    [HttpGet("my-deliveries")]
    [Authorize(Policy = "DriverOnly")]
    public async Task<IActionResult> MyDeliveries(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var driverId = User.FindFirstValue("username")
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? "";

        var result = await _reports.GetMyDeliveriesAsync(driverId, from, to, ct);
        return Ok(result);
    }
}
