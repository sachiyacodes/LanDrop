// Helpers/SerilogExtensions.cs
// Bridges Serilog into Microsoft.Extensions.Logging.ILoggerFactory

using Microsoft.Extensions.Logging;
using Serilog;

namespace LanDrop
{
    internal static class SerilogExtensions
    {
        /// <summary>
        /// Adds a Serilog provider to the ILoggerFactory so all services
        /// that receive ILogger<T> route through Serilog's file sink.
        /// </summary>
        public static ILoggerFactory AddSerilog(
            this ILoggerFactory factory,
            Serilog.ILogger logger)
        {
            factory.AddProvider(new Serilog.Extensions.Logging.SerilogLoggerProvider(logger));
            return factory;
        }
    }
}
