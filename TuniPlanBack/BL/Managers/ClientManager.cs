using BL.Mapping;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Business;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace BL.Managers;

/// <summary>Client files (CRM) of a business.</summary>
public interface IClientManager
{
    Task<PagedResult<ClientSummaryDto>> ListAsync(Guid organizationId, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ClientDetailsDto> GetAsync(Guid organizationId, Guid clientId, CancellationToken ct = default);
    Task<ClientSummaryDto> CreateAsync(Guid organizationId, CreateClientRequest request, CancellationToken ct = default);
    Task<ClientDetailsDto> UpdateAsync(Guid organizationId, Guid clientId, UpdateClientRequest request, CancellationToken ct = default);

    /// <summary>Finds or creates (not saved) the CRM record of a client for an organization.</summary>
    Task<OrganizationClient> EnsureClientAsync(Guid organizationId, User? user, string? displayName, string? phone, CancellationToken ct = default);
}

public sealed class ClientManager(IUnitOfWork uow, IOrganizationAccess access, IClock clock) : IClientManager
{
    public async Task<PagedResult<ClientSummaryDto>> ListAsync(Guid organizationId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var q = uow.OrganizationClients.QueryNoTracking().Where(c => c.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(c => c.DisplayName.Contains(term) || (c.PhoneNumber != null && c.PhoneNumber.Contains(term)));
        }
        var total = await q.CountAsync(ct);
        var clients = await q.OrderBy(c => c.DisplayName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var stats = await StatsAsync(organizationId, clients.Select(c => c.Id).ToList(), ct);
        return new PagedResult<ClientSummaryDto>(clients.Select(c => ToSummary(c, stats)).ToList(), page, pageSize, total);
    }

    public async Task<ClientDetailsDto> GetAsync(Guid organizationId, Guid clientId, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var client = await uow.OrganizationClients.QueryNoTracking().FirstOrDefaultAsync(c => c.Id == clientId && c.OrganizationId == organizationId, ct)
                     ?? throw new NotFoundException("Client introuvable.");
        var stats = await StatsAsync(organizationId, [client.Id], ct);
        var history = await uow.Appointments.QueryWithDetails().AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.OrganizationClientId == clientId)
            .OrderByDescending(a => a.StartUtc).Take(100).ToListAsync(ct);
        var now = clock.UtcNow;
        return new ClientDetailsDto
        {
            Summary = ToSummary(client, stats),
            PrivateNotes = client.PrivateNotes,
            History = history.Select(a => a.ToDto(now, forBusiness: true)).ToList()
        };
    }

    public async Task<ClientSummaryDto> CreateAsync(Guid organizationId, CreateClientRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null
            : PhoneNumberHelper.NormalizeTunisian(request.PhoneNumber) ?? throw ValidationException.For(nameof(request.PhoneNumber), "Numéro invalide.");
        if (phone is not null && await uow.OrganizationClients.AnyAsync(c => c.OrganizationId == organizationId && c.PhoneNumber == phone, ct))
            throw new ConflictException("Un client avec ce numéro existe déjà.");
        var user = phone is null ? null : await uow.Users.GetByPhoneAsync(phone, ct);
        var client = new OrganizationClient { OrganizationId = organizationId, DisplayName = request.DisplayName.Trim(), PhoneNumber = phone, UserId = user?.Id };
        await uow.OrganizationClients.AddAsync(client, ct);
        await uow.SaveChangesAsync(ct);
        return ToSummary(client, new Dictionary<Guid, ClientStats>());
    }

    public async Task<ClientDetailsDto> UpdateAsync(Guid organizationId, Guid clientId, UpdateClientRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var client = await uow.OrganizationClients.FirstOrDefaultAsync(c => c.Id == clientId && c.OrganizationId == organizationId, ct)
                     ?? throw new NotFoundException("Client introuvable.");
        client.PrivateNotes = request.PrivateNotes?.Trim();
        client.Tags = request.Tags.Select(t => t.Trim()).Where(t => t.Length is > 0 and <= 30).Distinct().Take(10).ToList();
        client.IsBlocked = request.IsBlocked;
        await uow.SaveChangesAsync(ct);
        return await GetAsync(organizationId, clientId, ct);
    }

    public async Task<OrganizationClient> EnsureClientAsync(Guid organizationId, User? user, string? displayName, string? phone, CancellationToken ct = default)
    {
        OrganizationClient? client = null;
        if (user is not null)
            client = await uow.OrganizationClients.FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.UserId == user.Id, ct);
        var normalizedPhone = user?.PhoneNumber ?? PhoneNumberHelper.NormalizeTunisian(phone);
        if (client is null && normalizedPhone is not null)
            client = await uow.OrganizationClients.FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.PhoneNumber == normalizedPhone, ct);
        if (client is not null)
        {
            if (user is not null && client.UserId is null) client.UserId = user.Id;
            return client;
        }
        client = new OrganizationClient
        {
            OrganizationId = organizationId,
            UserId = user?.Id,
            DisplayName = user?.FullName ?? displayName?.Trim() ?? "Client",
            PhoneNumber = normalizedPhone,
            Tags = ["nouveau"]
        };
        await uow.OrganizationClients.AddAsync(client, ct);
        return client;
    }

    private sealed record ClientStats(int Visits, DateTime? LastVisit, int NoShows, decimal Spent);

    private async Task<Dictionary<Guid, ClientStats>> StatsAsync(Guid organizationId, List<Guid> clientIds, CancellationToken ct)
    {
        if (clientIds.Count == 0) return new();
        var rows = await uow.Appointments.QueryNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.OrganizationClientId != null && clientIds.Contains(a.OrganizationClientId.Value))
            .Select(a => new { ClientId = a.OrganizationClientId!.Value, a.Status, a.StartUtc, a.Price })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ClientId).ToDictionary(g => g.Key, g => new ClientStats(
            g.Count(r => r.Status == AppointmentStatus.Completed),
            g.Where(r => r.Status == AppointmentStatus.Completed).Select(r => (DateTime?)r.StartUtc).Max(),
            g.Count(r => r.Status == AppointmentStatus.NoShow),
            g.Where(r => r.Status == AppointmentStatus.Completed).Sum(r => r.Price)));
    }

    private static ClientSummaryDto ToSummary(OrganizationClient c, IReadOnlyDictionary<Guid, ClientStats> stats)
    {
        stats.TryGetValue(c.Id, out var s);
        return new ClientSummaryDto
        {
            Id = c.Id, UserId = c.UserId, DisplayName = c.DisplayName, PhoneNumber = c.PhoneNumber,
            TotalVisits = s?.Visits ?? 0, LastVisitUtc = s?.LastVisit, NoShowCount = s?.NoShows ?? 0, TotalSpent = s?.Spent ?? 0,
            Tags = c.Tags, IsBlocked = c.IsBlocked
        };
    }
}
