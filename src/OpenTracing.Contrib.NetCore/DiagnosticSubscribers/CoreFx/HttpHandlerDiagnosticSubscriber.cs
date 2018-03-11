using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTracing.Contrib.NetCore.Configuration;
using OpenTracing.Contrib.NetCore.Internal;
using OpenTracing.Propagation;
using OpenTracing.Tag;

namespace OpenTracing.Contrib.NetCore.DiagnosticSubscribers.CoreFx
{
    /// <summary>
    /// Instruments outgoing HTTP calls that use <see cref="HttpClientHandler"/>.
    /// <para/>See https://github.com/dotnet/corefx/blob/master/src/System.Net.Http/src/System/Net/Http/DiagnosticsHandler.cs
    /// <para/>and https://github.com/dotnet/corefx/blob/master/src/System.Net.Http/src/System/Net/Http/DiagnosticsHandlerLoggingStrings.cs
    /// </summary>
    internal sealed class HttpHandlerDiagnosticSubscriber : DiagnosticSubscriberWithObserver
    {
        public const string DiagnosticListenerName = "HttpHandlerDiagnosticListener";

        public const string EventActivity = "System.Net.Http.HttpRequestOut";
        public const string EventActivityStart = EventActivity + ".Start";
        public const string EventActivityStop = EventActivity + ".Stop";
        public const string EventException = "System.Net.Http.Exception";

        private const string PropertiesKey = "ot-Span";

        private readonly PropertyFetcher _activityStart_RequestFetcher = new PropertyFetcher("Request");
        private readonly PropertyFetcher _activityStop_RequestFetcher = new PropertyFetcher("Request");
        private readonly PropertyFetcher _activityStop_ResponseFetcher = new PropertyFetcher("Response");
        private readonly PropertyFetcher _activityStop_RequestTaskStatusFetcher = new PropertyFetcher("RequestTaskStatus");
        private readonly PropertyFetcher _exception_RequestFetcher = new PropertyFetcher("Request");
        private readonly PropertyFetcher _exception_ExceptionFetcher = new PropertyFetcher("Exception");

        private readonly HttpHandlerDiagnosticOptions _options;

        protected override string ListenerName => DiagnosticListenerName;

        public HttpHandlerDiagnosticSubscriber(ILoggerFactory loggerFactory, ITracer tracer, IOptions<CoreFxOptions> options)
            : base(loggerFactory, tracer)
        {
            _options = options?.Value?.HttpHandlerDiagnostic ?? throw new ArgumentNullException(nameof(options));
        }

        protected override void OnNextCore(string eventName, object arg)
        {
            switch (eventName)
            {
                case EventActivityStart:
                    {
                        var request = (HttpRequestMessage)_activityStart_RequestFetcher.Fetch(arg);

                        if (IgnoreRequest(request))
                        {
                            Logger.LogDebug("Ignoring Request {RequestUri}", request.RequestUri);
                            return;
                        }

                        string operationName = _options.OperationNameResolver(request);

                        ISpan span = Tracer.BuildSpan(operationName)
                            .WithTag(Tags.SpanKind.Key, Tags.SpanKindClient)
                            .WithTag(Tags.Component.Key, _options.ComponentName)
                            .WithTag(Tags.HttpMethod.Key, request.Method.ToString())
                            .WithTag(Tags.HttpUrl.Key, request.RequestUri.ToString())
                            .WithTag(Tags.PeerHostname.Key, request.RequestUri.Host)
                            .WithTag(Tags.PeerPort.Key, request.RequestUri.Port)
                            .Start();

                        _options.OnRequest?.Invoke(span, request);

                        if (_options.InjectEnabled?.Invoke(request) ?? true)
                        {
                            Tracer.Inject(span.Context, BuiltinFormats.HttpHeaders, new HttpHeadersInjectAdapter(request.Headers));
                        }

                        // This throws if there's already an item with the same key. We do this for now to get notified of potential bugs.
                        request.Properties.Add(PropertiesKey, span);
                    }
                    break;

                case EventException:
                    {
                        var request = (HttpRequestMessage)_exception_RequestFetcher.Fetch(arg);
                        var exception = (Exception)_exception_ExceptionFetcher.Fetch(arg);

                        if (request.Properties.TryGetValue(PropertiesKey, out object objSpan) && objSpan is ISpan span)
                        {
                            span.SetException(exception);
                        }
                    }
                    break;

                case EventActivityStop:
                    {
                        var request = (HttpRequestMessage)_activityStop_RequestFetcher.Fetch(arg);
                        var response = (HttpResponseMessage)_activityStop_ResponseFetcher.Fetch(arg);
                        var requestTaskStatus = (TaskStatus)_activityStop_RequestTaskStatusFetcher.Fetch(arg);

                        if (request.Properties.TryGetValue(PropertiesKey, out object objSpan) && objSpan is ISpan span)
                        {
                            if (response != null)
                            {
                                span.SetTag(Tags.HttpStatus.Key, (int)response.StatusCode);
                            }

                            if (requestTaskStatus == TaskStatus.Canceled || requestTaskStatus == TaskStatus.Faulted)
                            {
                                span.SetTag(Tags.Error.Key, true);
                            }

                            span.Finish();

                            request.Properties[PropertiesKey] = null;
                        }
                    }
                    break;
            }
        }

        private bool IgnoreRequest(HttpRequestMessage request)
        {
            foreach (Func<HttpRequestMessage, bool> ignore in _options.IgnorePatterns)
            {
                if (ignore(request))
                    return true;
            }

            return false;
        }
    }
}
