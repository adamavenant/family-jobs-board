using System.Globalization;

namespace FamilyJobsBoard.Application.Points;

public sealed record PointsLedger(
    Guid? SelectedChildId,
    IReadOnlyList<PointsLedgerChild> Children,
    IReadOnlyList<PointsLedgerLine> Entries,
    string? NextCursor);

public sealed record PointsLedgerChild(
    Guid Id,
    string DisplayName,
    bool IsActive,
    int Balance);

public sealed record PointsLedgerLine(
    Guid Id,
    Guid ChildId,
    string ChildDisplayName,
    string Name,
    int Points,
    int BalanceAfter,
    DateTimeOffset AwardedAtUtc);

/// <summary>
/// A stored ledger entry with the name of its source (job, behaviour as logged, or
/// adjustment reason) already resolved.
/// </summary>
public sealed record PointsLedgerRecord(
    Guid Id,
    Guid ChildId,
    string Name,
    int Amount,
    DateTimeOffset AwardedAtUtc);

/// <summary>
/// A position in the newest-first ledger order: award time, then entry ID as a tie-break.
/// </summary>
public readonly record struct PointsLedgerPosition(DateTimeOffset AwardedAtUtc, Guid Id)
{
    private const char Separator = '-';

    public static PointsLedgerPosition Of(PointsLedgerRecord record) =>
        new(record.AwardedAtUtc, record.Id);

    public static bool TryParse(string? value, out PointsLedgerPosition position)
    {
        position = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separator = value.IndexOf(Separator);
        if (separator <= 0
            || !long.TryParse(
                value.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(value.AsSpan(separator + 1), "N", out var id))
        {
            return false;
        }

        position = new PointsLedgerPosition(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{AwardedAtUtc.UtcTicks}{Separator}{Id:N}");
}
