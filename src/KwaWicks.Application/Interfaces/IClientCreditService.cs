using KwaWicks.Application.DTOs;

namespace KwaWicks.Application.Interfaces;

public interface IClientCreditService
{
    Task<ClientCreditEntryResponse> AddDepositAsync(string clientId, AddCreditDepositRequest request, CancellationToken ct = default);
    Task<ClientCreditEntryResponse> ChargeInvoiceAsync(string clientId, string invoiceId, decimal amount, CancellationToken ct = default);
    Task<ClientCreditEntryResponse> AddManualChargeAsync(string clientId, decimal amount, string notes, string createdByUserId, CancellationToken ct = default);
    Task DeleteEntryAsync(string clientId, string entryId, CancellationToken ct = default);
    Task<ClientCreditLedgerResponse> GetLedgerAsync(string clientId, CancellationToken ct = default);
    Task<decimal> GetBalanceAsync(string clientId, CancellationToken ct = default);
    Task<CreditProofUploadUrlResponse> GetProofUploadUrlAsync(string clientId, string contentType, CancellationToken ct = default);
}
