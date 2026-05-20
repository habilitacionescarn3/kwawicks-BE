using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;

namespace KwaWicks.Api.Controllers;

[ApiController]
[Route("api/collection-requests")]
public class CollectionRequestsController : ControllerBase
{
    private readonly ICollectionRequestService _service;
    public CollectionRequestsController(ICollectionRequestService service) => _service = service;

    [HttpGet]
    [Authorize(Policy = "OperationalAccess")]
    public async Task<IActionResult> List([FromQuery] string? driverId, [FromQuery] string? status, [FromQuery] string? procurementOrderId, CancellationToken ct)
    {
        var result = await _service.ListAsync(driverId, status, procurementOrderId, ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "OperationalAccess")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "CollectionManagement")]
    public async Task<IActionResult> Create([FromBody] CreateCollectionRequestRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.CreateAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { id = result.CollectionRequestId }, result);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPut("{id}/load")]
    [Authorize(Policy = "DriverOnly")]
    public async Task<IActionResult> DriverLoad(string id, [FromBody] DriverLoadingUpdateRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.DriverLoadAsync(id, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPut("{id}/dispatch")]
    [Authorize(Policy = "DriverOnly")]
    public async Task<IActionResult> Dispatch(string id, CancellationToken ct)
    {
        try
        {
            var result = await _service.DispatchAsync(id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPut("{id}/arrive")]
    [Authorize(Policy = "DriverOnly")]
    public async Task<IActionResult> Arrive(string id, CancellationToken ct)
    {
        try
        {
            var result = await _service.ArriveAsync(id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPut("{id}/hub-confirm")]
    [Authorize(Policy = "CollectionManagement")]
    public async Task<IActionResult> HubConfirm(string id, [FromBody] HubConfirmReceiptRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.HubConfirmAsync(id, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPut("{id}/finance-acknowledge")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> FinanceAcknowledge(string id, [FromBody] FinanceAcknowledgeRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.FinanceAcknowledgeAsync(id, request.InvoiceS3Key, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpGet("{id}/delivery-note-view-url")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> GetDeliveryNoteViewUrl(string id, CancellationToken ct)
    {
        try
        {
            var viewUrl = await _service.GetDeliveryNoteViewUrlAsync(id, ct);
            return Ok(new { viewUrl });
        }
        catch (InvalidOperationException ex) { return NotFound(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpGet("{id}/delivery-note-upload-url")]
    [Authorize(Policy = "DriverOnly")]
    public async Task<IActionResult> GetDeliveryNoteUploadUrl(string id, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetDeliveryNoteUploadUrlAsync(id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return NotFound(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpGet("{id}/invoice-upload-url")]
    [Authorize(Policy = "FinancialAccess")]
    public async Task<IActionResult> GetInvoiceUploadUrl(string id, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetInvoiceUploadUrlAsync(id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return NotFound(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpGet("shortfall-report")]
    [Authorize(Policy = "CollectionManagement")]
    public async Task<IActionResult> ShortfallReport([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await _service.GetShortfallReportAsync(from, to, ct);
        return Ok(result);
    }

    [HttpPost("{id}/allocations")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AddAllocation(string id, [FromBody] AddDeliveryAllocationRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.AddDeliveryAllocationAsync(id, request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    // PUT /api/collection-requests/{id}/roadside-sales  (replaces entire set)
    [HttpPut("{id}/roadside-sales")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SetRoadsideSales(string id, [FromBody] SetRoadsideSalesRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.SetRoadsideSalesAsync(id, request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // POST /api/collection-requests/{id}/allocations/{deliveryOrderId}/confirm-delivery
    // Admin records actual delivered qty + payment type, creating the client invoice on behalf of the driver.
    [HttpPost("{id}/allocations/{deliveryOrderId}/confirm-delivery")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(CollectionRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmDelivery(
        string id,
        string deliveryOrderId,
        [FromBody] AdminConfirmDeliveryRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.ConfirmDeliveryAsync(id, deliveryOrderId, request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // PUT /api/collection-requests/{id}/allocations/HUB/accept
    // Hub staff physically verifies and accepts the HUB-allocated stock, incrementing QtyOnHandHub.
    [HttpPut("{id}/allocations/HUB/accept")]
    [Authorize(Policy = "CollectionManagement")]
    [ProducesResponseType(typeof(CollectionRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HubAcceptAllocation(
        string id,
        [FromBody] HubAcceptAllocationRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.HubAcceptAllocationAsync(id, request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // DELETE /api/collection-requests/{id}/allocations/{deliveryOrderId}
    // Removes a mistakenly added allocation, reverses stock bookings, deletes the delivery order.
    [HttpDelete("{id}/allocations/{deliveryOrderId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(CollectionRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAllocation(string id, string deliveryOrderId, CancellationToken ct)
    {
        try
        {
            var result = await _service.RemoveDeliveryAllocationAsync(id, deliveryOrderId, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // PUT /api/collection-requests/{id}/allocations/{deliveryOrderId}
    [HttpPut("{id}/allocations/{deliveryOrderId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(CollectionRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditAllocation(
        string id,
        string deliveryOrderId,
        [FromBody] EditAllocationRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.EditDeliveryAllocationAsync(id, deliveryOrderId, request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
