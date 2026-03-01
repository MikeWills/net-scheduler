using NcsScheduler.Models.Domain;

namespace NcsScheduler.Services;

public interface IEmailService
{
    Task SendInviteAsync(string toEmail, string toName, string inviteUrl);
    Task SendSlotOpenedAsync(NetController coordinator, NetSession session, NetController unavailableController);
    Task SendVolunteerNotificationAsync(NetController coordinator, NetSession session, NetController volunteer);
    Task SendAssignmentConfirmationAsync(NetController controller, NetSession session);
}
