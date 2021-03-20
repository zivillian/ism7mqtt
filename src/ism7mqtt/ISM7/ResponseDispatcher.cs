using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt
{
    public class ResponseDispatcher
    {
        private readonly ConcurrentBag<ResponseHandler> _handlers = new ConcurrentBag<ResponseHandler>();
        private readonly ConcurrentDictionary<ResponseHandler, object> _oneShotHandler = new ConcurrentDictionary<ResponseHandler, object>();

        public void Subscribe(Predicate<IResponse> predicate, Func<IResponse, CancellationToken, Task> handler)
        {
            _handlers.Add(new ResponseHandler(predicate, handler));
        }

        public void SubscribeOnce(Predicate<IResponse> predicate, Func<IResponse, CancellationToken, Task> handler)
        {
            _oneShotHandler.TryAdd(new ResponseHandler(predicate, handler), null);
        }

        public async Task DispatchAsync(IResponse response, CancellationToken cancellationToken)
        {
            foreach (var handler in _oneShotHandler.Keys)
            {
                if (handler.CanHandle(response))
                {
                    _oneShotHandler.TryRemove(handler, out _);
                    await handler.HandleAsync(response, cancellationToken);
                }
            }
            await Task.WhenAll(_handlers.Select(x => x.HandleAsync(response, cancellationToken)));
        }

        private class ResponseHandler
        {
            private readonly Predicate<IResponse> _predicate;

            private readonly Func<IResponse, CancellationToken, Task> _handler;

            public ResponseHandler(Predicate<IResponse> predicate, Func<IResponse, CancellationToken, Task> handler)
            {
                _predicate = predicate;
                _handler = handler;
            }

            public bool CanHandle(IResponse response)
            {
                return _predicate(response);
            }

            public Task HandleAsync(IResponse response, CancellationToken cancellationToken)
            {
                if (!_predicate(response)) return Task.CompletedTask;
                return _handler(response, cancellationToken);
            }
        }
    }
}