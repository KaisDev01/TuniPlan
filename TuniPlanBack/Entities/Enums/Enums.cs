using System.ComponentModel;

namespace Entities.Enums;

[Flags]
public enum AccountRoles
{
    None = 0,
    Client = 1,
    Business = 2,
    Admin = 4
}

[Description("Role of a user inside a business.")]
public enum MemberRole
{
    [Description("Full control: settings, team, delete, transfer.")] Owner = 0,
    [Description("Daily work: agenda, requests, clients, reviews, closed periods.")] Staff = 1
}

public enum BusinessCategory
{
    Health = 0,      // Santé
    Beauty = 1,      // Beauté
    Home = 2,        // Maison
    Sport = 3,       // Sport
    Auto = 4,        // Auto
    Pets = 5,        // Animaux
    Legal = 6,       // Juridique
    Creative = 7,    // Créatif
    Education = 8,   // Éducation
    Other = 99
}

/// <summary>slot = doctor/hairdresser, rental = days (car rental), queue = ticket (pharmacy).</summary>
public enum BookingType { Slot = 0, Rental = 1, Queue = 2 }

public enum DepositMode { None = 0, Fixed = 1, Percent = 2 }

[Description("Business verification (tax ID / RNE) reviewed by an admin.")]
public enum VerificationStatus
{
    [Description("Nothing submitted yet.")] NotSubmitted = 0,
    [Description("Documents submitted, waiting for an admin.")] Pending = 1,
    [Description("Approved: the business shows the \"Vérifié\" badge.")] Verified = 2,
    [Description("Refused by an admin: submit again.")] Rejected = 3
}

public enum ResourceType { Person = 0, Room = 1, Vehicle = 2, Equipment = 3 }

public enum AppointmentStatus
{
    Pending = 0,             // waiting for the business
    Confirmed = 1,
    CounterProposed = 2,     // business proposed another time
    Refused = 3,
    CancelledByClient = 4,
    CancelledByBusiness = 5,
    Completed = 6,
    NoShow = 7
}

public enum AppointmentSource { App = 0, AiSecretary = 1, WalkIn = 2, Phone = 3 }

[Description("Deposit payment state of an appointment.")]
public enum PaymentStatus
{
    [Description("No deposit for this appointment.")] NotRequired = 0,
    [Description("Deposit required, waiting for the payment.")] Pending = 1,
    [Description("Deposit paid.")] Paid = 2,
    [Description("Deposit refunded to the client.")] Refunded = 3,
    [Description("Payment failed or cancelled: it can be started again.")] Failed = 4
}

public enum PaymentProvider { Mock = 0, Konnect = 1, Flouci = 2, D17 = 3 }

public enum VerificationPurpose { VerifyPhone = 0, VerifyEmail = 1, ResetPassword = 2, MfaChallenge = 3 }

[Description("Kind of in-app / push notification. Client-side types first, then business-side types (10+).")]
public enum NotificationType
{
    [Description("Any other message (account, verification, review reply, business transfer...).")] General = 0,
    [Description("Client: the business confirmed the booking request.")] RequestAccepted = 1,
    [Description("Client: the request was refused or expired without an answer.")] RequestRefused = 2,
    [Description("Client: the business proposed another time (answer with /counter-offer/respond).")] CounterOffer = 3,
    [Description("Client: reminder 24 h / 2 h before the appointment.")] Reminder = 4,
    [Description("Client: invitation to review a completed appointment.")] ReviewRequest = 5,
    [Description("Business: new booking request, or a client moved an appointment.")] NewRequest = 10,
    [Description("Client or business: an appointment was cancelled by the other side.")] Cancellation = 11,
    [Description("Business: a client posted a review.")] NewReview = 12,
    [Description("Client: a slot became free for a waitlist entry.")] WaitlistSlotFree = 13
}

public enum NotificationChannel { InApp = 0, Push = 1, Sms = 2, WhatsApp = 3, Email = 4 }

public enum AiMessageRole { User = 0, Assistant = 1 }

[Description("Identity provider accepted by POST /api/auth/external.")]
public enum ExternalProvider
{
    [Description("token = Google ID token (JWT) from Google Sign-In / expo-auth-session.")] Google = 0,
    [Description("token = Facebook user access token from Facebook Login.")] Facebook = 1
}
