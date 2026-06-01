using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Web.Api.Hubs
{
    public class DevHostHub : Hub
    {
        private readonly IDevHostControl _control;

        public DevHostHub(IDevHostControl control)
        {
            _control = control;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            _control.PublishAllStates();
        }
    }
}
