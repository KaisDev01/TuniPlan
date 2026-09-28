using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <summary>
    /// Data only: removes the account +21699887766 and the business 01a0e8ab-9bcf-7bae-a829-eb369a444665 created during
    /// the front-end integration tests (same effect as DELETE /api/account and DELETE /api/business/organizations/{id}).
    /// Idempotent: nothing happens when they do not exist or are already deleted.
    /// </summary>
    public partial class RemoveFrontTestData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @userId uniqueidentifier = (SELECT TOP 1 Id FROM Users WHERE PhoneNumber = '+21699887766' AND IsDeleted = 0);
                DECLARE @orgs TABLE (Id uniqueidentifier PRIMARY KEY);

                INSERT INTO @orgs SELECT Id FROM Organizations WHERE Id = '01a0e8ab-9bcf-7bae-a829-eb369a444665' AND IsDeleted = 0;
                -- Businesses still owned by the test account
                INSERT INTO @orgs SELECT DISTINCT m.OrganizationId FROM OrganizationMembers m
                    JOIN Organizations o ON o.Id = m.OrganizationId AND o.IsDeleted = 0
                    WHERE m.UserId = @userId AND m.Role = 0 AND m.OrganizationId NOT IN (SELECT Id FROM @orgs);

                -- 1. Businesses: upcoming bookings cancelled (5 = CancelledByBusiness), team / favorites / waitlist removed, slug freed
                UPDATE Appointments SET Status = 5, CancelledAt = @now, CancelReason = N'Entreprise fermée sur TuniPlan', UpdatedAt = @now
                    WHERE OrganizationId IN (SELECT Id FROM @orgs) AND StartUtc > @now AND Status IN (0, 1, 2);
                DELETE FROM OrganizationMembers WHERE OrganizationId IN (SELECT Id FROM @orgs);
                DELETE FROM Favorites WHERE OrganizationId IN (SELECT Id FROM @orgs);
                DELETE FROM WaitlistEntries WHERE OrganizationId IN (SELECT Id FROM @orgs);
                UPDATE Organizations
                    SET IsPublished = 0, Slug = CONCAT('deleted-', REPLACE(LOWER(CONVERT(varchar(36), Id)), '-', '')),
                        IsDeleted = 1, DeletedAt = @now, UpdatedAt = @now
                    WHERE Id IN (SELECT Id FROM @orgs);

                -- 2. Account: anonymised and soft-deleted (the phone number can be used again)
                IF @userId IS NOT NULL
                BEGIN
                    UPDATE Appointments SET Status = 4, CancelledAt = @now, CancelReason = N'Compte supprimé', UpdatedAt = @now
                        WHERE ClientUserId = @userId AND StartUtc > @now AND Status IN (0, 1, 2);
                    DELETE FROM OrganizationMembers WHERE UserId = @userId;
                    DELETE FROM WaitlistEntries WHERE UserId = @userId;
                    DELETE FROM UserDevices WHERE UserId = @userId;
                    DELETE FROM ExternalLogins WHERE UserId = @userId;
                    UPDATE RefreshTokens SET RevokedAt = @now, RevokedReason = 'account_deleted', UpdatedAt = @now
                        WHERE UserId = @userId AND RevokedAt IS NULL;
                    UPDATE Users
                        SET IsActive = 0, FirstName = N'Utilisateur', LastName = N'supprimé', Email = NULL,
                            PhoneNumber = LEFT(CONCAT('deleted-', REPLACE(LOWER(CONVERT(varchar(36), Id)), '-', '')), 20),
                            PasswordHash = CONVERT(varchar(64), NEWID()), TwoFactorEnabled = 0, TwoFactorSecretProtected = NULL,
                            SecurityStamp = REPLACE(LOWER(CONVERT(varchar(36), NEWID())), '-', ''),
                            IsDeleted = 1, DeletedAt = @now, UpdatedAt = @now
                        WHERE Id = @userId;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data removal cannot be undone
        }
    }
}
