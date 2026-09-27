using AIL;
using BL.Interfaces;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Appointments;
using DTOs.Business;
using DTOs.Catalog;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace BL.Managers;

public interface IAiSecretaryManager
{
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default);
    Task<AppointmentDto> CreateRequestAsync(AiCreateRequestRequest request, CancellationToken ct = default);
}

/// <summary>
/// Conversational booking assistant. The AI reads services and availability and proposes slots,
/// but it NEVER confirms: <see cref="CreateRequestAsync"/> always creates a pending request for the owner.
/// </summary>
public sealed class AiSecretaryManager(
    IUnitOfWork uow, ICurrentUser currentUser, ISecretaryAgent agent, IAvailabilityManager availability,
    IAppointmentManager appointments, IClock clock) : IAiSecretaryManager
{
    private const int MaxMessagesPerConversation = 40;

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var org = await uow.Organizations.QueryNoTracking().Include(o => o.OpeningHours)
                      .FirstOrDefaultAsync(o => o.Id == request.OrganizationId && o.IsPublished, ct)
                  ?? throw new NotFoundException("Entreprise introuvable.");
        var services = await uow.Services.GetByOrganizationAsync(org.Id, activeOnly: true, ct);
        if (services.Count == 0) throw new BusinessRuleException("Cette entreprise n'a pas encore de prestations.");

        AiConversation conversation;
        if (request.ConversationId is { } cid)
        {
            conversation = await uow.AiConversations.Query().Include(c => c.Messages)
                               .FirstOrDefaultAsync(c => c.Id == cid && c.UserId == userId && c.OrganizationId == org.Id, ct)
                           ?? throw new NotFoundException("Conversation introuvable.");
            if (conversation.Messages.Count >= MaxMessagesPerConversation)
                throw new BusinessRuleException("Conversation trop longue : commencez-en une nouvelle.");
        }
        else
        {
            conversation = new AiConversation { OrganizationId = org.Id, UserId = userId };
            await uow.AiConversations.AddAsync(conversation, ct);
        }

        var history = conversation.Messages.OrderBy(m => m.CreatedAt)
            .Select(m => new SecretaryTurn(m.Role == AiMessageRole.User, m.Content)).ToList();

        // Service: explicit match in this message, otherwise the one matched earlier in the conversation, otherwise the first
        var aiServices = services.Select(s => new AiService(s.Id, s.Name, s.DurationMinutes, s.Price)).ToList();
        var matchedId = agent.MatchService(request.Message, aiServices)
                        ?? history.Where(h => h.FromUser).Select(h => agent.MatchService(h.Content, aiServices)).LastOrDefault(id => id is not null);
        var service = services.FirstOrDefault(s => s.Id == matchedId) ?? services[0];

        var slots = await availability.GetNextSlotsAsync(org, service, 40, 14, null, ct);
        var tz = TimeZoneHelper.Find(org.TimeZoneId);
        var aiSlots = slots.Select((s, i) => new AiSlot(i, s.StartUtc, TimeZoneHelper.ToLocal(s.StartUtc, tz), s.ResourceIds.Count > 0 ? s.ResourceIds[0] : (Guid?)null)).ToList();

        var context = new SecretaryContext(org.Name, org.Description, org.ManualValidationRequired, aiServices,
            aiServices.First(s => s.Id == service.Id), aiSlots, DateOnly.FromDateTime(TimeZoneHelper.ToLocal(clock.UtcNow, tz)));
        var result = await agent.RespondAsync(context, history, request.Message, ct);

        conversation.Messages.Add(new AiMessage { ConversationId = conversation.Id, Role = AiMessageRole.User, Content = request.Message.Trim() });
        conversation.Messages.Add(new AiMessage { ConversationId = conversation.Id, Role = AiMessageRole.Assistant,
            Content = result.Reply.Length > 4000 ? result.Reply[..4000] : result.Reply });
        await uow.SaveChangesAsync(ct);

        var proposed = result.ProposedSlotIndexes.Select(i => slots[i]).ToList();
        return new AiChatResponse(conversation.Id, result.Reply, proposed, result.SuggestedServiceId ?? service.Id);
    }

    public async Task<AppointmentDto> CreateRequestAsync(AiCreateRequestRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var conversation = await uow.AiConversations.Query().Include(c => c.Messages)
                               .FirstOrDefaultAsync(c => c.Id == request.ConversationId && c.UserId == userId, ct)
                           ?? throw new NotFoundException("Conversation introuvable.");

        var summary = "Échange avec la secrétaire IA : " + string.Join(" | ",
            conversation.Messages.Where(m => m.Role == AiMessageRole.User).OrderBy(m => m.CreatedAt).Select(m => m.Content).TakeLast(5));
        if (summary.Length > 2000) summary = summary[..2000];

        var appointment = await appointments.BookFromAiAsync(new BookAppointmentRequest
        {
            OrganizationId = conversation.OrganizationId,
            ServiceId = request.ServiceId,
            ResourceId = request.ResourceId,
            StartUtc = request.StartUtc
        }, summary, ct);

        conversation.DraftAppointmentId = appointment.Id;
        await uow.SaveChangesAsync(ct);
        return appointment;
    }
}
