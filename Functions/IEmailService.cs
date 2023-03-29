using System;
using System.Threading;
using System.Threading.Tasks;

namespace SelfMonitoringFunction;

public interface IEmailService : IDisposable
{
    Task SendAsync(string body, CancellationToken cancellationToken);
}