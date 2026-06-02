using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/delivery-orders")]
[Produces("application/json")]
[Authorize]
public class DeliveryOrdersController : ControllerBase
{
    private readonly IDeliveryOrderService _service;
    private readonly IInvoiceService _invoiceService;
    private readonly IClientService _clientService;
    private readonly IInvoiceNotificationService _notification;

    public DeliveryOrdersController(
        IDeliveryOrderService service,
        IInvoiceService invoiceService,
        IClientService clientService,
        IInvoiceNotificationService notification)
    {
        _service = service;
        _invoiceService = invoiceService;
        _clientService = clientService;
        _notification = notification;
    }

    // POST /api/delivery-orders
    [HttpPost]
    [Authorize(Policy = "HubStaffOnly")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateDeliveryOrderRequest request, CancellationToken ct)
    {
        try
        {
            // Only Owner and Finance may override the unit price on lines
            bool hasPriceOverride = request.Lines?.Any(l => l.UnitPrice.HasValue) == true;
            if (hasPriceOverride)
            {
                var roles = User.FindAll("cognito:groups").Select(c => c.Value).ToHashSet();
                if (!roles.Contains("Owner") && !roles.Contains("Finance"))
                    return Forbid();
            }

            var id = await _service.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { deliveryOrderId = id }, new { deliveryOrderId = id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /api/delivery-orders/driver-stock
    [HttpGet("driver-stock")]
    [Authorize(Policy = "DriverOnly")]
    [ProducesResponseType(typeof(List<DriverStockItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<DriverStockItem>>> GetDriverStock(CancellationToken ct)
    {
        var driverId = User.FindFirstValue("username")
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? "";

        var stock = await _service.GetDriverAvailableStockAsync(driverId, ct);
        return Ok(stock);
    }

    // GET /api/delivery-orders?driverId=&hubId=&status=
    [HttpGet]
    [ProducesResponseType(typeof(List<DeliveryOrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<DeliveryOrderResponse>>> List(
        [FromQuery] string? driverId,
        [FromQuery] string? hubId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var orders = await _service.ListAsync(driverId, hubId, status, ct);
        return Ok(orders);
    }

    // GET /api/delivery-orders/{deliveryOrderId}
    [HttpGet("{deliveryOrderId}")]
    [ProducesResponseType(typeof(DeliveryOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DeliveryOrderResponse>> GetById(string deliveryOrderId, CancellationToken ct)
    {
        var order = await _service.GetAsync(deliveryOrderId, ct);
        if (order is null) return NotFound();
        return Ok(order);
    }

    // PUT /api/delivery-orders/{deliveryOrderId}/status
    [HttpPut("{deliveryOrderId}/status")]
    [Authorize(Policy = "DriverOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateStatus(
        string deliveryOrderId,
        [FromBody] UpdateDeliveryStatusRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.UpdateStatusAsync(deliveryOrderId, request.Status, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // POST /api/delivery-orders/{deliveryOrderId}/invoice
    [HttpPost("{deliveryOrderId}/invoice")]
    [Authorize(Policy = "DriverOnly")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateInvoiceFromDelivery(
        string deliveryOrderId,
        [FromBody] CreateInvoiceFromDeliveryRequest request,
        CancellationToken ct)
    {
        try
        {
            var invoiceId = await _invoiceService.CreateFromDeliveryAsync(deliveryOrderId, request, ct);

            // Resolve client phone from delivery order → client record
            var order = await _service.GetAsync(deliveryOrderId, ct);
            string? effectivePhone = null;
            if (order is not null)
                effectivePhone = await ResolvePhoneAsync(order.CustomerId, request.ClientPhone, ct);

            bool whatsAppSent = false;
            string? whatsAppError = null;
            if (!string.IsNullOrWhiteSpace(effectivePhone))
            {
                (whatsAppSent, whatsAppError) = await _notification.TrySendInvoiceWhatsAppAsync(invoiceId, effectivePhone, ct);
            }

            return CreatedAtAction(
                nameof(InvoicesController.GetById),
                "Invoices",
                new { invoiceId },
                new { invoiceId, whatsAppSent, whatsAppError });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // PUT /api/delivery-orders/{deliveryOrderId}/lines  (Admin: edit qty/price on Open orders)
    [HttpPut("{deliveryOrderId}/lines")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EditLines(
        string deliveryOrderId,
        [FromBody] EditDeliveryOrderLinesRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.EditLinesAsync(deliveryOrderId, request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST /api/delivery-orders/{deliveryOrderId}/submit-return
    [HttpPost("{deliveryOrderId}/submit-return")]
    [Authorize(Policy = "DriverOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitReturn(
        string deliveryOrderId,
        [FromBody] SubmitReturnRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.SubmitReturnAsync(deliveryOrderId, request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST /api/delivery-orders/{deliveryOrderId}/check-in
    [HttpPost("{deliveryOrderId}/check-in")]
    [Authorize(Policy = "HubStaffOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckInReturn(string deliveryOrderId, CancellationToken ct)
    {
        try
        {
            await _service.CheckInReturnAsync(deliveryOrderId, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // DELETE /api/delivery-orders/{deliveryOrderId}  (Admin only — Open orders only, restores stock)
    [HttpDelete("{deliveryOrderId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string deliveryOrderId, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(deliveryOrderId, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private async Task<string?> ResolvePhoneAsync(string clientId, string? phoneOverride, CancellationToken ct)
    {
        var client = await _clientService.GetByIdAsync(clientId, ct);
        if (client is null) return null;

        if (!string.IsNullOrWhiteSpace(phoneOverride))
        {
            if (string.IsNullOrWhiteSpace(client.ClientPhone))
                await _clientService.PatchPhoneAsync(clientId, phoneOverride, ct);
            return phoneOverride;
        }

        // ClientPhone is the dedicated WhatsApp field; fall back to ClientContactDetails
        return !string.IsNullOrWhiteSpace(client.ClientPhone)
            ? client.ClientPhone
            : (!string.IsNullOrWhiteSpace(client.ClientContactDetails) ? client.ClientContactDetails : null);
    }
}
