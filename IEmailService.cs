using System;
using System.Threading.Tasks;

namespace SelfMonitoringFunction;

public interface IEmailService : IDisposable
{
    Task SendAsync(string body);
}