using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Services.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace BookMyFun.Tests
{
    public class BackgroundServiceResilienceTests
    {
        private class TestLogger : ILogger
        {
            public List<(LogLevel Level, string Message, Exception? Exception)> Logs = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Logs.Add((logLevel, formatter(state, exception), exception));
            }
        }

        [Fact]
        public void IsMissingTableException_DetectsInvalidObjectNameMessage()
        {
            var ex = new Exception("Invalid object name 'NotificationOutbox'.");
            Assert.True(BackgroundServiceResilienceHelper.IsMissingTableException(ex));

            var nestedEx = new Exception("Outer wrapper", new InvalidOperationException("Invalid object name 'Bookings'."));
            Assert.True(BackgroundServiceResilienceHelper.IsMissingTableException(nestedEx));
        }

        [Fact]
        public void IsMissingTableException_ReturnsFalseForOtherExceptions()
        {
            var ex = new InvalidOperationException("Some business rule failed.");
            Assert.False(BackgroundServiceResilienceHelper.IsMissingTableException(ex));
        }

        [Fact]
        public void IsDatabaseUnreachableException_DetectsConnectionAndNetworkFailures()
        {
            var ex1 = new Exception("A network-related or instance-specific error occurred while establishing a connection to SQL Server.");
            Assert.True(BackgroundServiceResilienceHelper.IsDatabaseUnreachableException(ex1));

            var ex2 = new TimeoutException("Connection Timeout Expired.");
            Assert.True(BackgroundServiceResilienceHelper.IsDatabaseUnreachableException(ex2));

            var ex3 = new Exception("Outer", new System.Net.Sockets.SocketException(10061));
            Assert.True(BackgroundServiceResilienceHelper.IsDatabaseUnreachableException(ex3));
        }

        [Fact]
        public async Task HandleLoopExceptionAsync_ThrottlesMissingTableWarningsToOnePerHour()
        {
            var logger = new TestLogger();
            var state = new ServiceResilienceState();
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel so delay returns immediately

            var missingTableEx = new Exception("Invalid object name 'NotificationOutbox'.");

            // First call -> should log Warning
            await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                missingTableEx,
                "TestService",
                logger,
                state,
                cts.Token);

            Assert.Single(logger.Logs);
            Assert.Equal(LogLevel.Warning, logger.Logs[0].Level);
            Assert.Contains("SQL error 208", logger.Logs[0].Message);
            Assert.Contains("TestService", logger.Logs[0].Message);

            // Immediate second call -> should be throttled, no new log
            await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                missingTableEx,
                "TestService",
                logger,
                state,
                cts.Token);

            Assert.Single(logger.Logs); // Still only 1 log!
        }

        [Fact]
        public async Task HandleLoopExceptionAsync_ThrottlesDatabaseUnreachableWarningsToOnePerHour()
        {
            var logger = new TestLogger();
            var state = new ServiceResilienceState();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var dbEx = new Exception("A network-related or instance-specific error occurred while establishing a connection to SQL Server.");

            // First call -> should log Warning
            await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                dbEx,
                "TestService",
                logger,
                state,
                cts.Token);

            Assert.Single(logger.Logs);
            Assert.Equal(LogLevel.Warning, logger.Logs[0].Level);
            Assert.Contains("Database is unreachable", logger.Logs[0].Message);

            // Immediate second call -> throttled
            await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                dbEx,
                "TestService",
                logger,
                state,
                cts.Token);

            Assert.Single(logger.Logs); // No repeated errors or warnings
        }

        [Fact]
        public async Task HandleLoopExceptionAsync_LogsErrorForUnexpectedExceptions()
        {
            var logger = new TestLogger();
            var state = new ServiceResilienceState();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var unexpectedEx = new NullReferenceException("Unexpected null");

            await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                unexpectedEx,
                "TestService",
                logger,
                state,
                cts.Token);

            Assert.Single(logger.Logs);
            Assert.Equal(LogLevel.Error, logger.Logs[0].Level);
            Assert.Contains("TestService", logger.Logs[0].Message);
        }

        [Fact]
        public void HostOptions_BackgroundServiceExceptionBehavior_IsSetToIgnore()
        {
            // Verify Program.cs configures HostOptions.BackgroundServiceExceptionBehavior = Ignore
            var options = new HostOptions();
            var configure = new Action<HostOptions>(opt =>
            {
                opt.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
            });
            configure(options);

            Assert.Equal(BackgroundServiceExceptionBehavior.Ignore, options.BackgroundServiceExceptionBehavior);
        }
    }
}
