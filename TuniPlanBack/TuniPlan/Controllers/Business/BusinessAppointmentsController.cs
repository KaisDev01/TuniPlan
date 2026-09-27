using BL.Interfaces;
using BL.Managers;
using DTOs.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers.Business;

/// <summary>Agenda and requests ("Demandes") of a business.</summary>
[ApiController]
[Route("api/business/organizations/{orgId:guid}/appointments")]
[Authorize(Roles = $"{Roles.Business},{Roles.Admin}")]
public sealed class BusinessAppointmentsController(IAppointmentManager appointments) : ControllerBase
{
    /// <summary>Agenda between two local dates (max 62 days), optionally for one resource or status.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> Agenda(Guid orgId, [FromQuery] AgendaQuery query, CancellationToken ct) =>
        Ok(await appointments.GetAgendaAsync(orgId, query, ct));

    /// <summary>Pending requests waiting for confirmation (including AI secretary requests).</summary>
    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> Requests(Guid orgId, CancellationToken ct) =>
        Ok(await appointments.GetPendingRequestsAsync(orgId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentDto>> Get(Guid orgId, Guid id, CancellationToken ct) =>
        Ok(await appointments.GetForBusinessAsync(orgId, id, ct));

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<AppointmentDto>> Confirm(Guid orgId, Guid id, CancellationToken ct) =>
        Ok(await appointments.ConfirmAsync(orgId, id, ct));

    [HttpPost("{id:guid}/refuse")]
    public async Task<ActionResult<AppointmentDto>> Refuse(Guid orgId, Guid id, RefuseRequest request, CancellationToken ct) =>
        Ok(await appointments.RefuseAsync(orgId, id, request, ct));

    /// <summary>Propose another time ("contre-proposition").</summary>
    [HttpPost("{id:guid}/counter-offer")]
    public async Task<ActionResult<AppointmentDto>> CounterOffer(Guid orgId, Guid id, CounterOfferRequest request, CancellationToken ct) =>
        Ok(await appointments.CounterOfferAsync(orgId, id, request, ct));

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<AppointmentDto>> Complete(Guid orgId, Guid id, CancellationToken ct) =>
        Ok(await appointments.CompleteAsync(orgId, id, ct));

    [HttpPost("{id:guid}/no-show")]
    public async Task<ActionResult<AppointmentDto>> NoShow(Guid orgId, Guid id, CancellationToken ct) =>
        Ok(await appointments.MarkNoShowAsync(orgId, id, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<AppointmentDto>> Cancel(Guid orgId, Guid id, CancelAppointmentRequest request, CancellationToken ct) =>
        Ok(await appointments.CancelByBusinessAsync(orgId, id, request, ct));

    [HttpPost("{id:guid}/reschedule")]
    public async Task<ActionResult<AppointmentDto>> Reschedule(Guid orgId, Guid id, RescheduleRequest request, CancellationToken ct) =>
        Ok(await appointments.RescheduleByBusinessAsync(orgId, id, request, ct));

    [HttpPut("{id:guid}/note")]
    public async Task<ActionResult<AppointmentDto>> Note(Guid orgId, Guid id, BusinessNoteRequest request, CancellationToken ct) =>
        Ok(await appointments.UpdateBusinessNoteAsync(orgId, id, request, ct));

    /// <summary>Quick add for walk-in or phone clients.</summary>
    [HttpPost("walk-in")]
    public async Task<ActionResult<AppointmentDto>> WalkIn(Guid orgId, WalkInRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await appointments.CreateWalkInAsync(orgId, request, ct));
}
