using System.Text.Json;
using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.App.Feishu;

public sealed class FeishuCardBuilder(RuntimeSettings settings)
{
    public string Build(DeliveryRecord notification)
    {
        var title = TextTools.LimitRunes(notification.Title ?? notification.SourceName, settings.CardTitleMaximumRunes);
        var body = TextTools.LimitRunes(notification.Body ?? string.Empty, settings.CardBodyMaximumRunes);
        var metadata = $"**Source:** {TextTools.EscapeFeishuMarkdown(notification.SourceName)}\n"
            + $"**Time:** {notification.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}";
        var content = string.IsNullOrWhiteSpace(body)
            ? metadata
            : metadata + "\n\n" + TextTools.EscapeFeishuMarkdown(body);
        var card = new
        {
            schema = "2.0",
            config = new { update_multi = false },
            header = new
            {
                title = new { tag = "plain_text", content = title },
                template = "green",
            },
            body = new
            {
                direction = "vertical",
                padding = "12px",
                elements = new object[] { new { tag = "markdown", content } },
            },
        };
        return JsonSerializer.Serialize(card);
    }
}
