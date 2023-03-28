using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues.Models;

namespace SelfMonitoringFunction;

public class EmailServiceSemaphore
{
    private readonly ILogger<TimedQueueReaderFunction> _log;
    private readonly QueueClient _queueClient;
    private readonly IServiceProvider _serviceProvider;

    public EmailServiceSemaphore(
        ILogger<TimedQueueReaderFunction> log,
        QueueClient queueClient,
        IServiceProvider serviceProvider)
    {
        _log = log;
        _queueClient = queueClient;
        _serviceProvider = serviceProvider;
    }

    public async Task SendEmails()
    {
        var semaphore = new SemaphoreSlim(Constants.MAX_SMTP_CLIENTS, Constants.MAX_SMTP_CLIENTS);

        var minMessageCount = await GetMessageCountAsync();

        _log.LogInformation("Found {minMessageCount} emails", minMessageCount);

        while (minMessageCount > 0)
        {
            await semaphore.WaitAsync(-1);

            _ = Task.Run(async () =>
            {
                await SendEmailsAsync();
                semaphore.Release();
            });

            minMessageCount = await GetMessageCountAsync();
        }
    }

    private async Task SendEmailsAsync()
    {
        try
        {
            var totalMessagesProcessed = 0;

            using var emailService = _serviceProvider.GetRequiredService<IEmailService>();

            var instanceId = Guid.NewGuid();

            List<QueueMessage> messages = new();
            bool empty;
            do
            {
                var partialMessagesResponse = await _queueClient.ReceiveMessagesAsync(32);
                messages.AddRange(partialMessagesResponse.Value ?? Array.Empty<QueueMessage>());

                empty = partialMessagesResponse.Value?.Length == 0;
            } while (!empty && messages.Count < 251);

            foreach (var message in messages)
            {
                await emailService.SendAsync(message.MessageText);

                //_log.LogInformation("Got message ID {messageId}", message.MessageId);

                await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt);
                totalMessagesProcessed++;
            }

            if (totalMessagesProcessed == 0)
                return;

            _log.LogInformation("{instance}: {messageCount} emails sent!", instanceId, totalMessagesProcessed);
        }
        catch (Exception e)
        {
            _log.LogError(e, "Failed sender instance");
        }
    }

    private async Task<int> GetMessageCountAsync()
    {
        var properties = await _queueClient.GetPropertiesAsync();
        return properties.Value?.ApproximateMessagesCount ?? 0;
    }
}