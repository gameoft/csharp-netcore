using Microsoft.Extensions.Logging;
using OpenTracing.Util;

namespace OpenTracing.Contrib.NetCore.Logging
{
    /// <summary>
    /// The provider for the <see cref="OpenTracingLogger"/>.
    /// </summary>
    [ProviderAlias("OpenTracing")]
    internal class OpenTracingLoggerProvider : ILoggerProvider
    {
        private readonly ITracer _tracer;

        public OpenTracingLoggerProvider()
        {
            // HACK: We can't use Dependency Injection here because this would lead to a StackOverflowException
            // if the ITracer needs a ILoggerFactory.
            _tracer = GlobalTracer.Instance;
        }

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
        {
            return new OpenTracingLogger(_tracer, categoryName);
        }

        public void Dispose()
        {
        }
    }
}
