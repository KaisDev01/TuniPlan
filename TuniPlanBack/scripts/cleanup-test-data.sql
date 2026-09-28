/* ---------------------------------------------------------------------------
   TuniPlan - removes the test data created during the front-end integration tests:
     - account      +21699887766
     - business     01a0e8ab-9bcf-7bae-a829-eb369a444665
   Same effect as DELETE /api/business/organizations/{id} and DELETE /api/account
   (soft delete + anonymisation, upcoming bookings cancelled, sessions revoked).

   Run it ONCE against the production database (SSMS / Azure Data Studio / sqlcmd).
   Everything runs in one transaction: check the counts printed, then COMMIT.
   --------------------------------------------------------------------------- */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @now datetime2 = SYSUTCDATETIME();
DECLARE @orgId uniqueidentifier = '01a0e8ab-9bcf-7bae-a829-eb369a444665';
DECLARE @userId uniqueidentifier = (SELECT Id FROM Users WHERE PhoneNumber = '+21699887766' AND IsDeleted = 0);

PRINT CONCAT('Business found: ', (SELECT COUNT(*) FROM Organizations WHERE Id = @orgId AND IsDeleted = 0));
PRINT CONCAT('Account found:  ', IIF(@userId IS NULL, 0, 1));

-- 1. Business ---------------------------------------------------------------
-- Status: 0 Pending, 1 Confirmed, 2 CounterProposed -> 5 CancelledByBusiness
UPDATE Appointments
SET Status = 5, CancelledAt = @now, CancelReason = N'Entreprise fermée sur TuniPlan', UpdatedAt = @now
WHERE OrganizationId = @orgId AND StartUtc > @now AND Status IN (0, 1, 2);

DELETE FROM OrganizationMembers WHERE OrganizationId = @orgId;
DELETE FROM Favorites WHERE OrganizationId = @orgId;
DELETE FROM WaitlistEntries WHERE OrganizationId = @orgId;

UPDATE Organizations
SET IsPublished = 0, Slug = CONCAT('deleted-', REPLACE(LOWER(CONVERT(varchar(36), Id)), '-', '')),
    IsDeleted = 1, DeletedAt = @now, UpdatedAt = @now
WHERE Id = @orgId;

-- 2. Account ----------------------------------------------------------------
IF @userId IS NOT NULL
BEGIN
    -- Businesses still owned by this account are deleted too
    DECLARE @owned TABLE (Id uniqueidentifier);
    INSERT INTO @owned SELECT OrganizationId FROM OrganizationMembers WHERE UserId = @userId AND Role = 0;

    UPDATE Appointments
    SET Status = 5, CancelledAt = @now, CancelReason = N'Entreprise fermée sur TuniPlan', UpdatedAt = @now
    WHERE OrganizationId IN (SELECT Id FROM @owned) AND StartUtc > @now AND Status IN (0, 1, 2);
    DELETE FROM Favorites WHERE OrganizationId IN (SELECT Id FROM @owned);
    DELETE FROM WaitlistEntries WHERE OrganizationId IN (SELECT Id FROM @owned);
    DELETE FROM OrganizationMembers WHERE OrganizationId IN (SELECT Id FROM @owned);
    UPDATE Organizations
    SET IsPublished = 0, Slug = CONCAT('deleted-', REPLACE(LOWER(CONVERT(varchar(36), Id)), '-', '')),
        IsDeleted = 1, DeletedAt = @now, UpdatedAt = @now
    WHERE Id IN (SELECT Id FROM @owned);

    -- 4 = CancelledByClient
    UPDATE Appointments
    SET Status = 4, CancelledAt = @now, CancelReason = N'Compte supprimé', UpdatedAt = @now
    WHERE ClientUserId = @userId AND StartUtc > @now AND Status IN (0, 1, 2);

    DELETE FROM OrganizationMembers WHERE UserId = @userId;
    DELETE FROM WaitlistEntries WHERE UserId = @userId;
    UPDATE RefreshTokens SET RevokedAt = @now, RevokedReason = 'account_deleted' WHERE UserId = @userId AND RevokedAt IS NULL;

    UPDATE Users
    SET IsActive = 0, FirstName = N'Utilisateur', LastName = N'supprimé', Email = NULL,
        PhoneNumber = LEFT(CONCAT('deleted-', REPLACE(LOWER(CONVERT(varchar(36), Id)), '-', '')), 20),
        PasswordHash = CONVERT(varchar(64), NEWID()), TwoFactorEnabled = 0, TwoFactorSecretProtected = NULL,
        SecurityStamp = REPLACE(LOWER(CONVERT(varchar(36), NEWID())), '-', ''),
        IsDeleted = 1, DeletedAt = @now, UpdatedAt = @now
    WHERE Id = @userId;
END

-- Check, then run COMMIT (or ROLLBACK to cancel)
SELECT Id, Name, Slug, IsDeleted FROM Organizations WHERE Id = @orgId;
SELECT Id, PhoneNumber, IsActive, IsDeleted FROM Users WHERE Id = @userId;
-- COMMIT;
-- ROLLBACK;
