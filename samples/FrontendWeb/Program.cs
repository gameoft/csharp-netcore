using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shared;

namespace Samples.FrontendWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args)
        {
            return WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseUrls(Constants.FrontendUrl)
                .ConfigureServices(services =>
                {
                    // Registers and starts Zipkin (see Shared.ZipkinService)
                    services.AddZipkin();

                    // Enables OpenTracing instrumentation for ASP.NET Core, CoreFx, EF Core
                    services.AddOpenTracing();
                })
                .Build();
        }
    }
}
