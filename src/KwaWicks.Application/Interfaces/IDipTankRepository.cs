using KwaWicks.Domain.Entities;

namespace KwaWicks.Application.Interfaces;

public interface IDipTankRepository
{
    Task<DipTank> CreateTankAsync(DipTank tank, CancellationToken ct);
    Task<DipTank?> GetTankAsync(string tankId, CancellationToken ct);
    Task<List<DipTank>> ListTanksAsync(CancellationToken ct);
    Task<DipReading> CreateReadingAsync(DipReading reading, CancellationToken ct);
    Task<List<DipReading>> ListReadingsAsync(CancellationToken ct);
    Task<List<DipReading>> ListReadingsByTankAsync(string tankId, CancellationToken ct);
}
