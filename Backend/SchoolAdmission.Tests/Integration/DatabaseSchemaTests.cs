using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLSchema")]
public sealed class DatabaseSchemaTests(PostgreSqlSchemaFixture database) : IClassFixture<PostgreSqlSchemaFixture>
{
    [PostgreSqlSchemaFact]
    public async Task MigratedColumnsMatchTheErdAndUtcValuesRoundTrip()
    {
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT table_name, column_name, data_type, is_nullable, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name IN ('AdmissionClasses', 'AdmissionHolds')
            """, (NpgsqlConnection)context.Database.GetDbConnection());
        var columns = new Dictionary<string, (string Type, bool Nullable, int? MaxLength)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns[$"{reader.GetString(0)}.{reader.GetString(1)}"] =
                    (reader.GetString(2), reader.GetString(3) == "YES", reader.IsDBNull(4) ? null : reader.GetInt32(4));
            }
        }

        Assert.Equal(13, columns.Count);
        Assert.Equal(("uuid", false, (int?)null), columns["AdmissionClasses.id"]);
        Assert.Equal(("character varying", false, (int?)200), columns["AdmissionClasses.name"]);
        Assert.Equal(("integer", false, (int?)null), columns["AdmissionClasses.total_seats"]);
        Assert.Equal(("uuid", false, (int?)null), columns["AdmissionHolds.id"]);
        Assert.Equal(("uuid", false, (int?)null), columns["AdmissionHolds.class_id"]);
        Assert.Equal(("character varying", false, (int?)64), columns["AdmissionHolds.student_id"]);
        Assert.Equal(("character varying", false, (int?)16), columns["AdmissionHolds.status"]);
        foreach (var column in new[] { "AdmissionClasses.created_at", "AdmissionClasses.updated_at",
                     "AdmissionHolds.created_at", "AdmissionHolds.expires_at" })
        {
            Assert.Equal(("timestamp with time zone", false, (int?)null), columns[column]);
        }
        Assert.Equal(("timestamp with time zone", true, (int?)null), columns["AdmissionHolds.confirmed_at"]);
        Assert.Equal(("timestamp with time zone", true, (int?)null), columns["AdmissionHolds.cancelled_at"]);

        var admissionClass = await CreateClassAsync();
        var hold = NewHold(admissionClass.Id, "00000000000000001");
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var stored = await context.AdmissionHolds.Include(entity => entity.AdmissionClass).SingleAsync(entity => entity.Id == hold.Id);
        Assert.Equal("00000000000000001", stored.StudentId);
        Assert.Equal(AdmissionHoldStatus.HELD, stored.Status);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.ExpiresAt.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.AdmissionClass.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.AdmissionClass.UpdatedAt.Kind);
        Assert.Null(stored.ConfirmedAt);
        Assert.Null(stored.CancelledAt);

        // Check the two required lookup indexes in the actual migrated database.
        var indexNames = await context.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value" FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'AdmissionHolds'
            """).ToListAsync();
        Assert.Contains("ix_admission_holds_class_status_expiry", indexNames);
        Assert.Contains("ix_admission_holds_student_created", indexNames);
    }

    [PostgreSqlSchemaFact]
    public async Task PositiveCapacityAndUniqueClassNameAreEnforced()
    {
        foreach (var seats in new[] { 0, -1 })
        {
            await AssertRejectedAsync($"""
                INSERT INTO "AdmissionClasses" (id, name, total_seats)
                VALUES ({Guid.NewGuid()}, {Guid.NewGuid().ToString()}, {seats})
                """, PostgresErrorCodes.CheckViolation, "ck_admission_classes_positive_seats");
        }

        var existing = await CreateClassAsync();
        await AssertRejectedAsync($"""
            INSERT INTO "AdmissionClasses" (id, name, total_seats)
            VALUES ({Guid.NewGuid()}, {existing.Name}, {10})
            """, PostgresErrorCodes.UniqueViolation, "ux_admission_classes_name");
    }

    [PostgreSqlSchemaFact]
    public async Task ForeignKeyRejectsUnknownClassesAndPreventsDeletingApplicationHistory()
    {
        var hold = NewHold(Guid.NewGuid(), "synthetic-student");
        await AssertRejectedAsync($"""
            INSERT INTO "AdmissionHolds" (id, class_id, student_id, status, expires_at)
            VALUES ({hold.Id}, {hold.ClassId}, {hold.StudentId}, {"HELD"}, {hold.ExpiresAt})
            """, PostgresErrorCodes.ForeignKeyViolation, "fk_admission_holds_class");

        var admissionClass = await CreateClassAsync();
        await InsertHoldAsync(NewHold(admissionClass.Id, "synthetic-student"));
        await AssertRejectedAsync($"""
            DELETE FROM "AdmissionClasses" WHERE id = {admissionClass.Id}
            """, PostgresErrorCodes.RestrictViolation, "fk_admission_holds_class");
    }

    [PostgreSqlSchemaFact]
    public async Task UnknownStoredStatusIsRejected()
    {
        var admissionClass = await CreateClassAsync();
        await AssertRejectedAsync($"""
            INSERT INTO "AdmissionHolds" (id, class_id, student_id, status, expires_at)
            VALUES ({Guid.NewGuid()}, {admissionClass.Id}, {"synthetic-student"}, {"UNKNOWN"}, {DateTime.UtcNow.AddMinutes(10)})
            """, PostgresErrorCodes.CheckViolation, "ck_admission_holds_status");
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD)]
    [InlineData(AdmissionHoldStatus.CONFIRMED)]
    public async Task ActiveOrConfirmedApplicationPreventsAnotherActiveApplication(AdmissionHoldStatus status)
    {
        var admissionClass = await CreateClassAsync();
        var existing = NewHold(admissionClass.Id, "synthetic-student", status);
        await InsertHoldAsync(existing);

        await AssertDuplicateHoldRejectedAsync(existing);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.EXPIRED)]
    [InlineData(AdmissionHoldStatus.CANCELLED)]
    public async Task TerminalHistoryAllowsAnotherApplicationAndPreservesExpiry(AdmissionHoldStatus status)
    {
        var admissionClass = await CreateClassAsync();
        var historical = NewHold(admissionClass.Id, "synthetic-student", status);
        await InsertHoldAsync(historical);
        await InsertHoldAsync(NewHold(admissionClass.Id, historical.StudentId));

        await using var context = database.CreateContext();
        var applications = await context.AdmissionHolds.Where(entity => entity.ClassId == admissionClass.Id).ToListAsync();
        Assert.Equal(2, applications.Count);
        Assert.Equal(status, applications.Single(entity => entity.Id == historical.Id).Status);
        Assert.True(applications.All(entity => entity.ExpiresAt > DateTime.UnixEpoch));
    }

    [PostgreSqlSchemaFact]
    public async Task OverdueHeldRowOccupiesUniqueIndexUntilItIsPersistedAsExpired()
    {
        var admissionClass = await CreateClassAsync();
        var overdue = NewHold(admissionClass.Id, "synthetic-student");
        overdue.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await InsertHoldAsync(overdue);
        await AssertDuplicateHoldRejectedAsync(overdue);

        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.SingleAsync(entity => entity.Id == overdue.Id);
        stored.Status = AdmissionHoldStatus.EXPIRED;
        await context.SaveChangesAsync();
        await InsertHoldAsync(NewHold(admissionClass.Id, overdue.StudentId));
    }

    [PostgreSqlSchemaFact]
    public async Task StudentIdentifierLengthAndPerClassUniquenessAreEnforced()
    {
        var firstClass = await CreateClassAsync();
        var secondClass = await CreateClassAsync();
        var studentId = new string('0', 64);
        await InsertHoldAsync(NewHold(firstClass.Id, studentId));
        await InsertHoldAsync(NewHold(secondClass.Id, studentId));
        await InsertHoldAsync(NewHold(firstClass.Id, "another-synthetic-student"));

        await AssertRejectedAsync($"""
            INSERT INTO "AdmissionHolds" (id, class_id, student_id, status, expires_at)
            VALUES ({Guid.NewGuid()}, {firstClass.Id}, {new string('0', 65)}, {"HELD"}, {DateTime.UtcNow.AddMinutes(10)})
            """, PostgresErrorCodes.StringDataRightTruncation);
    }

    [PostgreSqlSchemaFact]
    public async Task InitialMigrationCanBeRolledBackAndReapplied()
    {
        // Every case creates its own records; this class runs sequentially in its owned database.
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Migration.InitialDatabase);
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private async Task<AdmissionClass> CreateClassAsync()
    {
        await using var context = database.CreateContext();
        var admissionClass = new AdmissionClass { Name = $"Synthetic class {Guid.NewGuid():N}", TotalSeats = 10 };
        context.AdmissionClasses.Add(admissionClass);
        await context.SaveChangesAsync();
        return admissionClass;
    }

    private static AdmissionHold NewHold(Guid classId, string studentId, AdmissionHoldStatus status = AdmissionHoldStatus.HELD) =>
        new()
        {
            ClassId = classId,
            StudentId = studentId,
            Status = status,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ConfirmedAt = status == AdmissionHoldStatus.CONFIRMED ? DateTime.UtcNow : null,
            CancelledAt = status == AdmissionHoldStatus.CANCELLED ? DateTime.UtcNow : null
        };

    private async Task InsertHoldAsync(AdmissionHold hold)
    {
        await using var context = database.CreateContext();
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
    }

    private Task AssertDuplicateHoldRejectedAsync(AdmissionHold existing) => AssertRejectedAsync($"""
        INSERT INTO "AdmissionHolds" (id, class_id, student_id, status, expires_at)
        VALUES ({Guid.NewGuid()}, {existing.ClassId}, {existing.StudentId}, {"HELD"}, {DateTime.UtcNow.AddMinutes(10)})
        """, PostgresErrorCodes.UniqueViolation, "ux_admission_holds_class_student_active");

    private async Task AssertRejectedAsync(FormattableString sql, string sqlState, string? constraint = null)
    {
        await using var context = database.CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(sql));
        Assert.Equal(sqlState, exception.SqlState);
        if (constraint is not null)
        {
            Assert.Equal(constraint, exception.ConstraintName);
        }
    }
}
