using AgentNotifications.App.Domain;

namespace AgentNotifications.App.Feishu;

public interface IFeishuClient
{
    Task<FeishuMessage> SendAsync(FeishuDestination destination, DeliveryRecord notification, CancellationToken cancellationToken);

    Task UpdateAsync(FeishuDestination destination, string messageId, DeliveryRecord notification, CancellationToken cancellationToken);
}
