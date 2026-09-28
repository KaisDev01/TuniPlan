using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAL.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(80).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
        b.Property(x => x.TwoFactorSecretProtected).HasMaxLength(1024);
        b.Property(x => x.PreferredLanguage).HasMaxLength(5);
        b.Property(x => x.AvatarUrl).HasMaxLength(512);
        b.Ignore(x => x.FullName);

        b.HasIndex(x => x.PhoneNumber).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL AND [IsDeleted] = 0");

        b.OwnsOne(x => x.NotificationSettings, ns =>
        {
            ns.Property(p => p.PushEnabled).HasColumnName("Notif_Push");
            ns.Property(p => p.SmsEnabled).HasColumnName("Notif_Sms");
            ns.Property(p => p.WhatsAppEnabled).HasColumnName("Notif_WhatsApp");
            ns.Property(p => p.EmailEnabled).HasColumnName("Notif_Email");
            ns.Property(p => p.Reminder24h).HasColumnName("Notif_Reminder24h");
            ns.Property(p => p.Reminder2h).HasColumnName("Notif_Reminder2h");
        });
        b.Navigation(x => x.NotificationSettings).IsRequired();
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.DeviceName).HasMaxLength(200);
        b.Property(x => x.RevokedReason).HasMaxLength(200);
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VerificationCodeConfiguration : IEntityTypeConfiguration<VerificationCode>
{
    public void Configure(EntityTypeBuilder<VerificationCode> b)
    {
        b.Property(x => x.Target).HasMaxLength(256).IsRequired();
        b.Property(x => x.CodeHash).HasMaxLength(128);
        b.HasIndex(x => new { x.Target, x.Purpose, x.CreatedAt });
    }
}

public class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> b)
    {
        b.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(80).IsRequired();
        b.Property(x => x.Relation).HasMaxLength(40).IsRequired();
        b.Property(x => x.PhoneNumber).HasMaxLength(20);
        b.HasOne(x => x.User).WithMany(u => u.FamilyMembers).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> b)
    {
        b.HasIndex(x => new { x.UserId, x.OrganizationId }).IsUnique();
        b.HasOne(x => x.User).WithMany(u => u.Favorites).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.Details).HasMaxLength(1000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(300);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> b)
    {
        b.Property(x => x.Token).HasMaxLength(200).IsRequired();
        b.Property(x => x.Platform).HasMaxLength(20);
        b.Property(x => x.DeviceName).HasMaxLength(200);
        b.HasIndex(x => x.Token).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}

public class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> b)
    {
        b.Property(x => x.ProviderKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.HasIndex(x => new { x.Provider, x.ProviderKey }).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}
