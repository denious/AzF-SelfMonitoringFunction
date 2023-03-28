using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SelfMonitoringFunction
{
    public class DummyEmailService : IEmailService
    {
        private readonly ILogger<DummyEmailService> _log;
        private readonly Guid _instanceId;


        public DummyEmailService(ILogger<DummyEmailService> log)
        {
            _log = log;
            _instanceId = Guid.NewGuid();
        }

        public Task SendAsync(string body)
        {
            _log.LogInformation("{instanceId}: Sending email", _instanceId);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _log.LogInformation("{instanceId}: Goodbye!", _instanceId);
        }
    }
}