using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLExpiration")]
public sealed class HoldExpirationTests(PostgreSqlSchemaFixture database)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable))) return;
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaFact]
    public async Task EmptyBatchAndRepeatedExpirationReturnZero()
    {
        await using var context = database.CreateContext();
        Assert.Equal(0, await Service(context).ExpireBatchAsync(CancellationToken.None));
        await SeedAsync(1);
        Assert.Equal(1, await Service(context).ExpireBatchAsync(CancellationToken.None));
        Assert.Equal(0, await Service(context).ExpireBatchAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlSchemaFact]
    public async Task OnlyOverdueHeldRowsExpireAndAllTimestampsArePreserved()
    {
        var rows = await SeedAsync(5);
        await using var context = database.CreateContext();
        var now = await TimeAsync(context);
        await context.AdmissionHolds.Where(hold => hold.Id == rows[1].Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.ExpiresAt, now.AddHours(1)));
        foreach (var (index, status) in new[] { (2, AdmissionHoldStatus.CONFIRMED), (3, AdmissionHoldStatus.CANCELLED), (4, AdmissionHoldStatus.EXPIRED) })
            await context.AdmissionHolds.Where(hold => hold.Id == rows[index].Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.Status, status));
        var before = await context.AdmissionHolds.AsNoTracking().OrderBy(hold => hold.Id).ToListAsync();

        Assert.Equal(1, await Service(context).ExpireBatchAsync(CancellationToken.None));

        var after = await context.AdmissionHolds.AsNoTracking().OrderBy(hold => hold.Id).ToListAsync();
        foreach (var (old, current) in before.Zip(after))
        {
            Assert.Equal(old.Id == rows[0].Id ? AdmissionHoldStatus.EXPIRED : old.Status, current.Status);
            Assert.Equal((old.Id, old.ClassId, old.StudentId, old.CreatedAt, old.ExpiresAt, old.ConfirmedAt, old.CancelledAt),
                (current.Id, current.ClassId, current.StudentId, current.CreatedAt, current.ExpiresAt, current.ConfirmedAt, current.CancelledAt));
        }
    }

    [PostgreSqlSchemaFact]
    public async Task BatchLimitProcessesOldestRowsFirstAndDrainsAcrossCycles()
    {
        var rows = await SeedAsync(5);
        await using var context = database.CreateContext();
        var service = Service(context, 2);
        Assert.Equal(2, await service.ExpireBatchAsync(CancellationToken.None));
        var expiredIds = await context.AdmissionHolds.Where(hold => hold.Status == AdmissionHoldStatus.EXPIRED).Select(hold => hold.Id).ToListAsync();
        Assert.Equal(rows.Take(2).Select(hold => hold.Id).Order(), expiredIds.Order());
        Assert.Equal(2, await service.ExpireBatchAsync(CancellationToken.None));
        Assert.Equal(1, await service.ExpireBatchAsync(CancellationToken.None));
        Assert.Equal(0, await service.ExpireBatchAsync(CancellationToken.None));
    }

    [PostgreSqlSchemaFact]
    public async Task LockedRowIsSkippedAndACommittedStatusChangeIsNeverOverwritten()
    {
        var rows = await SeedAsync(2);
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.AdmissionHolds.Where(hold => hold.Id == rows[0].Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.Status, AdmissionHoldStatus.CONFIRMED));
        await using var worker = database.CreateContext();
        Assert.Equal(1, await Service(worker).ExpireBatchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        await transaction.CommitAsync();
        Assert.Equal(0, await Service(worker).ExpireBatchAsync(CancellationToken.None));
        Assert.Equal(AdmissionHoldStatus.CONFIRMED, (await worker.AdmissionHolds.AsNoTracking().SingleAsync(hold => hold.Id == rows[0].Id)).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task ConcurrentBatchesUpdateEachRowAtMostOnce()
    {
        await SeedAsync(6);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var counts = await Task.WhenAll(Service(first, 3).ExpireBatchAsync(CancellationToken.None),
            Service(second, 3).ExpireBatchAsync(CancellationToken.None));
        Assert.Equal(new[] { 3, 3 }, counts);
        Assert.Equal(6, await first.AdmissionHolds.CountAsync(hold => hold.Status == AdmissionHoldStatus.EXPIRED));
        Assert.Equal(0, await Service(first).ExpireBatchAsync(CancellationToken.None));
    }

    [PostgreSqlSchemaFact]
    public async Task HostedWorkersExpireHoldsAcrossCyclesWithoutAnyHttpRequest()
    {
        var rows = await SeedAsync(5);
        await using var context = database.CreateContext();
        var now = await TimeAsync(context);
        await context.AdmissionHolds.Where(hold => hold.Id == rows[4].Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.ExpiresAt, now.AddMilliseconds(500)));
        using var first = Factory();
        using var second = Factory();
        _ = first.Services; // Start the actual hosted services without creating an HTTP client.
        _ = second.Services;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await context.AdmissionHolds.CountAsync(hold => hold.Status == AdmissionHoldStatus.EXPIRED, timeout.Token) != 5)
            await Task.Delay(30, timeout.Token);
        Assert.Equal(0, await context.AdmissionHolds.CountAsync(hold => hold.Status == AdmissionHoldStatus.HELD));
    }

    private FoundationApiFactory Factory() => new(overrides: new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
        ["Expiration:IntervalMilliseconds"] = "100",
        ["Expiration:BatchSize"] = "1"
    }, runExpirationWorker: true);

    private static HoldExpirationService Service(AppDbContext context, int batchSize = 100) =>
        new(context, Options.Create(new ExpirationOptions { BatchSize = batchSize }));

    private async Task<List<AdmissionHold>> SeedAsync(int count)
    {
        await using var context = database.CreateContext();
        var now = await TimeAsync(context);
        var admissionClass = new AdmissionClass { Name = "Expiration test", TotalSeats = 20 };
        var rows = Enumerable.Range(0, count).Select(index => new AdmissionHold
        {
            AdmissionClass = admissionClass, ClassId = admissionClass.Id,
            StudentId = $"synthetic-{index}", ExpiresAt = now.AddMinutes(-count + index)
        }).ToList();
        context.AdmissionHolds.AddRange(rows);
        await context.SaveChangesAsync();
        return rows;
    }

    private static Task<DateTime> TimeAsync(AppDbContext context) =>
        context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
}
