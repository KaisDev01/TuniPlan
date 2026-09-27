using DAL.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DAL.Context;

/// <summary>Used by "dotnet ef migrations add" when run from the DAL project.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TuniPlanDbContext>
{
    public TuniPlanDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TUNIPLAN_DB")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=TuniPlanDb;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<TuniPlanDbContext>()
            .UseSqlServer(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor())
            .Options;
        return new TuniPlanDbContext(options);
    }
}
