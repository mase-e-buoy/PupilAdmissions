namespace PupilAdmissions.Domain;

/// <summary>
/// The sole authoritative source of agent name, deposit detail, and
/// nationality for a pupil (0..1 per <see cref="Pupil"/>, AD-9). A
/// <see cref="ShortStayDetail"/> row references this entity rather than
/// duplicating any of these fields — a pupil never has two independently
/// edited copies of agent/nationality data.
/// </summary>
public class InternationalDetail
{
    public int Id { get; set; }

    public int PupilId { get; set; }

    public Pupil? Pupil { get; set; }

    public string? AgentName { get; set; }

    public string? DepositDetail { get; set; }

    public string? Nationality { get; set; }
}
