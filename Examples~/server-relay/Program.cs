using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

// Minimal relay for games with no dedicated server (peer-to-peer or client-hosted). Games call this service
// instead of Chat Guard, so your cg_live_ key never ships in a game build.
//   CHATGUARD_API_KEY=cg_live_... RELAY_SHARED_SECRET=... dotnet run
// CHATGUARD_BASE_URL is optional (default https://api.chatguard.dev).
// Games send  POST /chat  { "message", "author_id", "channel_type", "language" }  with header X-Relay-Secret.
// author_id is your stable player id, never a real name or email. The reply is the POST /v1/moderate answer as is,
// or the degraded answer below; ChatGuardClient.TryParseResponse turns either into a ModerationResult.
// Without RELAY_SHARED_SECRET anyone can call the relay. In a real game, replace the secret with your own player
// authentication and forward only messages whose sender you have verified.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string apiKey = builder.Configuration["CHATGUARD_API_KEY"] ?? throw new InvalidOperationException("CHATGUARD_API_KEY is required");
string baseUrl = (builder.Configuration["CHATGUARD_BASE_URL"] ?? "https://api.chatguard.dev").TrimEnd('/');
string sharedSecret = builder.Configuration["RELAY_SHARED_SECRET"] ?? string.Empty;

// Null fields are not sent, so a game that sends no language gets the project's default.
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

    // When the call to Chat Guard fails, you choose to deliver the message (fail open) or block it (fail closed).
    // This example fails open: it answers allow with degraded=true and a degraded_reason the Unity package reads as
    // DegradedReason.
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
        // HttpClient's own timeout. When ct is canceled instead, the game hung up and needs no answer.
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
