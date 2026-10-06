namespace FamilyJobsBoard.Api.Features.Points;

public sealed record PointsLedgerResponse(
    Guid? SelectedChildId,
    IReadOnlyList<PointsLedgerChildResponse> Children,
    IReadOnlyList<PointsLedgerEntryResponse> Entries,
    string? NextCursor);

public sealed record PointsLedgerChildResponse(
    Guid Id,
    string DisplayName,
    bool IsActive,
    int Balance);

public sealed record PointsLedgerEntryResponse(
    Guid Id,
    Guid ChildId,
    string ChildDisplayName,
    string Name,
    int Points,
    int BalanceAfter,
    DateTimeOffset AwardedAtUtc,
    string? RecordedByDisplayName);
