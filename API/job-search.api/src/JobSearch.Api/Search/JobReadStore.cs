using JobSearch.Api.Contracts;
using JobSearch.Api.Persistence;
using Microsoft.EntityFrameworkCore;
namespace JobSearch.Api.Search;

public sealed record ReadRow(
    Guid Id,
    long SourceTicks,
    DateTimeOffset CreatedAt,
    string Title,
    string Department,
    string Location,
    decimal SalaryMin,
    decimal SalaryMax,
    DateOnly ClosingDate)
{
    public JobSummary Summary() =>
        new(Id, new(SourceTicks, TimeSpan.Zero), Title, Department, Location, SalaryMin, SalaryMax, ClosingDate);

    public PagePosition Position() => new(CreatedAt.UtcTicks, Id, ClosingDate.DayNumber);
}

public interface IJobReadStore
{
    Task<long> WatermarkAsync(CancellationToken token);
    Task<List<ReadRow>> ListAsync(SearchQuery query, CursorPayload snapshot, bool continuation, CancellationToken token);
    Task<JobDetail?> DetailAsync(Guid id, CancellationToken token);
}

public sealed class JobReadStore(IDbContextFactory<SearchDbContext> factory) : IJobReadStore
{
    public async Task<long> WatermarkAsync(CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.ReadWatermarkAsync(token);
    }

    public static string Pattern(string text) => "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public static IQueryable<ReadRow> Query(SearchDbContext db, SearchQuery query, CursorPayload snapshot, bool continuation)
    {
        var today = DateOnly.FromDayNumber(snapshot.Day);
        var rows = db.Jobs.AsNoTracking().Where(x => x.ClosingDate > today && x.IngestionSequence <= snapshot.Watermark);
        if (query.Q is not null)
        {
            var pattern = Pattern(query.Q);
            rows = rows.Where(x => EF.Functions.ILike(x.Title, pattern, "\\") || EF.Functions.ILike(x.Description, pattern, "\\"));
        }
        if (query.Department is not null)
        {
            var pattern = Pattern(query.Department);
            rows = rows.Where(x => EF.Functions.ILike(x.Department, pattern, "\\"));
        }
        if (query.Location is not null)
        {
            var pattern = Pattern(query.Location);
            rows = rows.Where(x => EF.Functions.ILike(x.Location, pattern, "\\"));
        }
        var created = new DateTimeOffset(snapshot.Last.CreatedTicks, TimeSpan.Zero);
        var id = snapshot.Last.Id;
        var closing = DateOnly.FromDayNumber(snapshot.Last.ClosingDay);
        if (continuation)
        {
            if (query.Sort == "newest")
                rows = rows.Where(x => EF.Functions.LessThan(ValueTuple.Create(x.CreatedAt, x.Id), ValueTuple.Create(created, id)));
            else
                rows = rows.Where(x => x.ClosingDate > closing
                    || (x.ClosingDate == closing
                        && EF.Functions.LessThan(ValueTuple.Create(x.CreatedAt, x.Id), ValueTuple.Create(created, id))));
        }
        var ordered = query.Sort == "newest"
            ? rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            : rows.OrderBy(x => x.ClosingDate).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
        return ordered.Take(query.Limit + 1).Select(x => new ReadRow(
            x.Id,
            x.SourceCreatedAtTicks,
            x.CreatedAt,
            x.Title,
            x.Department,
            x.Location,
            x.SalaryMin,
            x.SalaryMax,
            x.ClosingDate));
    }

    public async Task<List<ReadRow>> ListAsync(SearchQuery query, CursorPayload snapshot, bool continuation, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await Query(db, query, snapshot, continuation).ToListAsync(token);
    }

    public async Task<JobDetail?> DetailAsync(Guid id, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Jobs.AsNoTracking().Where(x => x.Id == id).Select(x => new JobDetail(
            x.Id,
            new DateTimeOffset(x.SourceCreatedAtTicks, TimeSpan.Zero),
            x.Title,
            x.Department,
            x.Location,
            x.Description,
            x.SalaryMin,
            x.SalaryMax,
            x.ClosingDate)).SingleOrDefaultAsync(token);
    }
}
