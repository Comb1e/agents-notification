using System.Text;
using System.Xml.Linq;
using AgentNotifications.App.Domain;
using Windows.ApplicationModel;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AgentNotifications.App.Notifications;

public sealed class WindowsToastPresenter
{
    public void Show(ObservedNotification notification)
    {
        var document = new XmlDocument();
        document.LoadXml(BuildXml(notification.Title, notification.Body));
        var toast = new ToastNotification(document)
        {
            Tag = Infrastructure.TextTools.Sha256(notification.Key)[..16],
            Group = "cli-completion",
            ExpirationTime = DateTimeOffset.Now.AddHours(24),
        };
        ToastNotificationManager.CreateToastNotifier($"{Package.Current.Id.FamilyName}!App").Show(toast);
    }

    public static string BuildXml(string title, string body) => new XElement("toast",
        new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", CleanXmlText(title)), new XElement("text", CleanXmlText(body)))))
        .ToString(SaveOptions.DisableFormatting);

    private static string CleanXmlText(string value) => string.Concat(value.EnumerateRunes()
        .Where(rune => rune.Value is 9 or 10 or 13 or >= 0x20 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
        .Select(rune => rune.ToString()));
}
