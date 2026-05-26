namespace KwaWicks.Application.DTOs;

// ── Requests ───────────────────────────────────────────────────────────────

public class AddCreditDepositRequest
{
    public decimal Amount { get; set; }

    /// <summary>Cash | EFT | CardMachine</summary>
    public string PaymentMethod { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";

    /// <summary>S3 key returned by the proof-upload-url endpoint. Optional.</summary>
    public string? ProofS3Key { get; set; }
}

public class AddManualChargeRequest
{
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public class CreditProofUploadUrlResponse
{
    public string UploadUrl { get; set; } = "";
    public string S3Key { get; set; } = "";
}

// ── Responses ──────────────────────────────────────────────────────────────

public class ClientCreditEntryResponse
{
    public string EntryId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public decimal Amount { get; set; }
    public string EntryType { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string ProofS3Key { get; set; } = "";
}

public class ClientCreditLedgerResponse
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public decimal Balance { get; set; }
    public List<ClientCreditEntryResponse> Entries { get; set; } = new();
}
