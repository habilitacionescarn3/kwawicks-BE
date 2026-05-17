namespace KwaWicks.Application.DTOs;

// ── Admin: Revenue Summary ───────────────────────────────────────────────────
public class RevenueSummaryResponse
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int TotalInvoices { get; set; }
    public decimal TotalSubTotal { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGrandTotal { get; set; }
    public List<PaymentTypeBreakdown> ByPaymentType { get; set; } = new();
}

public class PaymentTypeBreakdown
{
    public string PaymentType { get; set; } = "";
    public int Count { get; set; }
    public decimal SubTotal { get; set; }
    public decimal GrandTotal { get; set; }
}

// ── Admin: Outstanding Payments ──────────────────────────────────────────────
public class OutstandingPaymentsResponse
{
    public int Count { get; set; }
    public decimal TotalOutstanding { get; set; }
    public List<OutstandingPaymentItem> Items { get; set; } = new();
}

public class OutstandingPaymentItem
{
    public string InvoiceId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public decimal GrandTotal { get; set; }
    public DateTime CreatedAt { get; set; }
    public int DaysOutstanding { get; set; }
    public string ReceiptS3Key { get; set; } = "";
}

// ── Admin: Driver Performance ────────────────────────────────────────────────
public class DriverPerformanceResponse
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<DriverPerformanceItem> Drivers { get; set; } = new();
}

public class DriverPerformanceItem
{
    public string DriverId { get; set; } = "";
    public string DriverName { get; set; } = "";
    public int DeliveriesCompleted { get; set; }
    public decimal TotalValue { get; set; }
    public int TotalDeadReturns { get; set; }
    public int TotalMutilatedReturns { get; set; }
    public int TotalNotWantedReturns { get; set; }
}

// ── Admin: Returns Summary ───────────────────────────────────────────────────
public class ReturnsSummaryResponse
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<ReturnsSummaryItem> Items { get; set; } = new();
}

public class ReturnsSummaryItem
{
    public string SpeciesId { get; set; } = "";
    public int DeadQty { get; set; }
    public int MutilatedQty { get; set; }
    public int NotWantedQty { get; set; }
    public int TotalReturns { get; set; }
}

// ── Admin: Delivery Status Summary ───────────────────────────────────────────
public class DeliveryStatusSummaryResponse
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int OpenCount { get; set; }
    public int InTransitCount { get; set; }
    public int DeliveredCount { get; set; }
    public List<DeliveryStatusItem> Orders { get; set; } = new();
}

public class DeliveryStatusItem
{
    public string DeliveryOrderId { get; set; } = "";
    public string Status { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string DeliveryAddress { get; set; } = "";
    public int TotalItems { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Invoice (populated when driver has completed delivery)
    public string InvoiceId { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public string PaymentStatus { get; set; } = "";
    public decimal GrandTotal { get; set; }
}

// ── Client Credit Ledger Statement ──────────────────────────────────────────
public class ClientCreditStatementResponse
{
    public string CustomerId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerAddress { get; set; } = "";
    public string CustomerContact { get; set; } = "";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public decimal OpeningBalance { get; set; }
    public List<ClientCreditStatementLine> Lines { get; set; } = new();
    public decimal TotalDeposits { get; set; }
    public decimal TotalCharges { get; set; }
    public decimal ClosingBalance { get; set; }
}

public class ClientCreditStatementLine
{
    public DateTime Date { get; set; }
    /// <summary>Deposit | InvoiceCharge | ManualAdjustment</summary>
    public string EntryType { get; set; } = "";
    /// <summary>EFT | Cash | CardMachine | ""</summary>
    public string PaymentMethod { get; set; } = "";
    /// <summary>Notes or reference text from the ledger entry.</summary>
    public string Description { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    /// <summary>Positive = credit/deposit; negative = charge.</summary>
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
}

// ── Admin: Customer Statement (invoice-based — kept for existing reports) ────
public class CustomerStatementResponse
{
    public string CustomerId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerAddress { get; set; } = "";
    public string CustomerContact { get; set; } = "";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<CustomerStatementLine> Lines { get; set; } = new();
    public decimal TotalSubTotal { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGrandTotal { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalOutstanding { get; set; }
}

public class CustomerStatementLine
{
    public string InvoiceId { get; set; } = "";
    public DateTime Date { get; set; }
    public string PaymentType { get; set; } = "";
    public string PaymentStatus { get; set; } = "";
    public decimal SubTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
}

// ── Driver: My Delivery History ──────────────────────────────────────────────
public class MyDeliveryItem
{
    public string DeliveryOrderId { get; set; } = "";
    public string InvoiceId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string DeliveryAddress { get; set; } = "";
    public DateTime CompletedAt { get; set; }
    public decimal GrandTotal { get; set; }
    public string PaymentType { get; set; } = "";
    public string PaymentStatus { get; set; } = "";
}

// ── Sales Report (Client + WalkIn breakdown) ─────────────────────────────────
public class SalesReportResponse
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<SalesReportRow> Rows { get; set; } = new();
}

public class SalesReportRow
{
    public string InvoiceId     { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public DateTime Date        { get; set; }
    public string ClientId      { get; set; } = "";
    public string ClientName    { get; set; } = "";
    public bool   IsWalkIn      { get; set; }
    public string SpeciesId     { get; set; } = "";
    public string SpeciesName   { get; set; } = "";
    public int    Qty           { get; set; }
    public decimal UnitPrice    { get; set; }
    public decimal LineTotal    { get; set; }
    public string PaymentType   { get; set; } = "";
    public string SaleType      { get; set; } = "";
}

// ── Admin: Species Revenue ────────────────────────────────────────────────────
public class SpeciesRevenueResponse
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<string> Months { get; set; } = new();
    public List<SpeciesRevenueSummary> Items { get; set; } = new();
}

public class SpeciesRevenueSummary
{
    public string SpeciesId { get; set; } = "";
    public string SpeciesName { get; set; } = "";
    public int TotalQty { get; set; }
    public decimal TotalRevenue { get; set; }
    public Dictionary<string, decimal> RevenueByMonth { get; set; } = new();
}
