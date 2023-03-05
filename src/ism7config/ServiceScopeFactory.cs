using Microsoft.Extensions.DependencyInjection;

namespace ism7config;

public class ServiceScopeFactory : IServiceScopeFactory, IServiceScope
{
    public ServiceScopeFactory(IServiceProvider provider)
    {
        ServiceProvider = provider;
    }

    public IServiceScope CreateScope()
    {
        return this;
    }

    public void Dispose()
    {
    }

    public IServiceProvider ServiceProvider { get; }
}