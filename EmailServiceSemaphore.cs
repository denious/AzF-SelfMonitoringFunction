using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues.Models;

namespace SelfMonitoringFunction;

public class EmailServiceSemaphore : IDisposable
{
    private readonly ILogger<TimedQueueReaderFunction> _log;
    private readonly QueueClient _queueClient;
    private readonly IServiceProvider _serviceProvider;

    private readonly SemaphoreSlim _semaphore = new(Constants.MAX_SMTP_CLIENTS, Constants.MAX_SMTP_CLIENTS);

    public EmailServiceSemaphore(
        ILogger<TimedQueueReaderFunction> log,
        QueueClient queueClient,
        IServiceProvider serviceProvider)
    {
        _log = log;
        _queueClient = queueClient;
        _serviceProvider = serviceProvider;
    }

    public async Task SendEmails(CancellationToken cancellationToken)
    {
        try
        {
            var aggregateExceptions = new List<Exception>();
            var minMessageCount = await GetMessageCountAsync(cancellationToken);

            while (minMessageCount > 0)
            {
                if (!await _semaphore.WaitAsync(Constants.EMAIL_SEND_INSTANCE_TIMEOUT, cancellationToken))
                    throw new TimeoutException("All semaphore threads are stuck, the system is in an unusable state");

                _ = Task.Run(async () =>
                {
                    await SendEmailsAsync(cancellationToken);
                    _semaphore.Release();
                }, cancellationToken).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                        aggregateExceptions.AddRange(task.Exception!.InnerExceptions);
                }, cancellationToken);

                minMessageCount = await GetMessageCountAsync(cancellationToken);
            }

            await WaitForSemaphoreAsync(aggregateExceptions, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            throw new OperationCanceledException(
                "Operation cancelled as requested, see inner exception for details", ex);
        }
    }

    private async Task WaitForSemaphoreAsync(List<Exception> aggregateExceptions, CancellationToken cancellationToken)
    {
        var totalDelayMs = 0;

        while (totalDelayMs < Constants.EMAIL_SEND_INSTANCE_TIMEOUT)
        {
            if (_semaphore.CurrentCount == Constants.MAX_SMTP_CLIENTS)
                break;

            if (aggregateExceptions.Count > 0)
            {
                var exception = new AggregateException(
                    "One or more fatal exceptions encountered when attempting to send emails",
                    aggregateExceptions);

                _log.LogError(exception, "One or more fatal exceptions encountered when attempting to send emails");

                throw exception;
            }

            await Task.Delay(100, cancellationToken);
            totalDelayMs += 100;
        }

        if (totalDelayMs >= Constants.EMAIL_SEND_INSTANCE_TIMEOUT)
            _log.LogWarning(
                "All messages dequeued but {available} of {expected} semaphore threads are stuck, the system is in an unstable state",
                Constants.MAX_SMTP_CLIENTS - _semaphore.CurrentCount,
                Constants.MAX_SMTP_CLIENTS);
    }

    private async Task SendEmailsAsync(CancellationToken cancellationToken)
    {
        using var emailService = _serviceProvider.GetRequiredService<IEmailService>();

        List<QueueMessage> messages = new();
        bool empty;
        do
        {
            var partialMessagesResponse = await _queueClient
                .ReceiveMessagesAsync(Constants.MAX_DEQUEUE_COUNT, cancellationToken: cancellationToken);

            messages.AddRange(partialMessagesResponse.Value ?? Array.Empty<QueueMessage>());

            empty = partialMessagesResponse.Value?.Length == 0;
        } while (!empty && messages.Count >= Constants.MAX_EMAILS_PER_SMTP_CLIENT);

        foreach (var message in messages)
        {
            await emailService.SendAsync(message.MessageText, cancellationToken);

            await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
        }
    }

    private async Task<int> GetMessageCountAsync(CancellationToken cancellationToken)
    {
        var properties = await _queueClient.GetPropertiesAsync(cancellationToken);
        return properties.Value?.ApproximateMessagesCount ?? 0;
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}