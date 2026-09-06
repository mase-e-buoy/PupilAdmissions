namespace PupilAdmissions.Domain;

/// <summary>
/// Minimal actor stub for attributing <see cref="ChangeHistory"/> rows.
/// Deliberately has no auth/credential fields — ASP.NET Core Identity is
/// out of scope until Epic 1 ships; this is an id + display name only.
/// </summary>
public class ApplicationUser
{
    /// <summary>
    /// The constant id of the seeded hardcoded "system" actor used for
    /// every write until Epic 1 (login) ships real authenticated users.
    /// </summary>
    public const int SystemActorId = 1;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
