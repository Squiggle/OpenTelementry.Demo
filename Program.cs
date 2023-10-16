using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
// o11y
var honeycombOptions = builder.Configuration.GetHoneycombOptions();
builder.Services.AddOpenTelemetry().WithTracing(otelBuilder =>
{
    otelBuilder
        .AddHoneycomb(honeycombOptions)
        .AddCommonInstrumentations();
});
builder.Services.AddSingleton(TracerProvider.Default.GetTracer(honeycombOptions.ServiceName));
// caching
builder.Services.AddMemoryCache();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/{name}", Lookup);

app.Run();


static async Task<string> Lookup([FromRoute]string name, Tracer tracer, IMemoryCache memoryCache)
{
    var response = await memoryCache.GetOrCreateAsync<string>($"wiki.{name}", async cache => {
        cache.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
        using HttpClient client = new();
        client.DefaultRequestHeaders.Add("User-Agent", "Workshop-Demo-Client");
        var result = await client.GetStringAsync($"https://en.wikipedia.org/api/rest_v1/page/summary/{name}?redirect=false");
        cache.SetValue(result);
        return result;
    });

    using var span = tracer.StartActiveSpan("app.parser");
    var snippet = JsonNode.Parse(response)?["extract"]?.GetValue<string>() ?? "";
    span.SetAttribute("app.parser.route", name);
    span.SetAttribute("app.parser.resultlength", snippet.Length);
    return snippet;
}