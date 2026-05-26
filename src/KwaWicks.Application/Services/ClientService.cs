using KwaWicks.Application.DTOs;
using KwaWicks.Application.Interfaces;
using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Services;

public class ClientService : IClientService
{
    private readonly IClientRepository _repo;

    public ClientService(IClientRepository repo)
    {
        _repo = repo;
    }

    public async Task<ClientDto> CreateAsync(CreateClientRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientName))
            throw new ArgumentException("ClientName is required.");

        var now = DateTime.UtcNow;

        var client = new Client
        {
            ClientId = Guid.NewGuid().ToString("N"),
            ClientName = request.ClientName.Trim(),
            ClientAddress = request.ClientAddress?.Trim() ?? "",
            ClientCity = request.ClientCity?.Trim() ?? "",
            ClientProvince = request.ClientProvince?.Trim() ?? "",
            ClientPostalCode = request.ClientPostalCode?.Trim() ?? "",
            ClientContactDetails = request.ClientContactDetails?.Trim() ?? "",
            ClientPhone = request.ClientPhone?.Trim() ?? "",
            ClientType = request.ClientType,
            IsWalkIn = request.IsWalkIn,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _repo.PutAsync(client, ct);
        return Map(client);
    }

    public async Task<ClientDto?> GetByIdAsync(string clientId, CancellationToken ct = default)
    {
        var client = await _repo.GetAsync(clientId, ct);
        return client is null ? null : Map(client);
    }

    public async Task<List<ClientDto>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        var clients = await _repo.ListAsync(limit, ct);
        return clients.Select(Map).ToList();
    }

    public async Task<ClientDto?> UpdateAsync(string clientId, UpdateClientRequest request, CancellationToken ct = default)
    {
        var existing = await _repo.GetAsync(clientId, ct);
        if (existing is null) return null;

        existing.ClientName = string.IsNullOrWhiteSpace(request.ClientName) ? existing.ClientName : request.ClientName.Trim();
        existing.ClientAddress = request.ClientAddress?.Trim() ?? "";
        existing.ClientCity = request.ClientCity?.Trim() ?? "";
        existing.ClientProvince = request.ClientProvince?.Trim() ?? "";
        existing.ClientPostalCode = request.ClientPostalCode?.Trim() ?? "";
        existing.ClientContactDetails = request.ClientContactDetails?.Trim() ?? "";
        existing.ClientPhone = request.ClientPhone?.Trim() ?? "";
        existing.ClientType = request.ClientType;
        existing.IsWalkIn = request.IsWalkIn;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _repo.PutAsync(existing, ct);
        return Map(existing);
    }

    public async Task<bool> DeleteAsync(string clientId, CancellationToken ct = default)
        => await _repo.DeleteAsync(clientId, ct);

    public async Task PatchPhoneAsync(string clientId, string phone, CancellationToken ct = default)
    {
        var existing = await _repo.GetAsync(clientId, ct);
        if (existing is null) return;
        existing.ClientPhone = phone.Trim();
        existing.UpdatedAtUtc = DateTime.UtcNow;
        await _repo.PutAsync(existing, ct);
    }

    private static ClientDto Map(Client c) => new()
    {
        ClientId = c.ClientId,
        ClientName = c.ClientName,
        ClientAddress = c.ClientAddress,
        ClientCity = c.ClientCity,
        ClientProvince = c.ClientProvince,
        ClientPostalCode = c.ClientPostalCode,
        ClientContactDetails = c.ClientContactDetails,
        ClientPhone = c.ClientPhone,
        ClientType = c.ClientType,
        IsWalkIn = c.IsWalkIn,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc
    };
}
