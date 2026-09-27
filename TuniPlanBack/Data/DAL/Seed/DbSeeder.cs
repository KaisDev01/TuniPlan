using Common.Helpers;
using Common.Security;
using DAL.Context;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Seed;

/// <summary>
/// Demo data matching the TuniPlan front-end prototype (same demo accounts).
/// Only runs when the database has no users. Password for every demo account: TuniPlan#2026
/// </summary>
public sealed class DbSeeder(TuniPlanDbContext db, IPasswordHasher hasher)
{
    public const string DemoPassword = "TuniPlan#2026";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Users.IgnoreQueryFilters().AnyAsync(ct)) return;

        var hash = hasher.Hash(DemoPassword);
        User NewUser(string first, string last, string email, string phone, AccountRoles roles) => new()
        {
            FirstName = first, LastName = last, Email = email, EmailConfirmed = true,
            PhoneNumber = phone, PhoneConfirmed = true, PasswordHash = hash, Roles = roles
        };

        var admin = NewUser("Admin", "TuniPlan", "admin@tuniplan.demo", "+21620000000", AccountRoles.Admin | AccountRoles.Business | AccountRoles.Client);
        var kais = NewUser("Kais", "Client", "kais@tuniplan.demo", "+21622000001", AccountRoles.Client);
        var olfa = NewUser("Olfa", "Gharbi", "olfagharbi@tuniplan.demo", "+21698000001", AccountRoles.Business | AccountRoles.Client);
        var voit = NewUser("Voit25", "Location", "voit25@tuniplan.demo", "+21698000002", AccountRoles.Business);
        var salma = NewUser("Salma", "Karray", "salmakarray@tuniplan.demo", "+21698000003", AccountRoles.Business);
        var wifak = NewUser("Pharmacie", "El Wifak", "pharmawifak@tuniplan.demo", "+21698000004", AccountRoles.Business);
        var lilia = NewUser("Lilia", "Maison", "maisonlilia@tuniplan.demo", "+21698000005", AccountRoles.Business);
        db.Users.AddRange(admin, kais, olfa, voit, salma, wifak, lilia);

        var cabinet = Org(olfa, "Cabinet Dr. Olfa Gharbi", BusinessCategory.Health, "Médecine générale", "Sousse", "Sousse",
            "12 avenue Habib Bourguiba, Sousse", "Médecin généraliste. Consultations adultes et enfants, suivi des maladies chroniques, certificats médicaux.",
            BookingType.Slot, manual: true);
        AddService(cabinet, "Consultation générale", 20, 50);
        AddService(cabinet, "Consultation enfant", 20, 45);
        AddService(cabinet, "Certificat médical", 10, 30);
        AddResource(cabinet, "Dr. Olfa Gharbi", "Médecin généraliste", ResourceType.Person);

        var karim = Org(admin, "Cabinet Dr. Karim Ben Ammar", BusinessCategory.Health, "Cardiologie", "Tunis", "La Marsa",
            "24 avenue Taieb Mhiri, La Marsa", "Cardiologue : consultation, ECG, échographie cardiaque.", BookingType.Slot, manual: false);
        AddService(karim, "Consultation cardiologie", 30, 80);
        AddService(karim, "ECG", 15, 40);

        var maison = Org(lilia, "Maison Lilia", BusinessCategory.Beauty, "Salon de coiffure", "Tunis", "Lac 2",
            "18 rue du Lac Turkana, Les Berges du Lac 2", "Coupe, couleur et soins dans un salon lumineux au Lac 2.", BookingType.Slot, manual: true);
        var lina = AddResource(maison, "Lina", "Coiffeuse coloriste", ResourceType.Person);
        var sarra = AddResource(maison, "Sarra", "Coiffeuse", ResourceType.Person);
        AddService(maison, "Brushing", 45, 25, lina, sarra);
        AddService(maison, "Coupe femme", 60, 40, lina, sarra);
        AddService(maison, "Coloration", 120, 90, lina);

        var rental = Org(voit, "Voit25 Location", BusinessCategory.Auto, "Location de voitures", "Tunis", "Tunis",
            "Rue de Marseille, Tunis", "Location de voitures récentes à la journée.", BookingType.Rental, manual: true);
        var clio = AddResource(rental, "Renault Clio 5", "Citadine · Manuelle", ResourceType.Vehicle);
        var i20 = AddResource(rental, "Hyundai i20", "Citadine · Automatique", ResourceType.Vehicle);
        AddService(rental, "Location à la journée", 1440, 120, clio, i20);
        rental.DepositMode = DepositMode.Fixed;
        rental.DepositValue = 100;

        var legal = Org(salma, "Cabinet Me. Salma Karray", BusinessCategory.Legal, "Avocate", "Sfax", "Sfax",
            "Avenue Hedi Chaker, Sfax", "Avocate : droit de la famille, droit des affaires, conseil juridique.", BookingType.Slot, manual: true);
        AddService(legal, "Consultation juridique", 60, 120);
        AddService(legal, "Rendez-vous de suivi", 30, 60);

        var pharma = Org(wifak, "Pharmacie El Wifak", BusinessCategory.Health, "Pharmacie", "Ariana", "Ennasr",
            "Avenue de l'Ere Nouvelle, Ennasr", "Pharmacie : retrait d'ordonnances, conseils, prise de tension.", BookingType.Queue, manual: false);
        AddService(pharma, "Retrait d'ordonnance", 10, 0);
        AddService(pharma, "Prise de tension", 10, 5);

        await db.SaveChangesAsync(ct);

        // A few completed appointments with verified reviews for the first cabinet
        var consult = cabinet.Services.First();
        var reviews = new (int Rating, string Comment)[]
        {
            (5, "Médecin très à l'écoute, rendez-vous à l'heure. Je recommande."),
            (5, "Excellente prise en charge, cabinet propre et accueil chaleureux."),
            (4, "Très bien, un peu d'attente mais consultation sérieuse.")
        };
        var day = 3;
        foreach (var (rating, comment) in reviews)
        {
            var start = DateTime.UtcNow.Date.AddDays(-day++).AddHours(8);
            var appt = new Appointment
            {
                OrganizationId = cabinet.Id, ServiceId = consult.Id, ClientUserId = kais.Id,
                StartUtc = start, EndUtc = start.AddMinutes(consult.DurationMinutes),
                Status = AppointmentStatus.Completed, CompletedAt = start.AddMinutes(30), Price = consult.Price
            };
            db.Appointments.Add(appt);
            db.Reviews.Add(new Review
            {
                OrganizationId = cabinet.Id, AppointmentId = appt.Id, ClientUserId = kais.Id,
                Rating = rating, RatingWelcome = rating, RatingPunctuality = rating, RatingQuality = rating, RatingValue = rating,
                Comment = comment
            });
        }
        cabinet.RatingAverage = reviews.Average(r => r.Rating);
        cabinet.ReviewCount = reviews.Length;
        await db.SaveChangesAsync(ct);
    }

    private Organization Org(User ownerUser, string name, BusinessCategory category, string sub, string governorate, string city,
        string address, string description, BookingType bookingType, bool manual, bool owner = true)
    {
        var org = new Organization
        {
            Name = name, Slug = SlugHelper.Slugify(name), Category = category, Subcategory = sub,
            Governorate = governorate, City = city, Address = address, Description = description,
            BookingType = bookingType, ManualValidationRequired = manual, IsPublished = true, PublishedAt = DateTime.UtcNow,
            VerificationStatus = VerificationStatus.Verified, Phone = ownerUser.PhoneNumber, WhatsApp = ownerUser.PhoneNumber
        };
        foreach (var d in Enum.GetValues<DayOfWeek>())
        {
            org.OpeningHours.Add(new OpeningHour
            {
                DayOfWeek = d,
                IsClosed = d == DayOfWeek.Sunday,
                OpenTime = new TimeOnly(9, 0),
                CloseTime = d == DayOfWeek.Saturday ? new TimeOnly(13, 0) : new TimeOnly(18, 0),
                BreakStart = d == DayOfWeek.Saturday ? null : new TimeOnly(13, 0),
                BreakEnd = d == DayOfWeek.Saturday ? null : new TimeOnly(14, 0)
            });
        }
        org.Members.Add(new OrganizationMember { UserId = ownerUser.Id, Role = owner ? MemberRole.Owner : MemberRole.Staff });
        db.Organizations.Add(org);
        return org;
    }

    private static Resource AddResource(Organization org, string name, string title, ResourceType type)
    {
        var r = new Resource { OrganizationId = org.Id, Name = name, Title = title, Type = type };
        org.Resources.Add(r);
        return r;
    }

    private static void AddService(Organization org, string name, int minutes, decimal price, params Resource[] resources)
    {
        var s = new Service { OrganizationId = org.Id, Name = name, DurationMinutes = minutes, Price = price, SortOrder = org.Services.Count };
        foreach (var r in resources) s.ServiceResources.Add(new ServiceResource { ServiceId = s.Id, ResourceId = r.Id });
        org.Services.Add(s);
    }
}
