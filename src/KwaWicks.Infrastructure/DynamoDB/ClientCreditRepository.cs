using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;
using System.Globalization;

namespace KwaWicks.Infrastructure.DynamoDB;

public class ClientCreditRepository : IClientCreditRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    public ClientCreditRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb = ddb;
        _tableName = tableName;
    }

    private static string Pk(string entryId) => $"CLIENTCREDIT#{entryId}";
    private const string SkMeta = "META";

    public async Task<ClientCreditEntry> AddEntryAsync(ClientCreditEntry entry, CancellationToken ct = default)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = ToItem(entry),
            ConditionExpression = "attribute_not_exists(PK)"
        }, ct);
        return entry;
    }

    public async Task<List<ClientCreditEntry>> ListByClientAsync(string clientId, CancellationToken ct = default)
    {
        var resp = await _ddb.ScanAsync(new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "EntityType = :et AND ClientId = :cid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":et"]  = new() { S = "ClientCreditEntry" },
                [":cid"] = new() { S = clientId }
            }
        }, ct);

        return resp.Items
            .Select(FromItem)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();
    }

    public async Task<decimal> GetBalanceAsync(string clientId, CancellationToken ct = default)
    {
        var entries = await ListByClientAsync(clientId, ct);
        return entries.Sum(e => e.Amount);
    }

    public async Task<decimal> SumCashDepositsAsync(DateTime? since, CancellationToken ct = default)
    {
        // Positive-amount entries with PaymentMethod=Cash (deposits only, not invoice charges)
        var filterParts = new List<string> { "EntityType = :et", "PaymentMethod = :pm", "Amount > :zero" };
        var values = new Dictionary<string, AttributeValue>
        {
            [":et"]   = new() { S = "ClientCreditEntry" },
            [":pm"]   = new() { S = "Cash" },
            [":zero"] = new() { N = "0" }
        };

        if (since.HasValue)
        {
            filterParts.Add("CreatedAt >= :since");
            values[":since"] = new() { S = since.Value.ToString("O", CultureInfo.InvariantCulture) };
        }

        var resp = await _ddb.ScanAsync(new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = string.Join(" AND ", filterParts),
            ExpressionAttributeValues = values,
            ProjectionExpression = "Amount"
        }, ct);

        return resp.Items
            .Where(i => i.TryGetValue("Amount", out var a) && a.N is not null)
            .Sum(i => decimal.Parse(i["Amount"].N!, NumberStyles.Any, CultureInfo.InvariantCulture));
    }

    public async Task DeleteEntryAsync(string entryId, CancellationToken ct = default)
    {
        await _ddb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = Pk(entryId) },
                ["SK"] = new() { S = SkMeta }
            }
        }, ct);
    }

    // ── Serialisation ──────────────────────────────────────────────────────

    private static Dictionary<string, AttributeValue> ToItem(ClientCreditEntry e) => new()
    {
        ["PK"]              = new() { S = Pk(e.EntryId) },
        ["SK"]              = new() { S = SkMeta },
        ["EntityType"]      = new() { S = "ClientCreditEntry" },
        ["EntryId"]         = new() { S = e.EntryId },
        ["ClientId"]        = new() { S = e.ClientId },
        ["Amount"]          = new() { N = e.Amount.ToString(CultureInfo.InvariantCulture) },
        ["EntryType"]       = new() { S = e.EntryType },
        ["PaymentMethod"]   = new() { S = e.PaymentMethod },
        ["Reference"]       = new() { S = e.Reference },
        ["Notes"]           = new() { S = e.Notes },
        ["CreatedByUserId"] = new() { S = e.CreatedByUserId },
        ["CreatedAt"]       = new() { S = e.CreatedAt.ToString("O") },
        ["ProofS3Key"]      = new() { S = e.ProofS3Key },
    };

    private static ClientCreditEntry FromItem(Dictionary<string, AttributeValue> item)
    {
        static string Str(Dictionary<string, AttributeValue> d, string k) =>
            d.TryGetValue(k, out var v) ? v.S ?? "" : "";
        static decimal Dec(Dictionary<string, AttributeValue> d, string k) =>
            d.TryGetValue(k, out var v) && decimal.TryParse(v.N, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0m;
        static DateTime Dt(Dictionary<string, AttributeValue> d, string k) =>
            d.TryGetValue(k, out var v) && DateTime.TryParse(v.S, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.MinValue;

        return new ClientCreditEntry
        {
            EntryId         = Str(item, "EntryId"),
            ClientId        = Str(item, "ClientId"),
            Amount          = Dec(item, "Amount"),
            EntryType       = Str(item, "EntryType"),
            PaymentMethod   = Str(item, "PaymentMethod"),
            Reference       = Str(item, "Reference"),
            Notes           = Str(item, "Notes"),
            CreatedByUserId = Str(item, "CreatedByUserId"),
            CreatedAt       = Dt(item,  "CreatedAt"),
            ProofS3Key      = Str(item, "ProofS3Key"),
        };
    }
}
