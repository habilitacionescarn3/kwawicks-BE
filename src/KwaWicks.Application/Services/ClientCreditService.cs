using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class ClientCreditService : IClientCreditService
{
    private readonly IClientCreditRepository _repo;
    private readonly IClientRepository _clientRepo;
    private readonly IS3Service _s3;

    public ClientCreditService(IClientCreditRepository repo, IClientRepository clientRepo, IS3Service s3)
    {
        _repo = repo;
        _clientRepo = clientRepo;
        _s3 = s3;
    }

    public async Task<ClientCreditEntryResponse> AddDepositAsync(
        string clientId, AddCreditDepositRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
            throw new ArgumentException("Deposit amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            throw new ArgumentException("Payment method is required for deposits.");

        var entry = new ClientCreditEntry
        {
            ClientId        = clientId,
            Amount          = request.Amount,
            EntryType       = "Deposit",
            PaymentMethod   = request.PaymentMethod,
            Notes           = request.Notes,
            CreatedByUserId = request.CreatedByUserId,
            Reference       = "",
            ProofS3Key      = request.ProofS3Key?.Trim() ?? "",
        };

        await _repo.AddEntryAsync(entry, ct);
        return Map(entry);
    }

    public async Task<ClientCreditEntryResponse> ChargeInvoiceAsync(
        string clientId, string invoiceId, decimal amount, CancellationToken ct = default)
    {
        var entry = new ClientCreditEntry
        {
            ClientId        = clientId,
            Amount          = -Math.Abs(amount), // always negative (debit)
            EntryType       = "InvoiceCharge",
            PaymentMethod   = "",
            Reference       = invoiceId,
            Notes           = $"Invoice {invoiceId.Substring(0, Math.Min(8, invoiceId.Length)).ToUpper()} charged to account",
            CreatedByUserId = "system",
        };

        await _repo.AddEntryAsync(entry, ct);
        return Map(entry);
    }

    public async Task<ClientCreditEntryResponse> AddManualChargeAsync(
        string clientId, decimal amount, string notes, string createdByUserId, CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentException("Charge amount must be greater than zero.");

        var entry = new ClientCreditEntry
        {
            ClientId        = clientId,
            Amount          = -Math.Abs(amount), // negative = charge against client
            EntryType       = "ManualCharge",
            PaymentMethod   = "",
            Reference       = "",
            Notes           = string.IsNullOrWhiteSpace(notes) ? "Manual charge adjustment" : notes.Trim(),
            CreatedByUserId = createdByUserId,
        };

        await _repo.AddEntryAsync(entry, ct);
        return Map(entry);
    }

    public async Task DeleteEntryAsync(string clientId, string entryId, CancellationToken ct = default)
    {
        // Verify the entry belongs to this client before deleting
        var entries = await _repo.ListByClientAsync(clientId, ct);
        if (!entries.Any(e => e.EntryId == entryId))
            throw new InvalidOperationException($"Entry '{entryId}' not found for client '{clientId}'.");

        await _repo.DeleteEntryAsync(entryId, ct);
    }

    public async Task<ClientCreditLedgerResponse> GetLedgerAsync(
        string clientId, CancellationToken ct = default)
    {
        var client  = await _clientRepo.GetAsync(clientId, ct);
        var entries = await _repo.ListByClientAsync(clientId, ct);
        var balance = entries.Sum(e => e.Amount);

        return new ClientCreditLedgerResponse
        {
            ClientId   = clientId,
            ClientName = client?.ClientName ?? clientId,
            Balance    = balance,
            Entries    = entries.Select(Map).ToList(),
        };
    }

    public Task<decimal> GetBalanceAsync(string clientId, CancellationToken ct = default)
        => _repo.GetBalanceAsync(clientId, ct);

    public async Task<CreditProofUploadUrlResponse> GetProofUploadUrlAsync(
        string clientId, string contentType, CancellationToken ct = default)
    {
        var ext = contentType switch
        {
            "image/jpeg"       => "jpg",
            "image/png"        => "png",
            "image/heic"       => "heic",
            "application/pdf"  => "pdf",
            _                  => "bin"
        };
        var s3Key = $"credit-proofs/{clientId}/{DateTime.UtcNow:yyyyMMddHHmmssffff}.{ext}";
        var url   = await _s3.GeneratePresignedUploadUrlAsync(s3Key, contentType, ct);
        return new CreditProofUploadUrlResponse { UploadUrl = url, S3Key = s3Key };
    }

    private static ClientCreditEntryResponse Map(ClientCreditEntry e) => new()
    {
        EntryId         = e.EntryId,
        ClientId        = e.ClientId,
        Amount          = e.Amount,
        EntryType       = e.EntryType,
        PaymentMethod   = e.PaymentMethod,
        Reference       = e.Reference,
        Notes           = e.Notes,
        CreatedByUserId = e.CreatedByUserId,
        CreatedAt       = e.CreatedAt,
        ProofS3Key      = e.ProofS3Key,
    };
}
