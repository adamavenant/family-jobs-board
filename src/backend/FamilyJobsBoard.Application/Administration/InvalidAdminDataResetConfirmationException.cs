namespace FamilyJobsBoard.Application.Administration;

public sealed class InvalidAdminDataResetConfirmationException : InvalidOperationException
{
    public InvalidAdminDataResetConfirmationException()
        : base($"Type {AdministrationService.RequiredConfirmation} exactly to confirm the reset.")
    {
    }
}
