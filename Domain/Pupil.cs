namespace PupilAdmissions.Domain;

/// <summary>
/// A pupil record. Story 2.1 populates only the fields the create flow
/// needs (name, year group, boarding type, initial status) — short-stay,
/// international, discount, and archival fields are later stories (2.2-2.6)
/// and are deliberately not modeled here yet.
/// </summary>
public class Pupil
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public YearGroup YearGroup { get; set; }

    public BoardingType BoardingType { get; set; }

    public PupilStatus Status { get; set; }

    public ICollection<ChangeHistory> ChangeHistories { get; set; } = new List<ChangeHistory>();
}
