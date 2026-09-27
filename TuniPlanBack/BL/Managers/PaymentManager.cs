using BL.Interfaces;
using BL.Options;
using Common.Exceptions;
using DAO.Interfaces;
using DTOs.Business;
using Entities;
using Entities.Enums;
using LoggerService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BL.Managers;

public interface IPaymentManager
{
    Task<PaymentDto> InitiateDepositAsync(InitiateDepositRequest request, CancellationToken ct = default);
    Task<PaymentDto> ConfirmMockAsync(ConfirmMockPaymentRequest request, CancellationToken ct = default);
    /// <summary>Refunds the paid deposit of an appointment (changes are saved by the caller).</summary>
    Task RefundAsync(Appointment appointment, CancellationToken ct = default);
}

/// <summary>
/// Deposits (acomptes). Real providers (Konnect, Flouci, D17) plug in through <see cref="IPaymentGateway"/>;
/// the default gateway is a mock that never moves money.
/// </summary>
public sealed class PaymentManager(
    IUnitOfWork uow, ICurrentUser currentUser, IEnumerable<IPaymentGateway> gateways, ILoggerManager logger,
    IOptions<SecurityOptions> securityOptions) : IPaymentManager
{
    public async Task<PaymentDto> InitiateDepositAsync(InitiateDepositRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var a = await uow.Appointments.Query().Include(x => x.Organization).Include(x => x.Service)
                    .FirstOrDefaultAsync(x => x.Id == request.AppointmentId && x.ClientUserId == userId, ct)
                ?? throw new NotFoundException("Rendez-vous introuvable.");
        if (a.DepositAmount <= 0 || a.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.Failed))
            throw new BusinessRuleException("Aucun acompte à payer pour ce rendez-vous.");
        if (!a.IsActiveBooking) throw new BusinessRuleException("Ce rendez-vous n'est plus actif.");

        var gateway = gateways.FirstOrDefault(g => g.Provider == request.Provider)
                      ?? throw new BusinessRuleException("Ce moyen de paiement n'est pas encore disponible.", "provider_unavailable");
        var payment = new Payment { AppointmentId = a.Id, Amount = a.DepositAmount, Provider = request.Provider };
        var init = await gateway.InitiateAsync(payment.Id, payment.Amount, $"Acompte {a.Service.Name} – {a.Organization.Name}", ct);
        payment.ProviderReference = init.ProviderReference;
        await uow.Payments.AddAsync(payment, ct);
        a.PaymentStatus = PaymentStatus.Pending;
        await uow.SaveChangesAsync(ct);
        return ToDto(payment, init.CheckoutUrl);
    }

    public async Task<PaymentDto> ConfirmMockAsync(ConfirmMockPaymentRequest request, CancellationToken ct = default)
    {
        if (!securityOptions.Value.EnableMockPayments) throw new ForbiddenException("Paiements de démonstration désactivés.");
        var userId = currentUser.RequireUserId();
        var payment = await uow.Payments.Query().Include(p => p.Appointment)
                          .FirstOrDefaultAsync(p => p.Id == request.PaymentId && p.Appointment.ClientUserId == userId, ct)
                      ?? throw new NotFoundException("Paiement introuvable.");
        if (payment.Provider != PaymentProvider.Mock) throw new BusinessRuleException("Seuls les paiements de démonstration peuvent être confirmés ainsi.");
        if (payment.Status != PaymentStatus.Pending) return ToDto(payment, null);

        payment.Status = request.Success ? PaymentStatus.Paid : PaymentStatus.Failed;
        payment.PaidAt = request.Success ? DateTime.UtcNow : null;
        payment.Appointment.PaymentStatus = payment.Status;
        await uow.SaveChangesAsync(ct);
        return ToDto(payment, null);
    }

    public async Task RefundAsync(Appointment appointment, CancellationToken ct = default)
    {
        var paid = await uow.Payments.Query()
            .Where(p => p.AppointmentId == appointment.Id && p.Status == PaymentStatus.Paid).ToListAsync(ct);
        foreach (var p in paid)
        {
            var gateway = gateways.FirstOrDefault(g => g.Provider == p.Provider);
            var ok = gateway is not null && await gateway.RefundAsync(p.ProviderReference ?? p.Id.ToString(), p.Amount, ct);
            if (!ok) { logger.LogWarn("Refund failed for payment {PaymentId}", p.Id); continue; }
            p.Status = PaymentStatus.Refunded;
            p.RefundedAt = DateTime.UtcNow;
        }
        if (paid.Count > 0 && paid.All(p => p.Status == PaymentStatus.Refunded)) appointment.PaymentStatus = PaymentStatus.Refunded;
    }

    private static PaymentDto ToDto(Payment p, string? checkoutUrl) =>
        new(p.Id, p.AppointmentId, p.Amount, p.Currency, p.Provider, p.Status, p.ProviderReference, checkoutUrl);
}

/// <summary>Demo gateway: no money moves. The front-end shows a fake payment screen, then calls /payments/mock/confirm.</summary>
public sealed class MockPaymentGateway(IOptions<AppOptions> appOptions) : IPaymentGateway
{
    public PaymentProvider Provider => PaymentProvider.Mock;

    public Task<PaymentInit> InitiateAsync(Guid paymentId, decimal amount, string description, CancellationToken ct = default) =>
        Task.FromResult(new PaymentInit($"{appOptions.Value.PublicWebUrl.TrimEnd('/')}/paiement/demo/{paymentId}", $"MOCK-{paymentId:N}"));

    public Task<bool> RefundAsync(string providerReference, decimal amount, CancellationToken ct = default) => Task.FromResult(true);
}
