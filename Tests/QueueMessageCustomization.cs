namespace SelfMonitoringFunctionTests;

public class QueueMessageCustomization : ICustomization
{
    public void Customize(IFixture fixture)
    {
        fixture.Customize<QueueMessage>(composer =>
            composer.FromFactory((IFixture f) => CreateQueueMessageWithRandomValues(f)));
    }

    private static QueueMessage CreateQueueMessageWithRandomValues(IFixture fixture)
    {
        return QueuesModelFactory.QueueMessage(
            messageId: fixture.Create<string>(),
            popReceipt: fixture.Create<string>(),
            messageText: fixture.Create<string>(),
            dequeueCount: 0);
    }
}