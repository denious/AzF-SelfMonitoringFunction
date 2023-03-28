using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;

namespace SelfMonitoringFunction;

public class TimedQueueReaderFunction
{
    private readonly EmailServiceSemaphore _emailServiceSemaphore;

    public TimedQueueReaderFunction(EmailServiceSemaphore emailServiceSemaphore)
    {
        _emailServiceSemaphore = emailServiceSemaphore;
    }

    [FunctionName(nameof(TimedQueueReaderFunction))]
    public async Task Run(
        [TimerTrigger("*/10 * * * * *", RunOnStartup = true)]
        TimerInfo timer)
    {
        await _emailServiceSemaphore.SendEmails();
    }
}