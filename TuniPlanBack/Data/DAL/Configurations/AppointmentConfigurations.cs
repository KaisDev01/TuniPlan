using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAL.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.Property(x => x.Price).HasPrecision(10, 3);
        b.Property(x => x.DiscountPercent).HasPrecision(5, 2);
        b.Property(x => x.DepositAmount).HasPrecision(10, 3);
        b.Property(x => x.ClientNote).HasMaxLength(1000);
        b.Property(x => x.BusinessNote).HasMaxLength(2000);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.Property(x => x.ProposalMessage).HasMaxLength(500);
        b.Property(x => x.AiSummary).HasMaxLength(2000);
        b.Property(x => x.VoiceNoteTranscript).HasMaxLength(4000);
        b.Property(x => x.VoiceNoteSummary).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.IsActiveBooking);

        b.HasIndex(x => new { x.OrganizationId, x.StartUtc });
        b.HasIndex(x => new { x.ResourceId, x.StartUtc });
        b.HasIndex(x => new { x.ClientUserId, x.StartUtc });
        b.HasIndex(x => new { x.Status, x.StartUtc });

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
        b.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId);
        b.HasOne(x => x.ClientUser).WithMany().HasForeignKey(x => x.ClientUserId);
        b.HasOne(x => x.FamilyMember).WithMany().HasForeignKey(x => x.FamilyMemberId);
        b.HasOne(x => x.OrganizationClient).WithMany().HasForeignKey(x => x.OrganizationClientId);
    }
}

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.Property(x => x.Comment).HasMaxLength(2000).IsRequired();
        b.Property(x => x.OwnerReply).HasMaxLength(2000);
        b.Property(x => x.ReportReason).HasMaxLength(500);
        // One live review per appointment: a deleted review lets the client write a new one
        b.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        b.HasOne(x => x.Appointment).WithOne(a => a.Review).HasForeignKey<Review>(x => x.AppointmentId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.ClientUser).WithMany().HasForeignKey(x => x.ClientUserId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.Amount).HasPrecision(10, 3);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.ProviderReference).HasMaxLength(200);
        b.HasOne(x => x.Appointment).WithMany(a => a.Payments).HasForeignKey(x => x.AppointmentId);
    }
}

public class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> b)
    {
        b.HasIndex(x => new { x.OrganizationId, x.Date, x.NotifiedAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
    }
}

public class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> b)
    {
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}

public class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> b)
    {
        b.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        b.HasOne(x => x.Conversation).WithMany(c => c.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
