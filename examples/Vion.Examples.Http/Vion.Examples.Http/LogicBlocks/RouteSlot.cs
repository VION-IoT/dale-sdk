using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Http.LogicBlocks
{
    /// <summary>
    ///     One route <see cref="HttpSimServer" /> serves: a method and a path, and the status, content type and body it
    ///     answers them with. Every field is editable, so a route can be turned into a fault — a 503, a wrong content type,
    ///     a malformed document — while a client is talking to it.
    /// </summary>
    /// <remarks>
    ///     A service-bearing component: each slot forms its own service named after the property that holds it
    ///     (<c>Route1</c>, <c>Route2</c>, …), so the <c>VisibleWhen</c> predicates below resolve against the slot's own
    ///     properties. How many slots exist is decided at config time by the block's <c>RouteSlotCount</c> instantiation
    ///     parameter.
    /// </remarks>
    public class RouteSlot
    {
        [ServiceProperty(Title = "Enabled", Description = "Off by default so unconfigured slots serve nothing.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 10)]
        public bool Enabled { get; set; }

        [ServiceProperty(Title = "Method")]
        [Presentation(DisplayName = "Method", Group = PropertyGroup.Configuration, Order = 20, VisibleWhen = "Enabled")]
        public RequestMethod Method { get; set; } = RequestMethod.Get;

        [ServiceProperty(Title = "Path", Description = "Starts with '/', carries no query string, and matches case-sensitively: /api/status does not answer /API/status or /api/status/.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 30, VisibleWhen = "Enabled")]
        public string Path { get; set; } = "/";

        [ServiceProperty(Title = "Status code", Minimum = 200, Maximum = 599, Description = "A final status, from 200 to 599.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 40, VisibleWhen = "Enabled")]
        public int StatusCode { get; set; } = 200;

        [ServiceProperty(Title = "Content type", Description = "Printable ASCII only. Leave empty to send no Content-Type.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 50, VisibleWhen = "Enabled")]
        public string ContentType { get; set; } = "application/json";

        [ServiceProperty(Title = "Body", Description = "Sent as UTF-8. A 204 or 304 is sent without it, and so is the answer to a HEAD.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 60, VisibleWhen = "Enabled", UiHint = UiHints.Multiline)]
        public string Body { get; set; } = string.Empty;

        [ServiceProperty(Title = "Hits", Description = "Requests this route has answered.")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public int HitCount { get; internal set; }

        [ServiceProperty(Title = "Last hit")]
        [Presentation(Group = PropertyGroup.Status, Format = Formats.Relative)]
        public DateTime? LastHitAt { get; internal set; }

        [ServiceProperty(Title = "Status", Description = "What the route is serving, or why it serves nothing.")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public string Status { get; internal set; } = string.Empty;
    }
}
