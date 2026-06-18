using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;
using System.Globalization;

namespace KwaWicks.Infrastructure.DynamoDB;

public class FuelRepository : IFuelRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    public FuelRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb = ddb;
        _tableName = tableName;
    }

    private static string Pk(string id) => $"FUELISSUE#{id}";
    private const string SkProfile = "PROFILE";

    public async Task<FuelIssue> CreateAsync(FuelIssue issue, CancellationToken ct)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = ToItem(issue),
        }, ct);
        return issue;
    }

    public async Task<List<FuelIssue>> ListAsync(CancellationToken ct)
    {
        var req = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "EntityType = :et",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":et"] = new AttributeValue { S = "FuelIssue" }
            }
        };

        var result = new List<FuelIssue>();
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

    private static Dictionary<string, AttributeValue> ToItem(FuelIssue f)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = Pk(f.IssueId) },
            ["SK"] = new AttributeValue { S = SkProfile },
            ["EntityType"] = new AttributeValue { S = "FuelIssue" },
            ["IssueId"] = new AttributeValue { S = f.IssueId },
            ["VehicleId"] = new AttributeValue { S = f.VehicleId },
            ["TankId"] = new AttributeValue { S = f.TankId },
            ["SiteId"] = new AttributeValue { S = f.SiteId },
            ["Litres"] = new AttributeValue { N = f.Litres.ToString(CultureInfo.InvariantCulture) },
            ["Reference"] = new AttributeValue { S = f.Reference },
            ["IssuedByName"] = new AttributeValue { S = f.IssuedByName },
            ["IssuedAt"] = new AttributeValue { S = f.IssuedAt.ToString("O", CultureInfo.InvariantCulture) },
        };
        if (f.OdometerKm.HasValue)    item["OdometerKm"]    = new AttributeValue { N = f.OdometerKm.Value.ToString(CultureInfo.InvariantCulture) };
        if (f.CostPerLitre.HasValue)  item["CostPerLitre"]  = new AttributeValue { N = f.CostPerLitre.Value.ToString(CultureInfo.InvariantCulture) };
        if (f.TotalCost.HasValue)     item["TotalCost"]     = new AttributeValue { N = f.TotalCost.Value.ToString(CultureInfo.InvariantCulture) };
        return item;
    }

    private static FuelIssue FromItem(Dictionary<string, AttributeValue> item) => new()
    {
        IssueId      = item.TryGetValue("IssueId", out var iid) ? iid.S ?? "" : "",
        VehicleId    = item.TryGetValue("VehicleId", out var vid) ? vid.S ?? "" : "",
        TankId       = item.TryGetValue("TankId", out var tid) ? tid.S ?? "" : "",
        SiteId       = item.TryGetValue("SiteId", out var sid) ? sid.S ?? "" : "",
        Litres       = item.TryGetValue("Litres", out var li) && decimal.TryParse(li.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var liv) ? liv : 0,
        OdometerKm   = item.TryGetValue("OdometerKm", out var od) && decimal.TryParse(od.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var odv) ? odv : null,
        CostPerLitre = item.TryGetValue("CostPerLitre", out var cp) && decimal.TryParse(cp.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var cpv) ? cpv : null,
        TotalCost    = item.TryGetValue("TotalCost", out var tc) && decimal.TryParse(tc.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var tcv) ? tcv : null,
        Reference    = item.TryGetValue("Reference", out var ref_) ? ref_.S ?? "" : "",
        IssuedByName = item.TryGetValue("IssuedByName", out var ibn) ? ibn.S ?? "" : "",
        IssuedAt     = item.TryGetValue("IssuedAt", out var ia) ? DateTime.Parse(ia.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : DateTime.UtcNow,
    };
}
