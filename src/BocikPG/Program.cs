using BocikPG;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.VoiceNext;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================================
// Configuration
// ============================================================================
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var token = configuration["Discord:Token"]
    ?? throw new Exception("Discord:Token is not configured.");

// ============================================================================
// Discord Client Builder  (this IS the host)
// ============================================================================
var discordClientBuilder = DiscordClientBuilder.CreateSharded(token, DiscordIntents.All);

discordClientBuilder.ConfigureServices(services =>
{
    // ---- Configuration ----
    services.AddSingleton<IConfiguration>(configuration);
    services.Configure<BotOptions>(configuration.GetSection("Discord"));
    services.Configure<LavalinkOptions>(configuration.GetSection("Lavalink"));
    services.Configure<PingOptions>(configuration.GetSection("Ping"));
    services.Configure<RandomResponseOptions>(configuration.GetSection("RandomResponse"));
    services.Configure<VoiceOptions>(configuration.GetSection("Voice"));

    // ---- Lavalink (registered here, in the same DI container) ----
    services.AddLavalink();
    services.ConfigureLavalink(config =>
    {
        config.BaseAddress = new Uri(
            $"http://{configuration["Lavalink:Host"] ?? "localhost"}:{configuration["Lavalink:Port"] ?? "2333"}");
        config.Passphrase = configuration["Lavalink:Password"] ?? "youshallnotpass";
    });

    // ---- Bot services ----
    services.AddSingleton<MessageCreatedHandler>();
    services.AddSingleton<KeywordService>();
    services.AddSingleton<PingHandlerService>();
    services.AddSingleton<RandomResponseService>();
    services.AddSingleton<VoiceChannelService>();
    services.AddSingleton<UserWeightService>();
    services.AddSingleton<VoiceChannelService>();

    services.AddHostedService<PingDecayService>();

    // ---- Logging ----
    services.AddLogging(logging =>
    {
        logging.AddConsole();
        // Read ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT to detect dev mode
        var env = configuration["DOTNET_ENVIRONMENT"] ?? "Production";
        logging.SetMinimumLevel(
            env.Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? LogLevel.Debug
                : LogLevel.Information);
    });
});

// ---- Commands ----
discordClientBuilder.UseCommands((_, commands) =>
{
    commands.AddCommands(typeof(Program).Assembly);
    commands.AddProcessor(new SlashCommandProcessor());
});

// ---- Event Handlers ----
discordClientBuilder.ConfigureEventHandlers(handlers =>
{
    handlers.AddEventHandlers<MessageCreatedHandler>(ServiceLifetime.Singleton);
    handlers.AddEventHandlers<VoiceEventHandler>(ServiceLifetime.Singleton);
});

discordClientBuilder.UseVoiceNext(new VoiceNextConfiguration());

// ============================================================================
// Build & Run
// ============================================================================
var client = discordClientBuilder.Build();

await client.ServiceProvider.GetRequiredService<IAudioService>().StartAsync();

await client.ConnectAsync();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};


await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => Task.CompletedTask);

await client.DisconnectAsync();

