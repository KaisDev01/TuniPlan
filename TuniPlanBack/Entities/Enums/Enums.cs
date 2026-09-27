namespace Entities.Enums;

[Flags]
public enum AccountRoles
{
    None = 0,
    Client = 1,
    Business = 2,
    Admin = 4
}

public enum MemberRole { Owner = 0, Staff = 1 }

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

public enum VerificationStatus { NotSubmitted = 0, Pending = 1, Verified = 2, Rejected = 3 }

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

public enum PaymentStatus { NotRequired = 0, Pending = 1, Paid = 2, Refunded = 3, Failed = 4 }

public enum PaymentProvider { Mock = 0, Konnect = 1, Flouci = 2, D17 = 3 }

public enum VerificationPurpose { VerifyPhone = 0, VerifyEmail = 1, ResetPassword = 2, MfaChallenge = 3 }

public enum NotificationType
{
    General = 0,
    RequestAccepted = 1,
    RequestRefused = 2,
    CounterOffer = 3,
    Reminder = 4,
    ReviewRequest = 5,
    NewRequest = 10,
    Cancellation = 11,
    NewReview = 12,
    WaitlistSlotFree = 13
}

public enum NotificationChannel { InApp = 0, Push = 1, Sms = 2, WhatsApp = 3, Email = 4 }

public enum AiMessageRole { User = 0, Assistant = 1 }
