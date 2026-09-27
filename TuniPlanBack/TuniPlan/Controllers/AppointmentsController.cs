using BL.Managers;
using DTOs.Appointments;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers;

/// <summary>Client side: book, see, cancel, move appointments; answer counter-offers.</summary>
[ApiController]
[Route("api/appointments")]
public sealed class AppointmentsController(IAppointmentManager appointments) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AppointmentDto>> Book(BookAppointmentRequest request, CancellationToken ct)
    {
        var result = await appointments.BookAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>scope = upcoming | past | all</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> Mine([FromQuery] MyAppointmentsQuery query, CancellationToken ct) =>
        Ok(await appointments.GetMineAsync(query, ct));

    [HttpGet("to-review")]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> ToReview(CancellationToken ct) => Ok(await appointments.GetToReviewAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentDto>> Get(Guid id, CancellationToken ct) => Ok(await appointments.GetMineByIdAsync(id, ct));

    /// <summary>"Ajouter à mon calendrier" (.ics file).</summary>
    [HttpGet("{id:guid}/calendar.ics")]
    public async Task<IActionResult> Calendar(Guid id, CancellationToken ct) =>
        File(System.Text.Encoding.UTF8.GetBytes(await appointments.GetIcsAsync(id, ct)), "text/calendar", $"tuniplan-{id:N}.ics");

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<AppointmentDto>> Cancel(Guid id, CancelAppointmentRequest request, CancellationToken ct) =>
        Ok(await appointments.CancelByClientAsync(id, request, ct));

    [HttpPost("{id:guid}/reschedule")]
    public async Task<ActionResult<AppointmentDto>> Reschedule(Guid id, RescheduleRequest request, CancellationToken ct) =>
        Ok(await appointments.RescheduleAsync(id, request, ct));

    [HttpPost("{id:guid}/counter-offer/respond")]
    public async Task<ActionResult<AppointmentDto>> RespondCounterOffer(Guid id, RespondCounterOfferRequest request, CancellationToken ct) =>
        Ok(await appointments.RespondCounterOfferAsync(id, request, ct));
}

/// <summary>"M'avertir si un créneau se libère".</summary>
[ApiController]
[Route("api/waitlist")]
public sealed class WaitlistController(IAppointmentManager appointments) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WaitlistEntryDto>> Join(JoinWaitlistRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await appointments.JoinWaitlistAsync(request, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WaitlistEntryDto>>> Mine(CancellationToken ct) => Ok(await appointments.GetMyWaitlistAsync(ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        await appointments.LeaveWaitlistAsync(id, ct);
        return NoContent();
    }
}
