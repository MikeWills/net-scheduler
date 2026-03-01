namespace NcsScheduler.Models.Domain;

/// <summary>
/// An invite token sent by a coordinator/admin to a new net controller.
/// The controller record is pre-created; the invitation links their email to it.
/// </summary>
public class Invitation
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Token { get; set; } = "";
    public string InvitedByUserId { get; set; } = "";
    public int NetControllerId { get; set; }
    public NetController NetController { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsUsed => UsedAt.HasValue;
    public bool IsValid => !IsExpired && !IsUsed;
}
