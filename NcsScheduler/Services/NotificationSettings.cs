namespace NcsScheduler.Services;

public class NotificationSettings
{
    public bool EnableMailingListNotifications { get; set; } = false;
    public string MailingListAddress { get; set; } = "";
}
