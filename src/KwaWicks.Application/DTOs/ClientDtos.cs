using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.DTOs;

public class CreateClientRequest
{
    public string ClientName { get; set; } = string.Empty;
    public string ClientAddress { get; set; } = string.Empty;
    public string ClientCity { get; set; } = string.Empty;
    public string ClientProvince { get; set; } = string.Empty;
    public string ClientPostalCode { get; set; } = string.Empty;
    public string ClientContactDetails { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public ClientType ClientType { get; set; } = ClientType.CODCASH;
    public bool IsWalkIn { get; set; } = false;
}

public class UpdateClientRequest
{
    public string ClientName { get; set; } = string.Empty;
    public string ClientAddress { get; set; } = string.Empty;
    public string ClientCity { get; set; } = string.Empty;
    public string ClientProvince { get; set; } = string.Empty;
    public string ClientPostalCode { get; set; } = string.Empty;
    public string ClientContactDetails { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public ClientType ClientType { get; set; } = ClientType.CODCASH;
    public bool IsWalkIn { get; set; } = false;
}

public class ClientDto
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ClientAddress { get; set; } = string.Empty;
    public string ClientCity { get; set; } = string.Empty;
    public string ClientProvince { get; set; } = string.Empty;
    public string ClientPostalCode { get; set; } = string.Empty;
    public string ClientContactDetails { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public ClientType ClientType { get; set; }
    public bool IsWalkIn { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
