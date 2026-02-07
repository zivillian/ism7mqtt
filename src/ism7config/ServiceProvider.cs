using LuCon.Common.PortalModel;
using Microsoft.Extensions.DependencyInjection;
using Wolf.SocketServer.SocketServerBase;

namespace ism7config;

public class ServiceProvider : IServiceProvider
{
    private readonly BundleQueueWorker _bundleQueueWorker;
    private readonly ParameterStore _parameterStore;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IUnitOfWork _unitOfWork;

    public ServiceProvider(BundleQueueWorker bundleQueueWorker, ParameterStore parameterStore, IUnitOfWork unitOfWork)
    {
        _bundleQueueWorker = bundleQueueWorker;
        _parameterStore = parameterStore;
        _serviceScopeFactory = new ServiceScopeFactory(this);
        _unitOfWork = unitOfWork;
    }

    public object GetService(Type serviceType)
    {
        return GetInstance(serviceType);
    }

    public object GetInstance(Type serviceType)
    {
        if (serviceType.IsAssignableFrom(typeof(BundleQueueWorker))) return _bundleQueueWorker;
        if (serviceType.IsAssignableFrom(typeof(ParameterStore))) return _parameterStore;
        if (serviceType.IsAssignableFrom(typeof(IServiceScopeFactory))) return _serviceScopeFactory;
        if (serviceType.IsAssignableFrom(typeof(IUnitOfWork))) return _unitOfWork;
        throw new NotImplementedException();
    }

    public object GetInstance(Type serviceType, string key)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<object> GetAllInstances(Type serviceType)
    {
        throw new NotImplementedException();
    }

    public TService GetInstance<TService>()
    {
        return (TService)GetInstance(typeof(TService));
    }

    public TService GetInstance<TService>(string key)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<TService> GetAllInstances<TService>()
    {
        throw new NotImplementedException();
    }
}