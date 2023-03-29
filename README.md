# AzF-SelfMonitoringFunction

## Running Locally

Create a `local.settings.json` file with the contents below:

```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet"
    }
}
```

## Configuration

Key | Description
--- | -----------
STORAGE_CONNECTION_STRING_NAME | Used to reference the `AzureWebJobsStorage` setting
STORAGE_QUEUE_NAME | Used as the main read/write data container for the application's functionality
MAX_SMTP_CLIENTS | Sets the maximum number of concurrent threads that open SMTP client connections
MAX_EMAILS_PER_SMTP_CLIENT | Sets the maximum number of emails to send via a single SMTP connection
EMAIL_SEND_INSTANCE_TIMEOUT | Sets the maximum expected duration of a batch email send operation (i.e., how much will your client take to send MAX_EMAILS_PER_SMTP_CLIENT emails)
MAX_DEQUEUE_COUNT | Sets the maximum number of messages you expect to dequeue in a single operation (as of the writing of this document, no more than 32 is supported by Azure)
