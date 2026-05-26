using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Infrastructure.DynamoDB;

public class ClientRepository : IClientRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    public ClientRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb = ddb;
        _tableName = tableName;
    }

    private static string Pk(string clientId) => $"CLIENT#{clientId}";
    private const string SkValue = "PROFILE";

    public async Task PutAsync(Client client, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = Pk(client.ClientId) },
            ["SK"] = new AttributeValue { S = SkValue },

            ["EntityType"] = new AttributeValue { S = "Client" },

            ["ClientId"] = new AttributeValue { S = client.ClientId },
            ["ClientName"] = new AttributeValue { S = client.ClientName },
            ["ClientAddress"] = new AttributeValue { S = client.ClientAddress ?? "" },
            ["ClientCity"] = new AttributeValue { S = client.ClientCity ?? "" },
            ["ClientProvince"] = new AttributeValue { S = client.ClientProvince ?? "" },
            ["ClientPostalCode"] = new AttributeValue { S = client.ClientPostalCode ?? "" },
            ["ClientContactDetails"] = new AttributeValue { S = client.ClientContactDetails ?? "" },
            ["ClientPhone"] = new AttributeValue { S = client.ClientPhone ?? "" },
            ["ClientType"] = new AttributeValue { S = client.ClientType.ToString() },
            ["IsWalkIn"] = new AttributeValue { BOOL = client.IsWalkIn },

            ["CreatedAtUtc"] = new AttributeValue { S = client.CreatedAtUtc.ToString("O") },
            ["UpdatedAtUtc"] = new AttributeValue { S = client.UpdatedAtUtc.ToString("O") }
        };

        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = item
        }, ct);
    }

    public async Task<Client?> GetAsync(string clientId, CancellationToken ct = default)
    {
        var res = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(clientId) },
                ["SK"] = new AttributeValue { S = SkValue }
            }
        }, ct);

        if (res.Item is null || res.Item.Count == 0) return null;
        return FromItem(res.Item);
    }

    public async Task<List<Client>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        // Paginate through the entire table — DynamoDB's Limit caps items *scanned*
        // (not returned), so a fixed Limit would silently miss clients in large tables.
        var clients = new List<Client>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var req = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "EntityType = :t AND SK = :sk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":t"] = new AttributeValue { S = "Client" },
                    [":sk"] = new AttributeValue { S = SkValue }
                }
            };

            if (lastKey is not null)
                req.ExclusiveStartKey = lastKey;

            var res = await _ddb.ScanAsync(req, ct);
            clients.AddRange(res.Items.Select(FromItem));
            lastKey = res.LastEvaluatedKey?.Count > 0 ? res.LastEvaluatedKey : null;
        }
        while (lastKey is not null);

        return clients;
    }

    public async Task<bool> DeleteAsync(string clientId, CancellationToken ct = default)
    {
        await _ddb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(clientId) },
                ["SK"] = new AttributeValue { S = SkValue }
            }
        }, ct);

        return true;
    }

    private static Client FromItem(Dictionary<string, AttributeValue> item)
    {
        item.TryGetValue("ClientType", out var ctVal);

        Enum.TryParse<ClientType>(ctVal?.S, ignoreCase: true, out var clientType);

        DateTime.TryParse(item.GetValueOrDefault("CreatedAtUtc")?.S, out var created);
        DateTime.TryParse(item.GetValueOrDefault("UpdatedAtUtc")?.S, out var updated);

        return new Client
        {
            ClientId = item["ClientId"].S,
            ClientName = item.GetValueOrDefault("ClientName")?.S ?? "",
            ClientAddress = item.GetValueOrDefault("ClientAddress")?.S ?? "",
            ClientCity = item.GetValueOrDefault("ClientCity")?.S ?? "",
            ClientProvince = item.GetValueOrDefault("ClientProvince")?.S ?? "",
            ClientPostalCode = item.GetValueOrDefault("ClientPostalCode")?.S ?? "",
            ClientContactDetails = item.GetValueOrDefault("ClientContactDetails")?.S ?? "",
            ClientPhone = item.GetValueOrDefault("ClientPhone")?.S ?? "",
            ClientType = clientType,
            IsWalkIn = item.TryGetValue("IsWalkIn", out var wi) && wi.IsBOOLSet && wi.BOOL == true,
            CreatedAtUtc = created == default ? DateTime.UtcNow : created,
            UpdatedAtUtc = updated == default ? DateTime.UtcNow : updated
        };
    }
}