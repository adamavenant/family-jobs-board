namespace FamilyJobsBoard.Application.Today.Requests;

public class AddJobRequest
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required int Points { get; set; }
}