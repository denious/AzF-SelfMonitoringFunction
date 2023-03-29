namespace SelfMonitoringFunction;

public static class Constants
{
    public const string STORAGE_CONNECTION_STRING_NAME = "AzureWebJobsStorage";
    public const string STORAGE_QUEUE_NAME = "delegated-items";
    public const int MAX_SMTP_CLIENTS = 5;
    public const int MAX_EMAILS_PER_SMTP_CLIENT = 250;
    public const int EMAIL_SEND_INSTANCE_TIMEOUT = 5000;
    public const int MAX_DEQUEUE_COUNT = 32;
}