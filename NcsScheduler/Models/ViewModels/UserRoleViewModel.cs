namespace NcsScheduler.Models.ViewModels;

public class UserRoleViewModel
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Callsign { get; set; }
    public string? Name { get; set; }
    public List<string> Roles { get; set; } = [];
    public bool IsCurrentUser { get; set; }

    /// <summary>True when a BandCoordinator DB record exists for this user's NetController.</summary>
    public bool HasCoordinatorRecord { get; set; }

    public bool IsCoordinator => Roles.Contains("BandCoordinator");
    public bool IsSuperAdmin => Roles.Contains("SuperAdmin");
    public bool IsNetController => Roles.Contains("NetController");

    /// <summary>
    /// True when the DB says coordinator but Identity role is missing —
    /// happens when promotion occurred before the user accepted their invite.
    /// </summary>
    public bool CoordinatorRoleMissing => HasCoordinatorRecord && !IsCoordinator && !IsSuperAdmin;
}
