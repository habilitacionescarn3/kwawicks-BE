using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;
using System.Globalization;

namespace KwaWicks.Infrastructure.DynamoDB;

public class SiteRepository : ISiteRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    public SiteRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb = ddb;
        _tableName = tableName;
    }

    private static string Pk(string id) => $"SITE#{id}";
    private const string SkProfile = "PROFILE";

    public async Task<Site> CreateAsync(Site site, CancellationToken ct)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = ToItem(site),
            ConditionExpression = "attribute_not_exists(PK)"
        }, ct);
        return site;
    }

    public async Task<Site?> GetAsync(string siteId, CancellationToken ct)
    {
        var res = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(siteId) },
                ["SK"] = new AttributeValue { S = SkProfile }
            }
        }, ct);
        return res.Item is null || res.Item.Count == 0 ? null : FromItem(res.Item);
    }

    public async Task<Site> UpdateAsync(Site site, CancellationToken ct)
    {
        await _ddb.PutItemAsync(new PutItemRequest { TableName = _tableName, Item = ToItem(site) }, ct);
        return site;
    }

    public async Task<List<Site>> ListAsync(CancellationToken ct)
    {
        var req = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "EntityType = :et",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":et"] = new AttributeValue { S = "Site" }
            }
        };

        var result = new List<Site>();
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

    private static Dictionary<string, AttributeValue> ToItem(Site s) => new()
    {
        ["PK"] = new AttributeValue { S = Pk(s.SiteId) },
        ["SK"] = new AttributeValue { S = SkProfile },
        ["EntityType"] = new AttributeValue { S = "Site" },
        ["SiteId"] = new AttributeValue { S = s.SiteId },
        ["Name"] = new AttributeValue { S = s.Name },
        ["Address"] = new AttributeValue { S = s.Address },
        ["ContactName"] = new AttributeValue { S = s.ContactName },
        ["ContactPhone"] = new AttributeValue { S = s.ContactPhone },
        ["IsActive"] = new AttributeValue { BOOL = s.IsActive },
        ["CreatedAtUtc"] = new AttributeValue { S = s.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture) },
        ["UpdatedAtUtc"] = new AttributeValue { S = s.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture) },
    };

    private static Site FromItem(Dictionary<string, AttributeValue> item) => new()
    {
        SiteId       = item.TryGetValue("SiteId", out var id) ? id.S ?? "" : "",
        Name         = item.TryGetValue("Name", out var n) ? n.S ?? "" : "",
        Address      = item.TryGetValue("Address", out var a) ? a.S ?? "" : "",
        ContactName  = item.TryGetValue("ContactName", out var cn) ? cn.S ?? "" : "",
        ContactPhone = item.TryGetValue("ContactPhone", out var cp) ? cp.S ?? "" : "",
        IsActive     = item.TryGetValue("IsActive", out var ia) && ia.IsBOOLSet && ia.BOOL == true,
        CreatedAtUtc = item.TryGetValue("CreatedAtUtc", out var ca) ? DateTime.Parse(ca.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : DateTime.UtcNow,
        UpdatedAtUtc = item.TryGetValue("UpdatedAtUtc", out var ua) ? DateTime.Parse(ua.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : DateTime.UtcNow,
    };
}
