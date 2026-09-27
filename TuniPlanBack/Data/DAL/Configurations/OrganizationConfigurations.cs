using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAL.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.Property(x => x.Subcategory).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Governorate).HasMaxLength(50).IsRequired();
        b.Property(x => x.City).HasMaxLength(80).IsRequired();
        b.Property(x => x.Address).HasMaxLength(250).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(64);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.WhatsApp).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.LogoUrl).HasMaxLength(512);
        b.Property(x => x.CoverUrl).HasMaxLength(512);
        b.Property(x => x.ReminderTemplate).HasMaxLength(500);
        b.Property(x => x.TaxId).HasMaxLength(50);
        b.Property(x => x.RneNumber).HasMaxLength(50);
        b.Property(x => x.DepositValue).HasPrecision(10, 3);

        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.IsPublished, x.Category, x.Governorate });
    }
}

public class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> b)
    {
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        b.HasOne(x => x.Organization).WithMany(o => o.Members).HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.User).WithMany(u => u.Memberships).HasForeignKey(x => x.UserId);
    }
}

public class OrganizationPhotoConfiguration : IEntityTypeConfiguration<OrganizationPhoto>
{
    public void Configure(EntityTypeBuilder<OrganizationPhoto> b)
    {
        b.Property(x => x.Url).HasMaxLength(512).IsRequired();
        b.HasOne(x => x.Organization).WithMany(o => o.Photos).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class OpeningHourConfiguration : IEntityTypeConfiguration<OpeningHour>
{
    public void Configure(EntityTypeBuilder<OpeningHour> b)
    {
        b.HasIndex(x => new { x.OrganizationId, x.DayOfWeek }).IsUnique();
        b.HasOne(x => x.Organization).WithMany(o => o.OpeningHours).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ClosedPeriodConfiguration : IEntityTypeConfiguration<ClosedPeriod>
{
    public void Configure(EntityTypeBuilder<ClosedPeriod> b)
    {
        b.Property(x => x.Reason).HasMaxLength(200);
        b.HasIndex(x => new { x.OrganizationId, x.StartUtc, x.EndUtc });
        b.HasOne(x => x.Organization).WithMany(o => o.ClosedPeriods).HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId);
    }
}

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.PhotoUrl).HasMaxLength(512);
        b.HasOne(x => x.Organization).WithMany(o => o.Resources).HasForeignKey(x => x.OrganizationId);
    }
}

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Category).HasMaxLength(80);
        b.Property(x => x.Price).HasPrecision(10, 3);
        b.HasOne(x => x.Organization).WithMany(o => o.Services).HasForeignKey(x => x.OrganizationId);
    }
}

public class ServiceResourceConfiguration : IEntityTypeConfiguration<ServiceResource>
{
    public void Configure(EntityTypeBuilder<ServiceResource> b)
    {
        b.HasKey(x => new { x.ServiceId, x.ResourceId });
        b.HasOne(x => x.Service).WithMany(s => s.ServiceResources).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Resource).WithMany(r => r.ServiceResources).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.Property(x => x.Title).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.OrganizationId, x.IsActive, x.ValidTo });
        b.HasOne(x => x.Organization).WithMany(o => o.Promotions).HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
    }
}

public class OrganizationClientConfiguration : IEntityTypeConfiguration<OrganizationClient>
{
    public void Configure(EntityTypeBuilder<OrganizationClient> b)
    {
        b.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
        b.Property(x => x.PhoneNumber).HasMaxLength(20);
        b.Property(x => x.PrivateNotes).HasMaxLength(2000);
        b.HasIndex(x => new { x.OrganizationId, x.UserId });
        b.HasIndex(x => new { x.OrganizationId, x.PhoneNumber });
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}
