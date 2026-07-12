using System.Text;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

// ─────────────────────────────────────────────────────────────────────────────
// Local dev reverse proxy: serves the Story and Localizer web apps from ONE origin
// so they share a single IndexedDB, exactly like production on https://deusald.github.io.
//
// In production both apps live on the same origin under different sub-paths
// (/DeusaldStory/ and /DeusaldLocalizer/), so the browser gives them one shared
// 'Deusald' IndexedDB. On localhost each dev server runs on its own port, and the
// port is part of the origin — so without this proxy they get two SEPARATE databases
// and the Story app can't see the 'loc:' projects created in the Localizer app.
//
// Mounts:
//   http://localhost:8080/        -> Story     dev server (localhost:5125)
//   http://localhost:8080/loc/    -> Localizer dev server (localhost:5047)
//
// Run all three (Story WebApp, Localizer WebApp, this proxy), then browse to
// http://localhost:8080 for Story and http://localhost:8080/loc/ for the Localizer.
// ─────────────────────────────────────────────────────────────────────────────

const string story_Address = "http://localhost:5125"; // Story    WebApp launch profile 'http'
const string loc_Address   = "http://localhost:5047"; // Localizer WebApp launch profile 'http'
const string loc_Prefix    = "/loc";                  // sub-path the Localizer is mounted under

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:8080");

RouteConfig[] routes =
[
    // Localizer under /loc/* — strip the prefix before forwarding, and rewrite its
    // <base href> so the app's relative assets (_framework, css, …) resolve back
    // through /loc/ and route here again. Order 1 so it wins over the root catch-all.
    new()
    {
        RouteId = "loc",
        ClusterId = "loc",
        Order = 1,
        Match = new RouteMatch { Path = "/loc/{**catch-all}" },
    },
    // Everything else is the Story app at the origin root (base href stays "/").
    new()
    {
        RouteId = "story",
        ClusterId = "story",
        Order = 2,
        Match = new RouteMatch { Path = "/{**catch-all}" },
    },
];

ClusterConfig[] clusters =
[
    new()
    {
        ClusterId = "story",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["d1"] = new() { Address = story_Address },
        },
    },
    new()
    {
        ClusterId = "loc",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["d1"] = new() { Address = loc_Address },
        },
    },
];

builder.Services
       .AddReverseProxy()
       .LoadFromMemory(routes, clusters)
       .AddTransforms(context =>
        {
            if (context.Route.RouteId != "loc") return;

            // Strip the /loc mount prefix so the upstream dev server sees root-relative paths.
            context.AddPathRemovePrefix(loc_Prefix);

            // Ask upstream for uncompressed bytes so the HTML rewrite below can read the body
            // as text without having to inflate gzip/brotli first.
            context.AddRequestTransform(transform =>
            {
                transform.ProxyRequest.Headers.Remove("Accept-Encoding");
                return ValueTask.CompletedTask;
            });

            // Rewrite the Localizer's <base href="/"> to "/loc/" in the served index.html so all
            // of its relative asset requests come back through /loc/ and are routed to :5047.
            context.AddResponseTransform(async transform =>
            {
                HttpResponseMessage? upstream = transform.ProxyResponse;
                if (upstream is null) return;

                string? mediaType = upstream.Content.Headers.ContentType?.MediaType;
                if (mediaType != "text/html") return; // only the HTML document needs rewriting

                transform.SuppressResponseBody = true;

                string html = await upstream.Content.ReadAsStringAsync();
                html = html.Replace("<base href=\"/\" />", "<base href=\"/loc/\" />");

                byte[] bytes = Encoding.UTF8.GetBytes(html);
                transform.HttpContext.Response.ContentLength = bytes.Length;
                await transform.HttpContext.Response.Body.WriteAsync(bytes);
            });
        });

WebApplication app = builder.Build();

// Nicety: /loc (no trailing slash) → /loc/ so the base href and relative assets line up.
// Exact-string match on Request.Path so it does NOT also catch /loc/ (which would loop) —
// unlike endpoint routing, PathString compares the trailing slash literally.
app.Use((ctx, next) =>
{
    if (ctx.Request.Path == loc_Prefix)
    {
        ctx.Response.Redirect(loc_Prefix + "/");
        return Task.CompletedTask;
    }
    return next();
});

app.MapReverseProxy();

app.Logger.LogInformation("Dev proxy on http://localhost:8080  →  Story '/'  |  Localizer '/loc/'");
app.Logger.LogInformation("Start the Story WebApp ({Story}) and Localizer WebApp ({Loc}) too.", story_Address, loc_Address);

app.Run();
