using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Common
{
    public class ServiceResilienceState
    {
        public DateTime LastMissingTableWarningUtc { get; set; } = DateTime.MinValue;
        public DateTime LastDbUnreachableWarningUtc { get; set; } = DateTime.MinValue;
    }

    public static class BackgroundServiceResilienceHelper
    {
        public static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan WarningThrottleInterval = TimeSpan.FromHours(1);

        public static bool IsMissingTableException(Exception? ex)
        {
            var current = ex;
            while (current != null)
            {
                if (current is SqlException sqlEx)
                {
                    if (sqlEx.Number == 208)
                    {
                        return true;
                    }

                    foreach (SqlError err in sqlEx.Errors)
                    {
                        if (err.Number == 208)
                        {
                            return true;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(current.Message) &&
                    current.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        public static bool IsDatabaseUnreachableException(Exception? ex)
        {
            var current = ex;
            while (current != null)
            {
                if (current is SqlException sqlEx)
                {
                    if (sqlEx.Number is -1 or 2 or 53 or 258 or 4060 or 10054 or 10060 or 10061 || sqlEx.Class >= 20)
                    {
                        return true;
                    }

                    foreach (SqlError err in sqlEx.Errors)
                    {
                        if (err.Number is -1 or 2 or 53 or 258 or 4060 or 10054 or 10060 or 10061 || err.Class >= 20)
                        {
                            return true;
                        }
                    }

                    if (!string.IsNullOrEmpty(sqlEx.Message) && (
                        sqlEx.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                        sqlEx.Message.Contains("server was not found", StringComparison.OrdinalIgnoreCase) ||
                        sqlEx.Message.Contains("not accessible", StringComparison.OrdinalIgnoreCase) ||
                        sqlEx.Message.Contains("cannot open database", StringComparison.OrdinalIgnoreCase) ||
                        sqlEx.Message.Contains("Connection Timeout", StringComparison.OrdinalIgnoreCase) ||
                        sqlEx.Message.Contains("Login failed", StringComparison.OrdinalIgnoreCase) ||
                        sqlEx.Message.Contains("The service is currently unavailable", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
                else if (current is SocketException || current is TimeoutException)
                {
                    return true;
                }
                else if (!string.IsNullOrEmpty(current.Message) && (
                    current.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("server was not found", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("not accessible", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("cannot open database", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        public static async Task HandleLoopExceptionAsync(
            Exception ex,
            string serviceName,
            ILogger logger,
            ServiceResilienceState state,
            CancellationToken stoppingToken)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;

            if (IsMissingTableException(ex))
            {
                if (now - state.LastMissingTableWarningUtc >= WarningThrottleInterval)
                {
                    state.LastMissingTableWarningUtc = now;
                    logger.LogWarning(ex, "[{ServiceName}] [{CorrelationId}] Required database table is missing (SQL error 208): {Message}. Skipping this run.", serviceName, correlationId, ex.Message);
                }
            }
            else if (IsDatabaseUnreachableException(ex))
            {
                if (now - state.LastDbUnreachableWarningUtc >= WarningThrottleInterval)
                {
                    state.LastDbUnreachableWarningUtc = now;
                    logger.LogWarning(ex, "[{ServiceName}] [{CorrelationId}] Database is unreachable: {Message}. Background service will retry in 60s.", serviceName, correlationId, ex.Message);
                }
            }
            else
            {
                logger.LogError(ex, "[{ServiceName}] [{CorrelationId}] Unhandled exception in {ServiceName} execution loop.", serviceName, correlationId, serviceName);
            }

            try
            {
                await Task.Delay(FailureDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful cancellation during backoff
            }
        }
    }
}
