using System.Threading.Tasks;
using Vion.Dale.DevHost.Web.Services;
using Microsoft.AspNetCore.SignalR;

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