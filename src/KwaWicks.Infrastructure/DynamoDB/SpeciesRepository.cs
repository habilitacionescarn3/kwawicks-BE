using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;
using System.Globalization;

namespace KwaWicks.Infrastructure.DynamoDB;

public class SpeciesRepository : ISpeciesRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _tableName;

    public SpeciesRepository(IAmazonDynamoDB ddb, string tableName)
    {
        _ddb = ddb;
        _tableName = tableName;
    }

    private static string Pk(string speciesId) => $"SPECIES#{speciesId}";
    private const string SkMeta = "META";

    public async Task<Species> CreateAsync(Species species, CancellationToken ct)
    {
        if (species is null) throw new ArgumentNullException(nameof(species));
        if (string.IsNullOrWhiteSpace(species.SpeciesId))
            throw new ArgumentException("SpeciesId is required.", nameof(species));
        if (string.IsNullOrWhiteSpace(species.Name))
            throw new ArgumentException("Name is required.", nameof(species));

        var req = new PutItemRequest
        {
            TableName = _tableName,
            Item = ToItem(species),
            ConditionExpression = "attribute_not_exists(PK)"
        };

        await _ddb.PutItemAsync(req, ct);
        return species;
    }

    public async Task<List<Species>> ListAsync(CancellationToken ct)
    {
        var req = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "begins_with(PK, :p) AND SK = :sk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":p"] = new AttributeValue { S = "SPECIES#" },
                [":sk"] = new AttributeValue { S = SkMeta }
            }
        };

        var res = await _ddb.ScanAsync(req, ct);
        return res.Items.Select(FromItem).OrderBy(s => s.Name).ToList();
    }

    public async Task<Species?> GetAsync(string speciesId, CancellationToken ct)
    {
        var res = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(speciesId) },
                ["SK"] = new AttributeValue { S = SkMeta }
            }
        }, ct);

        return res.Item is null || res.Item.Count == 0 ? null : FromItem(res.Item);
    }

    public async Task DeleteAsync(string speciesId, CancellationToken ct)
    {
        await _ddb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(speciesId) },
                ["SK"] = new AttributeValue { S = SkMeta }
            }
        }, ct);
    }

    public async Task<Species?> UpdateAsync(Species species, CancellationToken ct)
    {
        if (species is null) throw new ArgumentNullException(nameof(species));
        if (string.IsNullOrWhiteSpace(species.SpeciesId))
            throw new ArgumentException("SpeciesId is required.", nameof(species));

        var req = new PutItemRequest
        {
            TableName = _tableName,
            Item = ToItem(species),
            ConditionExpression = "attribute_exists(PK)"
        };

        await _ddb.PutItemAsync(req, ct);
        return species;
    }

    public async Task AdjustStockAsync(string speciesId, int onHandDelta, int bookedDelta, CancellationToken ct, int minOnHandRequired = 0)
    {
        var expressionNames = new Dictionary<string, string>
        {
            ["#oh"] = "QtyOnHandHub",
            ["#bo"] = "QtyBookedOutForDelivery"
        };
        var expressionValues = new Dictionary<string, AttributeValue>
        {
            [":onHand"] = new AttributeValue { N = onHandDelta.ToString(CultureInfo.InvariantCulture) },
            [":booked"] = new AttributeValue { N = bookedDelta.ToString(CultureInfo.InvariantCulture) }
        };

        string condition = "attribute_exists(PK)";
        if (minOnHandRequired > 0)
        {
            // Prevent hub stock from dropping below the required minimum
            expressionValues[":minQty"] = new AttributeValue { N = minOnHandRequired.ToString(CultureInfo.InvariantCulture) };
            condition += " AND #oh >= :minQty";
        }

        var req = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = Pk(speciesId) },
                ["SK"] = new AttributeValue { S = SkMeta }
            },
            UpdateExpression = "ADD #oh :onHand, #bo :booked",
            ConditionExpression = condition,
            ExpressionAttributeNames = expressionNames,
            ExpressionAttributeValues = expressionValues
        };

        try
        {
            await _ddb.UpdateItemAsync(req, ct);
        }
        catch (ConditionalCheckFailedException)
        {
            throw new InvalidOperationException(
                $"Insufficient stock for species '{speciesId}'. " +
                $"Required at least {minOnHandRequired} on hand.");
        }
    }

    // ✅ Single source of truth for mapping -> DynamoDB item
    private static Dictionary<string, AttributeValue> ToItem(Species s)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = Pk(s.SpeciesId) },
            ["SK"] = new AttributeValue { S = SkMeta },
            ["EntityType"] = new AttributeValue { S = "Species" },

            ["SpeciesId"] = new AttributeValue { S = s.SpeciesId },
            ["Name"] = new AttributeValue { S = s.Name },

            // ✅ Numbers MUST use invariant culture (.)
            ["UnitCost"] = new AttributeValue { N = s.UnitCost.ToString(CultureInfo.InvariantCulture) },

            // ✅ NEW FIELDS
            ["Vat"] = new AttributeValue { N = s.Vat.ToString(CultureInfo.InvariantCulture) },
            ["QtyOnHandHub"] = new AttributeValue { N = s.QtyOnHandHub.ToString(CultureInfo.InvariantCulture) },
            ["QtyBookedOutForDelivery"] = new AttributeValue { N = s.QtyBookedOutForDelivery.ToString(CultureInfo.InvariantCulture) },

            ["IsActive"] = new AttributeValue { BOOL = s.IsActive },
            ["CreatedAtUtc"] = new AttributeValue { S = s.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture) }
        };

        if (s.SellPrice.HasValue)
        {
            item["SellPrice"] = new AttributeValue
            {
                N = s.SellPrice.Value.ToString(CultureInfo.InvariantCulture)
            };
        }
        else
        {
            item["SellPrice"] = new AttributeValue { NULL = true };
        }

        return item;
    }

    private static Species FromItem(Dictionary<string, AttributeValue> item)
    {
        // Sell price
        decimal? sellPrice =
            item.TryGetValue("SellPrice", out var sp)
            && sp.NULL != true
            && !string.IsNullOrWhiteSpace(sp.N)
                ? decimal.Parse(sp.N, CultureInfo.InvariantCulture)
                : null;

        // NEW fields (backwards compatible if old items don’t have them)
        decimal vat = 0;
        if (item.TryGetValue("Vat", out var vatAttr))
        {
            if (!string.IsNullOrWhiteSpace(vatAttr.N))
                vat = decimal.Parse(vatAttr.N, CultureInfo.InvariantCulture);
            else if (!string.IsNullOrWhiteSpace(vatAttr.S))
                vat = decimal.Parse(vatAttr.S, CultureInfo.InvariantCulture);
        }

        int qtyOnHand = 0;
        if (item.TryGetValue("QtyOnHandHub", out var qoh))
        {
            if (!string.IsNullOrWhiteSpace(qoh.N))
                qtyOnHand = int.Parse(qoh.N, CultureInfo.InvariantCulture);
            else if (!string.IsNullOrWhiteSpace(qoh.S))
                qtyOnHand = int.Parse(qoh.S, CultureInfo.InvariantCulture);
        }

        int qtyBooked = 0;
        if (item.TryGetValue("QtyBookedOutForDelivery", out var qbo))
        {
            if (!string.IsNullOrWhiteSpace(qbo.N))
                qtyBooked = int.Parse(qbo.N, CultureInfo.InvariantCulture);
            else if (!string.IsNullOrWhiteSpace(qbo.S))
                qtyBooked = int.Parse(qbo.S, CultureInfo.InvariantCulture);
        }

        return new Species
        {
            SpeciesId = item["SpeciesId"].S,
            Name = item["Name"].S,

            UnitCost = decimal.Parse(item["UnitCost"].N, CultureInfo.InvariantCulture),
            SellPrice = sellPrice,

            Vat = vat,
            QtyOnHandHub = qtyOnHand,
            QtyBookedOutForDelivery = qtyBooked,

            IsActive = item.TryGetValue("IsActive", out var ia) && ia.BOOL == true,

            CreatedAtUtc = item.TryGetValue("CreatedAtUtc", out var ca)
                ? DateTime.Parse(ca.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : DateTime.UtcNow
        };
    }
}