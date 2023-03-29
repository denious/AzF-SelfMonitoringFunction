using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace SelfMonitoringFunctionTests;

public class EmailServiceSemaphoreTests
{
    private readonly CancellationToken _noneCancellationToken = CancellationToken.None;
    private readonly IFixture _fixture;
    private readonly ILogger<TimedQueueReaderFunction> _mockLogger;

    public EmailServiceSemaphoreTests()
    {
        _fixture = new Fixture()
            .Customize(new AutoMoqCustomization())
            .Customize(new QueueMessageCustomization());

        _mockLogger = _fixture.Create<ILogger<TimedQueueReaderFunction>>();
    }

    [Fact]
    public async Task EmailServiceSemaphore_SendEmails_NoEmailsInQueue_NoActionTaken()
    {
        /*
         * Arrange
         */

        var (mockQueueClient, _) = MockQueueClient(0);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(x => x.GetService(typeof(IEmailService)))
            .Returns(_fixture.Create<IEmailService>());

        var emailServiceSemaphore =
            new EmailServiceSemaphore(_mockLogger, mockQueueClient.Object, mockServiceProvider.Object);

        /*
         * Act
         */

        await emailServiceSemaphore.SendEmails(_noneCancellationToken);

        /*
         * Assert
         */

        mockQueueClient.Verify(x =>
                x.ReceiveMessagesAsync(It.IsAny<int>(), It.IsAny<TimeSpan?>(), _noneCancellationToken),
            Times.Never());

        mockServiceProvider.Verify(x =>
                x.GetService(typeof(IEmailService)),
            Times.Never());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task EmailServiceSemaphore_SendEmails_EmailsInQueue_AllEmailsSent(int emailCount)
    {
        /*
         * Arrange
         */

        var (mockQueueClient, messages) = MockQueueClient(emailCount);

        var mockEmailService = new Mock<IEmailService>();
        mockEmailService
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection()
            .AddSingleton(mockEmailService.Object)
            .BuildServiceProvider();

        using var emailServiceSemaphore =
            new EmailServiceSemaphore(_mockLogger, mockQueueClient.Object, serviceProvider);

        /*
         * Act
         */

        await emailServiceSemaphore.SendEmails(_noneCancellationToken);

        /*
         * Assert
         */

        foreach (var queueMessage in messages)
        {
            mockEmailService.Verify(x =>
                    x.SendAsync(queueMessage.MessageText, _noneCancellationToken),
                Times.Once);

            mockQueueClient.Verify(x =>
                    x.DeleteMessageAsync(queueMessage.MessageId, queueMessage.PopReceipt, _noneCancellationToken),
                Times.Once);
        }
    }

    [Fact]
    public async Task EmailServiceSemaphore_SendEmails_AllSemaphoreThreadsStuck_TimeoutExceptionThrown()
    {
        /*
         * Arrange
         */

        // calculate how many emails is enough to lock all available semaphore threads to simulate a stuck situation
        const int emailCount = Constants.MAX_SMTP_CLIENTS * Constants.MAX_DEQUEUE_COUNT + 1;

        var (mockQueueClient, _) = MockQueueClient(emailCount);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(x => x.GetService(typeof(IEmailService)))
            .Returns(() =>
            {
                var stuckEmailService = new Mock<IEmailService>();

                stuckEmailService
                    .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))

                    // simulate a stuck email sender by taking longer than the allocated time
                    .Returns(Task.Delay(Constants.EMAIL_SEND_INSTANCE_TIMEOUT * 10));

                return stuckEmailService.Object;
            });

        using var emailServiceSemaphore =
            new EmailServiceSemaphore(_mockLogger, mockQueueClient.Object, mockServiceProvider.Object);

        /*
         * Act & Assert
         */

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            emailServiceSemaphore.SendEmails(_noneCancellationToken));

        Assert.Equal("All semaphore threads are stuck, the system is in an unusable state",
            exception.Message);
    }

    [Fact]
    public async Task EmailServiceSemaphore_SendEmails_SingleSemaphoreThreadStuck_WarningLogged()
    {
        /*
         * Arrange
         */
        var mockLogger = new Mock<ILogger<TimedQueueReaderFunction>>();

        var emailCount = 1;

        var (mockQueueClient, _) = MockQueueClient(emailCount);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(x => x.GetService(typeof(IEmailService)))
            .Returns(() =>
            {
                var stuckEmailService = new Mock<IEmailService>();

                stuckEmailService
                    .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))

                    // simulate a stuck email sender by taking longer than the allocated time
                    .Returns(Task.Delay(Constants.EMAIL_SEND_INSTANCE_TIMEOUT * 10));

                return stuckEmailService.Object;
            });

        using var emailServiceSemaphore =
            new EmailServiceSemaphore(mockLogger.Object, mockQueueClient.Object, mockServiceProvider.Object);

        /*
         * Act & Assert
         */

        await emailServiceSemaphore.SendEmails(_noneCancellationToken);

        var warningMessage = $"All messages dequeued but 1 of {Constants.MAX_SMTP_CLIENTS} semaphore threads are stuck, the system is in an unstable state";
        mockLogger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Warning),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, @type) => @object.ToString() == warningMessage),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task EmailServiceSemaphore_SendEmails_CancellationTokenCancelled_OperationCancelledExceptionThrown(
        int emailCount)
    {
        /*
         * Arrange
         */

        const int simulatedEmailSendDelayMs = 500;

        var (mockQueueClient, _) = MockQueueClient(emailCount);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(x => x.GetService(typeof(IEmailService)))
            .Returns(() =>
            {
                var cancellingEmailService = new Mock<IEmailService>();

                cancellingEmailService
                    .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.Delay(simulatedEmailSendDelayMs));

                return cancellingEmailService.Object;
            });

        using var emailServiceSemaphore =
            new EmailServiceSemaphore(_mockLogger, mockQueueClient.Object, mockServiceProvider.Object);

        using var cts = new CancellationTokenSource();

        /*
         * Act
         */

        var sendEmailsTask = emailServiceSemaphore.SendEmails(cts.Token);

        // cancel the operation prior to its completion
        cts.Cancel();

        /*
         * Assert
         */

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => sendEmailsTask);

        Assert.Equal("Operation cancelled as requested, see inner exception for details",
            exception.Message);
    }

    [Fact]
    public async Task EmailServiceSemaphore_SendEmails_FatalExceptionThrown_CustomExceptionLogged()
    {
        /*
         * Arrange
         */

        var mockLogger = new Mock<ILogger<TimedQueueReaderFunction>>();

        const int emailCount = 1;

        var (mockQueueClient, _) = MockQueueClient(emailCount);

        const string innerExceptionMessage = "Inner exception test";

        var mockEmailServiceWithException = new Mock<IEmailService>();
        mockEmailServiceWithException
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new Exception(innerExceptionMessage));

        var serviceProvider = new ServiceCollection()
            .AddSingleton(mockEmailServiceWithException.Object)
            .BuildServiceProvider();

        using var emailServiceSemaphore =
            new EmailServiceSemaphore(mockLogger.Object, mockQueueClient.Object, serviceProvider);

        /*
         * Act
         */

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            emailServiceSemaphore.SendEmails(_noneCancellationToken));

        /*
         * Assert
         */

        Assert.Equal(innerExceptionMessage, exception.InnerException!.Message);

        var exceptionMessage = "One or more fatal exceptions encountered when attempting to send emails";
        mockLogger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Error),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, @type) => @object.ToString() == exceptionMessage),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task EmailServiceSemaphore_Dispose_SemaphoreDisposed()
    {
        /*
         * Arrange
         */

        var (mockQueueClient, _) = MockQueueClient(1);

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IEmailService, DummyEmailService>()
            .BuildServiceProvider();

        var emailServiceSemaphore = new EmailServiceSemaphore(_mockLogger, mockQueueClient.Object, serviceProvider);

        /*
         * Act
         */

        emailServiceSemaphore.Dispose();

        /*
         * Assert
         */

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => emailServiceSemaphore.SendEmails(_noneCancellationToken));
    }


    private (Mock<QueueClient> mockQueueClient, ConcurrentQueue<QueueMessage> messages) MockQueueClient(
        int messageCount)
    {
        // populate initial list of queue messages
        // concurrent queue used to work with multiple instances of email senders used by semaphore
        var messages = new ConcurrentQueue<QueueMessage>(
            _fixture.CreateMany<QueueMessage>(messageCount));

        // track requested cancellations
        var innerCancellationToken = CancellationToken.None;

        var mockQueueClient = new Mock<QueueClient>();

        mockQueueClient
            .Setup(x => x.GetPropertiesAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(cancellationToken =>
                innerCancellationToken = cancellationToken
            )
            .ReturnsAsync(() =>
            {
                if (innerCancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();

                // return the latest count of messages remaining in queue
                var queueProperties = BuildQueueProperties(messages.Count);

                return Response.FromValue(queueProperties, Mock.Of<Response>());
            });

        mockQueueClient
            .Setup(x => x.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, _, cancellationToken) =>
                innerCancellationToken = cancellationToken
            )
            .ReturnsAsync(() =>
            {
                if (innerCancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();

                return Mock.Of<Response>();
            });

        // track message count to dequeue on each request
        var maxMessages = 0;

        mockQueueClient
            .Setup(x => x.ReceiveMessagesAsync(It.IsAny<int>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback<int?, TimeSpan?, CancellationToken>((passedMaxMessages, _, cancellationToken) =>
            {
                maxMessages = passedMaxMessages!.Value;
                innerCancellationToken = cancellationToken;
            })
            .ReturnsAsync(() =>
            {
                if (innerCancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();

                List<QueueMessage> messagesToReturn = new();

                // dequeue requested message count until request is filled or queue is drained
                while (messagesToReturn.Count < maxMessages && messages.TryDequeue(out var message))
                    messagesToReturn.Add(message);

                return Response.FromValue(messagesToReturn.ToArray(), Mock.Of<Response>());
            });

        return (mockQueueClient, messages);
    }

    private static QueueProperties BuildQueueProperties(int messageCount)
    {
        var queueProperties = new QueueProperties();

        // there is no public access to the message count, so force it
        queueProperties
            .GetType()
            .GetProperty(nameof(QueueProperties.ApproximateMessagesCount))!
            .SetValue(queueProperties, messageCount);

        return queueProperties;
    }
}