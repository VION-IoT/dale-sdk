using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Vion.Dale.DevHost.Web.Services;

namespace Vion.Dale.DevHost.Web.Api.Hubs
{
    public class DevHostHub : Hub
    {
        private readonly IDevHostStateProvider _stateProvider;

        public DevHostHub(IDevHostStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            await _stateProvider.PublishAllStatesAsync();
        }
    }
}
