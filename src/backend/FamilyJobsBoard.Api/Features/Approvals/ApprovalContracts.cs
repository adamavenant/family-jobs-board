using FamilyJobsBoard.Api.Features.Today;

namespace FamilyJobsBoard.Api.Features.Approvals;

public sealed record PendingApprovalsResponse(IReadOnlyList<JobResponse> Jobs);
