using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

// Minimal relay for client-authoritative games (no dedicated server): clients never see the Chat Guard key.
//   CHATGUARD_API_KEY=cg_live_... RELAY_SHARED_SECRET=... dotnet run
// CHATGUARD_BASE_URL is optional and defaults to the Chat Guard API, https://api.chatguard.dev.
// Clients send  POST /chat  { "message", "author_id", "channel_type", "language" }  with header X-Relay-Secret.
// In a real game, replace the shared secret with your session/auth token validation and forward only
// after checking the sender is who they claim to be.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string apiKey = builder.Configuration["CHATGUARD_API_KEY"] ?? throw new InvalidOperationException("CHATGUARD_API_KEY is required");
string baseUrl = (builder.Configuration["CHATGUARD_BASE_URL"] ?? "https://api.chatguard.dev").TrimEnd('/');
string sharedSecret = builder.Configuration["RELAY_SHARED_SECRET"] ?? string.Empty;

// Fields the game left out are not sent, so Chat Guard applies the project's defaults (its language, for example).
var json = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

builder.Services.AddHttpClient("chatguard", http =>
{
    http.BaseAddress = new Uri(baseUrl);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    http.Timeout = TimeSpan.FromSeconds(3);
});

WebApplication app = builder.Build();

app.MapPost("/chat", async (RelayRequest request, HttpContext http, IHttpClientFactory clients, CancellationToken ct) =>
{
    if (sharedSecret.Length > 0 && http.Request.Headers["X-Relay-Secret"] != sharedSecret)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 2000)
    {
        return Results.BadRequest(new { error = "message is required (max 2000 chars)" });
    }

    var payload = new
    {
        message = request.Message,
        author = new { id = request.AuthorId ?? "anonymous" },
        channel = new { type = request.ChannelType ?? "global", language = request.Language },
        request_id = Guid.NewGuid().ToString(),
    };

    // Fail open or closed is your product decision; here we fail open with a marker the client can show. The reasons
    // are ones the Unity package reads (DegradedReason): upstream_rate_limit for a 429, timeout when no answer came
    // within the 3 s above, upstream for any other error.
    try
    {
        using HttpResponseMessage response = await clients.CreateClient("chatguard").PostAsJsonAsync("/v1/moderate", payload, json, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string reason = response.StatusCode == HttpStatusCode.TooManyRequests ? "upstream_rate_limit" : "upstream";
            return Degraded(reason, (int)response.StatusCode);
        }

        return Results.Content(body, "application/json");
    }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
    {
        return Degraded("timeout", null);
    }
    catch (HttpRequestException ex)
    {
        return Degraded("upstream", ex.StatusCode is { } status ? (int)status : null);
    }
});

app.MapGet("/healthz", () => Results.Text("ok"));
app.Run();

static IResult Degraded(string reason, int? status)
{
    return Results.Json(new { action = "allow", degraded = true, degraded_reason = reason, status }, statusCode: 200);
}

internal sealed record RelayRequest(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("author_id")] string? AuthorId,
    [property: JsonPropertyName("channel_type")] string? ChannelType,
    [property: JsonPropertyName("language")] string? Language);
