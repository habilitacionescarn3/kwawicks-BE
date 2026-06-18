using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;
using System.Globalization;

namespace KwaWicks.Infrastructure.DynamoDB;

public class DipTankRepository : IDipTankRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    public DipTankRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb = ddb;
        _tableName = tableName;
    }

    private static string TankPk(string id) => $"DIPTANK#{id}";
    private static string ReadingPk(string id) => $"DIPREADING#{id}";
    private const string SkProfile = "PROFILE";

    // ── Tanks ─────────────────────────────────────────────────────────────────

    public async Task<DipTank> CreateTankAsync(DipTank tank, CancellationToken ct)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = TankToItem(tank),
            ConditionExpression = "attribute_not_exists(PK)"
        }, ct);
        return tank;
    }

    public async Task<DipTank?> GetTankAsync(string tankId, CancellationToken ct)
    {
        var res = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = TankPk(tankId) },
                ["SK"] = new AttributeValue { S = SkProfile }
            }
        }, ct);
        return res.Item is null || res.Item.Count == 0 ? null : TankFromItem(res.Item);
    }

    public async Task<List<DipTank>> ListTanksAsync(CancellationToken ct)
    {
        var req = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "EntityType = :et",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":et"] = new AttributeValue { S = "DipTank" }
            }
        };

        var result = new List<DipTank>();
        ScanResponse? response;
        do
        {
            response = await _ddb.ScanAsync(req, ct);
            result.AddRange(response.Items.Select(TankFromItem));
            req.ExclusiveStartKey = response.LastEvaluatedKey;
        }
        while (response.LastEvaluatedKey is { Count: > 0 });

        return result;
    }

    // ── Readings ──────────────────────────────────────────────────────────────

    public async Task<DipReading> CreateReadingAsync(DipReading reading, CancellationToken ct)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = ReadingToItem(reading),
        }, ct);
        return reading;
    }

    public async Task<List<DipReading>> ListReadingsAsync(CancellationToken ct)
    {
        var req = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "EntityType = :et",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":et"] = new AttributeValue { S = "DipReading" }
            }
        };

        var result = new List<DipReading>();
        ScanResponse? response;
        do
        {
            response = await _ddb.ScanAsync(req, ct);
            result.AddRange(response.Items.Select(ReadingFromItem));
            req.ExclusiveStartKey = response.LastEvaluatedKey;
        }
        while (response.LastEvaluatedKey is { Count: > 0 });

        return result;
    }

    public async Task<List<DipReading>> ListReadingsByTankAsync(string tankId, CancellationToken ct)
    {
        var all = await ListReadingsAsync(ct);
        return all.Where(r => r.TankId == tankId).ToList();
    }

    // ── Serialisation ─────────────────────────────────────────────────────────

    private static Dictionary<string, AttributeValue> TankToItem(DipTank t)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = TankPk(t.TankId) },
            ["SK"] = new AttributeValue { S = SkProfile },
            ["EntityType"] = new AttributeValue { S = "DipTank" },
            ["TankId"] = new AttributeValue { S = t.TankId },
            ["Name"] = new AttributeValue { S = t.Name },
            ["Description"] = new AttributeValue { S = t.Description },
            ["SiteId"] = new AttributeValue { S = t.SiteId },
            ["FuelType"] = new AttributeValue { S = t.FuelType },
            ["CapacityLitres"] = new AttributeValue { N = t.CapacityLitres.ToString(CultureInfo.InvariantCulture) },
            ["IsActive"] = new AttributeValue { BOOL = t.IsActive },
            ["CreatedAtUtc"] = new AttributeValue { S = t.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture) },
            ["UpdatedAtUtc"] = new AttributeValue { S = t.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture) },
        };
        if (t.LowQtyLitres.HasValue)
            item["LowQtyLitres"] = new AttributeValue { N = t.LowQtyLitres.Value.ToString(CultureInfo.InvariantCulture) };
        return item;
    }

    private static DipTank TankFromItem(Dictionary<string, AttributeValue> item) => new()
    {
        TankId        = item.TryGetValue("TankId", out var tid) ? tid.S ?? "" : "",
        Name          = item.TryGetValue("Name", out var n) ? n.S ?? "" : "",
        Description   = item.TryGetValue("Description", out var d) ? d.S ?? "" : "",
        SiteId        = item.TryGetValue("SiteId", out var si) ? si.S ?? "" : "",
        FuelType      = item.TryGetValue("FuelType", out var ft) ? ft.S ?? "diesel" : "diesel",
        CapacityLitres = item.TryGetValue("CapacityLitres", out var cap) && decimal.TryParse(cap.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var capv) ? capv : 0,
        LowQtyLitres  = item.TryGetValue("LowQtyLitres", out var lq) && decimal.TryParse(lq.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var lqv) ? lqv : null,
        IsActive      = item.TryGetValue("IsActive", out var ia) && ia.IsBOOLSet && ia.BOOL == true,
        CreatedAtUtc  = item.TryGetValue("CreatedAtUtc", out var ca) ? DateTime.Parse(ca.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : DateTime.UtcNow,
        UpdatedAtUtc  = item.TryGetValue("UpdatedAtUtc", out var ua) ? DateTime.Parse(ua.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : DateTime.UtcNow,
    };

    private static Dictionary<string, AttributeValue> ReadingToItem(DipReading r)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = ReadingPk(r.ReadingId) },
            ["SK"] = new AttributeValue { S = SkProfile },
            ["EntityType"] = new AttributeValue { S = "DipReading" },
            ["ReadingId"] = new AttributeValue { S = r.ReadingId },
            ["TankId"] = new AttributeValue { S = r.TankId },
            ["ReadingLitres"] = new AttributeValue { N = r.ReadingLitres.ToString(CultureInfo.InvariantCulture) },
            ["Notes"] = new AttributeValue { S = r.Notes },
            ["RecordedBy"] = new AttributeValue { S = r.RecordedBy },
            ["RecordedAt"] = new AttributeValue { S = r.RecordedAt.ToString("O", CultureInfo.InvariantCulture) },
        };
        if (r.ReadingMm.HasValue) item["ReadingMm"] = new AttributeValue { N = r.ReadingMm.Value.ToString(CultureInfo.InvariantCulture) };
        if (r.PctFull.HasValue)   item["PctFull"]   = new AttributeValue { N = r.PctFull.Value.ToString(CultureInfo.InvariantCulture) };
        return item;
    }

    private static DipReading ReadingFromItem(Dictionary<string, AttributeValue> item) => new()
    {
        ReadingId     = item.TryGetValue("ReadingId", out var rid) ? rid.S ?? "" : "",
        TankId        = item.TryGetValue("TankId", out var tid) ? tid.S ?? "" : "",
        ReadingLitres = item.TryGetValue("ReadingLitres", out var rl) && decimal.TryParse(rl.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var rlv) ? rlv : 0,
        ReadingMm     = item.TryGetValue("ReadingMm", out var mm) && decimal.TryParse(mm.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var mmv) ? mmv : null,
        PctFull       = item.TryGetValue("PctFull", out var pf) && decimal.TryParse(pf.N, NumberStyles.Number, CultureInfo.InvariantCulture, out var pfv) ? pfv : null,
        Notes         = item.TryGetValue("Notes", out var no) ? no.S ?? "" : "",
        RecordedBy    = item.TryGetValue("RecordedBy", out var rb) ? rb.S ?? "" : "",
        RecordedAt    = item.TryGetValue("RecordedAt", out var ra) ? DateTime.Parse(ra.S!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : DateTime.UtcNow,
    };
}
