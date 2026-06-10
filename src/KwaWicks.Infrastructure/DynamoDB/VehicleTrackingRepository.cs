using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;
using System.Globalization;

namespace KwaWicks.Infrastructure.DynamoDB;

/// <summary>
/// DynamoDB key design:
///   Live  — PK: TRACKING#LIVE,       SK: DRIVER#{driverId}
///   Hist  — PK: DRIVER#{driverId},   SK: LOC#{yyyy-MM-ddTHH:mm:ssZ}
///
/// History items carry a "Ttl" epoch-seconds attribute so DynamoDB TTL
/// can auto-expire them after 7 days (enable TTL on the "Ttl" attribute
/// in the DynamoDB console if not already enabled).
/// </summary>
public class VehicleTrackingRepository : IVehicleTrackingRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    private const string LivePk = "TRACKING#LIVE";
    private static string LiveSk(string driverId)  => $"DRIVER#{driverId}";
    private static string HistPk(string driverId)  => $"DRIVER#{driverId}";
    private static string HistSk(DateTime t)       => $"LOC#{t:yyyy-MM-ddTHH:mm:ssZ}";
    private static long   TtlEpoch(DateTime t)     => new DateTimeOffset(t.AddDays(7)).ToUnixTimeSeconds();

    public VehicleTrackingRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb       = ddb;
        _tableName = tableName;
    }

    // ── Write ──────────────────────────────────────────────────────────────────

    public async Task UpsertLiveAsync(VehicleLocation loc, CancellationToken ct = default)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item      = LiveItem(loc)
        }, ct);
    }

    public async Task AppendHistoryAsync(VehicleLocation loc, CancellationToken ct = default)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item      = HistItem(loc)
        }, ct);
    }

    // ── Read ───────────────────────────────────────────────────────────────────

    public async Task<List<VehicleLocation>> GetAllLiveAsync(CancellationToken ct = default)
    {
        var resp = await _ddb.QueryAsync(new QueryRequest
        {
            TableName                 = _tableName,
            KeyConditionExpression    = "PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new AttributeValue { S = LivePk }
            }
        }, ct);

        return resp.Items.Select(FromItem).ToList();
    }

    public async Task<List<VehicleLocation>> GetHistoryAsync(
        string driverId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var resp = await _ddb.QueryAsync(new QueryRequest
        {
            TableName                 = _tableName,
            KeyConditionExpression    = "PK = :pk AND SK BETWEEN :from AND :to",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"]   = new AttributeValue { S = HistPk(driverId) },
                [":from"] = new AttributeValue { S = HistSk(from) },
                [":to"]   = new AttributeValue { S = HistSk(to) }
            }
        }, ct);

        // Filter to only tracking history (excludes any future DRIVER# items)
        return resp.Items
            .Where(i => i.TryGetValue("EntityType", out var et) && et.S == "TrackingHistory")
            .Select(FromItem)
            .ToList();
    }

    // ── Item builders ──────────────────────────────────────────────────────────

    private static Dictionary<string, AttributeValue> LiveItem(VehicleLocation loc)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"]         = Str(LivePk),
            ["SK"]         = Str(LiveSk(loc.DriverId)),
            ["EntityType"] = Str("TrackingLive"),
            ["DriverId"]   = Str(loc.DriverId),
            ["DriverName"] = Str(loc.DriverName),
            ["Lat"]        = Num(loc.Latitude),
            ["Lng"]        = Num(loc.Longitude),
            ["RecordedAt"] = Str(loc.RecordedAt.ToString("O", CultureInfo.InvariantCulture)),
            ["DeliveryOrderId"] = Str(loc.DeliveryOrderId ?? ""),
        };
        if (loc.Accuracy.HasValue) item["Accuracy"] = Num(loc.Accuracy.Value);
        if (loc.Speed.HasValue)    item["Speed"]    = Num(loc.Speed.Value);
        return item;
    }

    private static Dictionary<string, AttributeValue> HistItem(VehicleLocation loc)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"]         = Str(HistPk(loc.DriverId)),
            ["SK"]         = Str(HistSk(loc.RecordedAt)),
            ["EntityType"] = Str("TrackingHistory"),
            ["DriverId"]   = Str(loc.DriverId),
            ["DriverName"] = Str(loc.DriverName),
            ["Lat"]        = Num(loc.Latitude),
            ["Lng"]        = Num(loc.Longitude),
            ["RecordedAt"] = Str(loc.RecordedAt.ToString("O", CultureInfo.InvariantCulture)),
            ["Ttl"]        = new AttributeValue { N = TtlEpoch(loc.RecordedAt).ToString() },
        };
        if (loc.Accuracy.HasValue)                 item["Accuracy"]        = Num(loc.Accuracy.Value);
        if (loc.Speed.HasValue)                    item["Speed"]           = Num(loc.Speed.Value);
        if (!string.IsNullOrEmpty(loc.DeliveryOrderId)) item["DeliveryOrderId"] = Str(loc.DeliveryOrderId);
        return item;
    }

    // ── Deserialization ────────────────────────────────────────────────────────

    private static VehicleLocation FromItem(Dictionary<string, AttributeValue> i) => new()
    {
        DriverId        = S(i, "DriverId"),
        DriverName      = S(i, "DriverName"),
        Latitude        = N(i, "Lat"),
        Longitude       = N(i, "Lng"),
        Accuracy        = NullableN(i, "Accuracy"),
        Speed           = NullableN(i, "Speed"),
        RecordedAt      = i.TryGetValue("RecordedAt", out var ra)
                            ? DateTime.Parse(ra.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                            : DateTime.UtcNow,
        DeliveryOrderId = i.TryGetValue("DeliveryOrderId", out var doi) && !string.IsNullOrEmpty(doi.S)
                            ? doi.S : null,
    };

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AttributeValue Str(string v) => new() { S = v };
    private static AttributeValue Num(double  v) => new() { N = v.ToString(CultureInfo.InvariantCulture) };

    private static string S(Dictionary<string, AttributeValue> i, string key) =>
        i.TryGetValue(key, out var v) ? v.S ?? "" : "";

    private static double N(Dictionary<string, AttributeValue> i, string key) =>
        i.TryGetValue(key, out var v) && v.N != null &&
        double.TryParse(v.N, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0;

    private static double? NullableN(Dictionary<string, AttributeValue> i, string key) =>
        i.TryGetValue(key, out var v) && v.N != null &&
        double.TryParse(v.N, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : null;
}
