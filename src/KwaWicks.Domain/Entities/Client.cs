namespace KwaWicks.Domain.Entities;

public enum ClientType
{
    CODCASH = 0,
    CODEFT = 1,
    Credit = 2,
}

public class Client
{
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");

    public string ClientName { get; set; } = string.Empty;
    public string ClientAddress { get; set; } = string.Empty;
    public string ClientCity { get; set; } = string.Empty;
    public string ClientProvince { get; set; } = string.Empty;
    public string ClientPostalCode { get; set; } = string.Empty;

    // keep it flexible: phone/email/notes etc
    public string ClientContactDetails { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;

    public ClientType ClientType { get; set; } = ClientType.CODCASH;
    public bool IsWalkIn { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}