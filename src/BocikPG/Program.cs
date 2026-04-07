using BocikPG;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using Lavalink4NET.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// ============================================================================
// Configuration
// ============================================================================
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.Configure<LavalinkOptions>(builder.Configuration.GetSection("Lavalink"));
builder.Services.Configure<PingOptions>(builder.Configuration.GetSection("Ping"));

// ============================================================================
// Discord Client Builder
// ============================================================================
var token = builder.Configuration["Discord:Token"]
    ?? throw new Exception("Discord:Token is not configured.");

var discordClientBuilder = DiscordClientBuilder.CreateSharded(token, DiscordIntents.All);

// ---- Services (DI) ----
discordClientBuilder.ConfigureServices(services =>
{
    // Lavalink
    services.AddLavalink();
    services.ConfigureLavalink(config =>
    {
        var ll = builder.Configuration.GetSection("Lavalink");
        config.BaseAddress = new Uri($"http://{ll["Host"]}:{ll["Port"]}");
        config.Passphrase = ll["Password"] ?? "youshallnotpass";
    });

    services.Configure<PingOptions>(builder.Configuration.GetSection("Ping"));
    services.Configure<BotOptions>(builder.Configuration.GetSection("Discord"));
    services.Configure<LavalinkOptions>(builder.Configuration.GetSection("Lavalink"));

    // Custom services
    services.AddSingleton<MessageCreatedHandler>();
    services.AddSingleton<KeywordService>();
    services.AddSingleton<PingHandlerService>();
    
    services.AddHostedService<PingDecayService>();
});

// ---- Commands ----
discordClientBuilder.UseCommands((serviceProvider, commands) =>
{
    commands.AddCommands(typeof(Program).Assembly);
    commands.AddProcessor(new SlashCommandProcessor());
});

// ---- Event Handlers ----
discordClientBuilder.ConfigureEventHandlers(handlers =>
{
    handlers.AddEventHandlers<MessageCreatedHandler>(ServiceLifetime.Singleton);
});

// Build the client and register it with the host container
var discordClient = discordClientBuilder.Build();
builder.Services.AddSingleton(discordClient);

// ============================================================================
// Hosted Service (lifecycle management)
// ============================================================================
builder.Services.AddHostedService<BotService>();

// ============================================================================
// Logging
// ============================================================================
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(
        builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);
});

// ============================================================================
// Run
// ============================================================================
await builder.Build().RunAsync();