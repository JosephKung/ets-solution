using Ets.Application;
using Ets.Application.Interfaces;
using Ets.Infrastructure;
using Ets.Infrastructure.BackgroundServices;
using Ets.Infrastructure.ExternalClients.TeamPlus;
using Ets.Infrastructure.ExternalClients.Voice;
using Ets.Infrastructure.Logging;
using Ets.Infrastructure.Metrics;
using Ets.Infrastructure.Outbox.Handlers;
using Ets.Infrastructure.Security;
using Ets.Infrastructure.Webhooks;
using Ets.WebApi.Filters;
using Ets.WebApi.Logging;
using Ets.WebApi.Middlewares;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("ETS WebApi starting up...");

    var builder = WebApplication.CreateBuilder(args);

    builder.AddEtsSerilog();
    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "ETS 緊急應變智能通報平台 API",
            Version = "v1"
        }));
    builder.Services.AddSignalR();

    // ── team+ Clients ─────────────────────────────────────────────
    builder.Services.AddTeamPlusSystemClient(builder.Configuration);  // ← 補上（含 ITeamPlusSystemClient）
    builder.Services.AddTeamPlusChannelClient(builder.Configuration);
    builder.Services.AddTeamPlusSsoClient(builder.Configuration);

    // ── Flex / Outbox ─────────────────────────────────────────────
    builder.Services.AddScoped<IFlexMessageBuilder, FlexMessageBuilder>();
    builder.Services.AddVirtualAccountKeyEncryptor();
    builder.Services.AddOutboxDispatcher();        // 1.3.15：11 個 Handler + OutboxDispatcherWorker
    builder.Services.AddReadStatusPollingWorker(); // 1.3.12：已讀輪詢

    // ── Voice Fallback ────────────────────────────────────────────
    builder.Services.AddVoiceFallback(builder.Configuration); // VoiceApiClient + VoiceFallbackWorker
    builder.Services.AddSingleton<IVoiceQueueMetrics, VoiceQueueMetrics>();

    // ── Webhook Auth ──────────────────────────────────────────────
    builder.Services.AddScoped<ITeamPlusSignatureVerifier, TeamPlusSignatureVerifier>();
    builder.Services.AddScoped<VoiceWebhookAuthFilter>();      // ServiceFilter 需要 DI 註冊

    // ── Webhook Handlers ──────────────────────────────────────────
    builder.Services.AddScoped<PostbackWebhookHandler>();
    builder.Services.AddScoped<VoiceCallbackHandler>();

    // ── Dashboard Notifier（M6 前用 Null 佔位，M6 實作 SignalR Hub 後替換）──
    builder.Services.AddScoped<IDashboardNotifier, NullDashboardNotifier>();
	// CheckIn（M5 1.5.1）
	builder.Services.AddScoped<CheckInUseCase>();
	builder.Services.AddSingleton<IQrHmacKeyProvider, QrHmacKeyProvider>();
    var app = builder.Build();

    // ── 允許 Request Body 重複讀取（Webhook HMAC 驗證需要）───────
    app.Use(async (context, next) =>
    {
        context.Request.EnableBuffering();
        await next();
    });

    app.UseMiddleware<RequestLoggingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "ETS API v1");
            o.RoutePrefix = string.Empty;
        });
    }

    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseMiddleware<HisApiKeyMiddleware>();
    app.UseAuthorization();
    app.MapControllers();

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = WriteHealthResponse
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = WriteHealthResponse
    });
    app.MapHealthChecks("/health/startup", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("startup"),
        ResponseWriter = WriteHealthResponse
    });

    Log.Information("ETS WebApi started successfully");
    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "ETS WebApi terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static Task WriteHealthResponse(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    var result = new
    {
        status = report.Status.ToString(),
        duration = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            duration = e.Value.Duration.TotalMilliseconds,
            data = e.Value.Data,
            error = e.Value.Exception?.Message
        })
    };
    return ctx.Response.WriteAsync(
        JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
}

public partial class Program { }

/// <summary>
/// IDashboardNotifier Null 實作（M6 前佔位用）
/// M6 實作 DashboardHub 後，將此替換為真實 SignalR 實作
/// </summary>
internal sealed class NullDashboardNotifier : IDashboardNotifier
{
    public Task NotifyStatsChangedAsync(string eventId, CancellationToken ct = default)
        => Task.CompletedTask;
}
