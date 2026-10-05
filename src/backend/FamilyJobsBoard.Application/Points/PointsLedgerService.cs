using FamilyJobsBoard.Domain.Households;

namespace FamilyJobsBoard.Application.Points;

public sealed class PointsLedgerService
{
    public const int PageSize = 20;

    private readonly IPointsLedgerRepository _repository;

    public PointsLedgerService(IPointsLedgerRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Reads one page of the ledger, newest first. Children always get their own ledger;
    /// adults get every child's, or one child's when <paramref name="childId"/> is given.
    /// </summary>
    public async Task<PointsLedger> GetAsync(
        Guid viewerId,
        Guid? childId,
        string? before,
        CancellationToken cancellationToken)
    {
        var viewer = await _repository.GetActiveMemberAsync(viewerId, cancellationToken)
            ?? throw new PointsLedgerForbiddenException();
        if (!viewer.IsAdult && childId is not null && childId != viewer.Id)
        {
            throw new PointsLedgerForbiddenException();
        }

        PointsLedgerPosition? position = null;
        if (before is not null)
        {
            if (!PointsLedgerPosition.TryParse(before, out var parsed))
            {
                throw Invalid("Before", "Start from the newest entries and try again.");
            }

            position = parsed;
        }

        var children = await _repository.GetChildrenAsync(cancellationToken);
        if (viewer.IsAdult && childId is not null && children.All(child => child.Id != childId))
        {
            throw Invalid("ChildId", "Choose a child in this household.");
        }

        var balances = await _repository.GetBalancesAsync(cancellationToken);
        var selectedChildId = viewer.IsAdult ? childId : viewer.Id;
        var listedChildren = viewer.IsAdult
            ? children
                .Where(child => child.IsActive || balances.ContainsKey(child.Id))
                .OrderBy(child => !child.IsActive)
                .ToArray()
            : [viewer];

        var records = await _repository.GetEntriesAsync(
            selectedChildId,
            position,
            PageSize + 1,
            cancellationToken);
        var page = records.Take(PageSize).ToArray();

        return new PointsLedger(
            selectedChildId,
            listedChildren
                .Select(child => new PointsLedgerChild(
                    child.Id,
                    child.DisplayName,
                    child.IsActive,
                    balances.GetValueOrDefault(child.Id)))
                .ToArray(),
            await MapLinesAsync(page, children, cancellationToken),
            records.Count > PageSize ? PointsLedgerPosition.Of(page[^1]).ToString() : null);
    }

    private async Task<IReadOnlyList<PointsLedgerLine>> MapLinesAsync(
        IReadOnlyList<PointsLedgerRecord> page,
        IReadOnlyList<HouseholdMember> children,
        CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        // Every entry for a child between the top of this page and that child's first row is
        // on the page, so walking down from the balance at the top gives each row's
        // balance-after, even across pages.
        var running = new Dictionary<Guid, int>(await _repository.GetBalancesThroughAsync(
            page.Select(record => record.ChildId).Distinct().ToArray(),
            PointsLedgerPosition.Of(page[0]),
            cancellationToken));
        var names = children.ToDictionary(child => child.Id, child => child.DisplayName);
        var lines = new List<PointsLedgerLine>(page.Count);
        foreach (var record in page)
        {
            var balanceAfter = running.GetValueOrDefault(record.ChildId);
            running[record.ChildId] = balanceAfter - record.Amount;
            lines.Add(new PointsLedgerLine(
                record.Id,
                record.ChildId,
                names.GetValueOrDefault(record.ChildId) ?? string.Empty,
                record.Name,
                record.Amount,
                balanceAfter,
                record.AwardedAtUtc,
                record.RecordedByDisplayName));
        }

        return lines;
    }

    private static InvalidPointsLedgerRequestException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
