# OpenTelementry.Demo

Live coding workshop with
- [Github codespaces](https://github.com/codespaces) (requires subscription)
- [.NET Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-7.0)
- [OpenTelementry](https://opentelemetry.io/) [middleware](https://opentelemetry.io/docs/instrumentation/net/) with [Honeycomb](https://honeycomb.io/)
- [Grafana k6](https://k6.io) load testing

This session demonstrates the implementation of OpenTelemetry, how traces and spans can are visualised in Honeycomb, and how to prove internal system behaviour using observability.

We begin with this empty repository.

See the `workshop` branch for a functionally complete version.

# Process

## 1. Bootstrap

Launch new codespaces on this repo (blank starting point).

Initialise a new dotnet minimal WebAPI

```bash
dotnet new webapi -o Demo.Web -minimal
dotnet new gitignore
cd Demo.Web
```

Remove the redundant code - we just want to start with a basic `hello world` app.

```csharp
// Program.cs

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/", () => "hello world");

app.Run();
```

## 2. OpenTelemetry middleware

Install Honeycomb and OTel extensions.

```bash
dotnet add package Honeycomb.OpenTelemetry
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package Honeycomb.OpenTelemetry.CommonInstrumentations --prerelease
```

Configure honeycomb (`appsettings.json`)

```json
    "Honeycomb": {
        "ServiceName": "dotnet-cop-demo",
        "ApiKey": "<your API key here>"
    }
```

Add Honeycomb middleware to `Program.cs`

```csharp
var honeycombOptions = builder.Configuration.GetHoneycombOptions();
builder.Services.AddOpenTelemetry().WithTracing(otelBuilder =>
{
    otelBuilder
        .AddHoneycomb(honeycombOptions)
        .AddCommonInstrumentations();
});
```

This is sufficient to demonstrate the out-of-the-box behaviour of Honeycomb.

- Run the web server with `dotnet run`
- Log in to your honeycomb account
- Hit the hosted endpoint via web browser or `curl`
- Observe the new traffic
  - Single span within the trace
  - Make a note of the fields available for query

## 3. Extend app behaviour

Extend the app behaviour - introduce an external query to wikipedia.

Add a new endpoint accepting a name, and mapping it to an action. Use intellisense to auto-import relevant dependencies.

```csharp
app.MapGet("/{name}", Lookup);
```

```csharp
static async Task<string> Lookup([FromRoute]string name)
{
    using HttpClient client = new();
    client.DefaultRequestHeaders.Add("User-Agent", "Workshop-Demo-Client");
    var response = await client.GetStringAsync($"https://en.wikipedia.org/api/rest_v1/page/summary/{name}?redirect=false");
    return JsonNode.Parse(response)?["extract"]?.GetValue<string>() ?? "";
}
```

Add the singleton to `Program.cs` for consumption in your actions.

```csharp
builder.Services.AddSingleton(TracerProvider.Default.GetTracer(honeycombOptions.ServiceName));
```

Demonstrate how this external request appears in Honeycomb.

## 4. Custom spans

Extend the `JsonNode.Parse` line with a custom span, with custom attributes:

```csharp
using var span = tracer.StartActiveSpan("app.parser");
var snippet = JsonNode.Parse(response)?["extract"]?.GetValue<string>() ?? "";
span.SetAttribute("app.parser.route", name);
span.SetAttribute("app.parser.resultlength", snippet.Length);
return snippet;
```

Demonstrate how this new span appears in Honeycomb.

## 5. Add Caching

Extend the Wikipedia query behavior with caching; set the cache expiration to __5 seconds__ to be able to demonstrate the caching behaviour.

```csharp
var response = await memoryCache.GetOrCreateAsync<string>($"wiki.{name}", async cache => {
    cache.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
    using HttpClient client = new();
    client.DefaultRequestHeaders.Add("User-Agent", "Workshop-Demo-Client");
    var result = await client.GetStringAsync($"https://en.wikipedia.org/api/rest_v1/page/summary/{name}?redirect=false");
    cache.SetValue(result);
    return result;
});
```

Demonstrate this by submitting a few requests in quick success.

- Visualise `COUNT`
- Visualise `MAX(duration_ms)`

You should see some _very fast_ responses, which do not contain the external HTTP request.

## 6. k6 Load Testing

Install k6

```bash
sudo gpg -k
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update
sudo apt-get install k6
```

Create a new `test.js` file

```js
import http from 'k6/http';
import { sleep } from 'k6';

export default function () {
  http.get('<your endpoint here>/.NET');
  sleep(1);
}
```

Execute this test with 5 concurrent virtual users; gather 30 seconds of performance data.

```bash
k6 run --vus 5 --duration 30s test.ts
```

Observe the result of this in Honeycomb; demonstrate grouping by traces with and without cache hits, and demonstrate the user of histograms to visualise high volume datasets.