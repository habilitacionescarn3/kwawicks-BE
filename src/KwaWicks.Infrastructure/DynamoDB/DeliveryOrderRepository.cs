using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;
using System.Globalization;
using System.Text.Json;

namespace KwaWicks.Infrastructure.DynamoDB;

public class DeliveryOrderRepository : IDeliveryOrderRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    public DeliveryOrderRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb = ddb;
        _tableName = tableName;
    }

    private static string Pk(string id) => $"DO#{id}";
    private const string SkMeta = "META";

    public async Task<DeliveryOrder> CreateAsync(DeliveryOrder deliveryOrder, CancellationToken ct)
    {
        if (deliveryOrder is null) throw new ArgumentNullException(nameof(deliveryOrder));

        var req = new PutItemRequest
        {
            TableName = _tableName,
            Item = ToItem(deliveryOrder),
            ConditionExpression = "attribute_not_exists(PK)"
        };

        await _ddb.PutItemAsync(req, ct);
        return deliveryOrder;
    }

    public async Task<DeliveryOrder?> GetAsync(string deliveryOrderId, CancellationToken ct)
    {
        var res = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(deliveryOrderId) },
                ["SK"] = new AttributeValue { S = SkMeta }
            }
        }, ct);

        return res.Item is null || res.Item.Count == 0 ? null : FromItem(res.Item);
    }

    public async Task<DeliveryOrder> UpdateAsync(DeliveryOrder deliveryOrder, CancellationToken ct)
    {
        if (deliveryOrder is null) throw new ArgumentNullException(nameof(deliveryOrder));

        var req = new PutItemRequest
        {
            TableName = _tableName,
            Item = ToItem(deliveryOrder)
        };

        await _ddb.PutItemAsync(req, ct);
        return deliveryOrder;
    }

    public async Task<List<DeliveryOrder>> ListAsync(string? driverId, string? hubId, string? status, CancellationToken ct)
    {
        var filterParts = new List<string> { "EntityType = :et" };
        var values = new Dictionary<string, AttributeValue>
        {
            [":et"] = new AttributeValue { S = "DeliveryOrder" }
        };

        if (!string.IsNullOrWhiteSpace(driverId))
        {
            filterParts.Add("AssignedDriverId = :driverId");
            values[":driverId"] = new AttributeValue { S = driverId };
        }

        if (!string.IsNullOrWhiteSpace(hubId))
        {
            filterParts.Add("HubId = :hubId");
            values[":hubId"] = new AttributeValue { S = hubId };
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            filterParts.Add("#st = :status");
            values[":status"] = new AttributeValue { S = status };
        }

        var req = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = string.Join(" AND ", filterParts),
            ExpressionAttributeValues = values
        };

        // Status is a reserved word in DynamoDB
        if (!string.IsNullOrWhiteSpace(status))
            req.ExpressionAttributeNames = new Dictionary<string, string> { ["#st"] = "Status" };

        var result = new List<DeliveryOrder>();
        ScanResponse? response;

        do
        {
            response = await _ddb.ScanAsync(req, ct);
            result.AddRange(response.Items.Select(FromItem));
            req.ExclusiveStartKey = response.LastEvaluatedKey;
        }
        while (response.LastEvaluatedKey is { Count: > 0 });

        return result;
    }

    public async Task DeleteAsync(string deliveryOrderId, CancellationToken ct)
    {
        await _ddb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(deliveryOrderId) },
                ["SK"] = new AttributeValue { S = SkMeta }
            }
        }, ct);
    }

    private static Dictionary<string, AttributeValue> ToItem(DeliveryOrder d) =>
        new()
        {
            ["PK"] = new AttributeValue { S = Pk(d.DeliveryOrderId) },
            ["SK"] = new AttributeValue { S = SkMeta },
            ["EntityType"] = new AttributeValue { S = "DeliveryOrder" },

            ["DeliveryOrderId"] = new AttributeValue { S = d.DeliveryOrderId },
            ["InvoiceId"] = new AttributeValue { S = d.InvoiceId ?? "" },
            ["HubId"] = new AttributeValue { S = d.HubId ?? "" },
            ["CustomerId"] = new AttributeValue { S = d.CustomerId ?? "" },
            ["AssignedDriverId"] = new AttributeValue { S = d.AssignedDriverId ?? "" },
            ["AssignedDriverName"] = new AttributeValue { S = d.AssignedDriverName ?? "" },
            ["Status"] = new AttributeValue { S = d.Status ?? "Open" },

            ["DeliveryAddressLine1"] = new AttributeValue { S = d.DeliveryAddressLine1 ?? "" },
            ["City"] = new AttributeValue { S = d.City ?? "" },
            ["Province"] = new AttributeValue { S = d.Province ?? "" },
            ["PostalCode"] = new AttributeValue { S = d.PostalCode ?? "" },

            ["ReturnSubmitted"] = new AttributeValue { BOOL = d.ReturnSubmitted },
            ["ReturnCheckedIn"] = new AttributeValue { BOOL = d.ReturnCheckedIn },

            ["CreatedAtUtc"] = new AttributeValue { S = d.CreatedAt.ToString("O", CultureInfo.InvariantCulture) },
            ["UpdatedAtUtc"] = new AttributeValue { S = d.UpdatedAt.ToString("O", CultureInfo.InvariantCulture) },

            ["LinesJson"] = new AttributeValue { S = JsonSerializer.Serialize(d.Lines ?? new List<DeliveryOrderLine>()) }
        };

    private static DeliveryOrder FromItem(Dictionary<string, AttributeValue> item)
    {
        var linesJson = item.TryGetValue("LinesJson", out var lj) ? lj.S : "[]";
        var lines = JsonSerializer.Deserialize<List<DeliveryOrderLine>>(linesJson ?? "[]") ?? new();

        return new DeliveryOrder
        {
            DeliveryOrderId = item.TryGetValue("DeliveryOrderId", out var id) ? id.S ?? "" : "",
            InvoiceId = item.TryGetValue("InvoiceId", out var inv) ? inv.S ?? "" : "",
            HubId = item.TryGetValue("HubId", out var h) ? h.S ?? "" : "",
            CustomerId = item.TryGetValue("CustomerId", out var c) ? c.S ?? "" : "",
            AssignedDriverId = item.TryGetValue("AssignedDriverId", out var did) ? did.S ?? "" : "",
            AssignedDriverName = item.TryGetValue("AssignedDriverName", out var dn) ? dn.S ?? "" : "",
            Status = item.TryGetValue("Status", out var st) ? st.S ?? "Open" : "Open",

            DeliveryAddressLine1 = item.TryGetValue("DeliveryAddressLine1", out var a1) ? a1.S ?? "" : "",
            City = item.TryGetValue("City", out var city) ? city.S ?? "" : "",
            Province = item.TryGetValue("Province", out var prov) ? prov.S ?? "" : "",
            PostalCode = item.TryGetValue("PostalCode", out var pc) ? pc.S ?? "" : "",

            CreatedAt = item.TryGetValue("CreatedAtUtc", out var ca)
                ? DateTime.Parse(ca.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : DateTime.UtcNow,

            UpdatedAt = item.TryGetValue("UpdatedAtUtc", out var ua)
                ? DateTime.Parse(ua.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : DateTime.UtcNow,

            ReturnSubmitted = item.TryGetValue("ReturnSubmitted", out var rs) && (rs.BOOL == true),
            ReturnCheckedIn = item.TryGetValue("ReturnCheckedIn", out var rc) && (rc.BOOL == true),

            Lines = lines
        };
    }
}
