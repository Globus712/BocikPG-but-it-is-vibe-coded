using BocikPG;
using BocikPG.Soundboard;
using BocikPG.SoundStats;
using BocikPG.Sync;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.EventArgs;
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
// Discord Client Builder
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
    services.Configure<SoundboardOptions>(configuration.GetSection("Soundboard"));
    services.Configure<GitSyncOptions>(configuration.GetSection("Sync"));
    services.Configure<ChatOptions>(configuration.GetSection("Chat"));
    services.Configure<SoundStatsOptions>(configuration.GetSection("SoundStats"));
    services.Configure<DynamicCommandOptions>(configuration.GetSection("DynamicCommand"));

    // ---- Lavalink ----
    services.AddLavalink();
    services.ConfigureLavalink(config =>
    {
        config.BaseAddress = new Uri(
            $"http://{configuration["Lavalink:Host"] ?? "localhost"}:{configuration["Lavalink:Port"] ?? "2333"}");
        config.Passphrase = configuration["Lavalink:Password"] ?? "youshallnotpass";
        config.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(120));
    });

    // ---- Bot services ----
    services.AddSingleton<MessageCreatedHandler>();
    services.AddSingleton<KeywordService>();
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<KeywordService>());

    services.AddSingleton<PingHandlerService>();
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<PingHandlerService>());

    services.AddSingleton<RandomResponseService>();
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<RandomResponseService>());

    services.AddSingleton<UserWeightService>();
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<UserWeightService>());

    services.AddSingleton<VoiceChannelService>();
    services.AddSingleton<SoundboardService>();
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<SoundboardService>());

    services.AddSingleton<SoundboardInteractionHandler>();
    services.AddSingleton<SoundboardMessageStore>();
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<SoundboardMessageStore>());

    // ---- Soundboard: player + per-user sounds ----
    services.AddSingleton<SoundPlayerService>();           // shared play logic
    services.AddSingleton<UserSoundService>();             // per-user assignments
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<UserSoundService>());
    services.AddSingleton<VoiceJoinLeaveHandler>();        // join/leave event handler

    services.AddSingleton<GitSyncService>();
    services.AddSingleton<SyncReloadCoordinator>();
    services.AddSingleton<SyncConflictHandler>();
    services.AddSingleton<SoundboardUploadHandler>();
    services.AddSingleton<SoundboardBoardService>();

    services.AddSingleton<SoundStatsService>();
    services.AddSingleton<IReloadable>(sp => sp.GetRequiredService<SoundStatsService>());
    services.AddHostedService(sp => sp.GetRequiredService<SoundStatsService>());

    services.AddHostedService<PingDecayService>();
    services.AddSingleton<SourceNameAutoCompleteProvider>();
    services.AddSingleton<SoundStatsButtonHandler>();

    // ---- Logging ----
    services.AddLogging(logging =>
    {
        logging.AddConsole();
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
    commands.AddCommands(CommandBuilderFactory.BuildCommandsFromJsonFile(configuration["DynamicCommand:CommandsFilePath"] ?? ""));
    commands.AddCommands(typeof(Program).Assembly);
    commands.AddProcessor(new SlashCommandProcessor());
});

// ---- Event Handlers ----
discordClientBuilder.ConfigureEventHandlers(handlers =>
{
    handlers.AddEventHandlers<MessageCreatedHandler>(ServiceLifetime.Singleton);
    handlers.AddEventHandlers<VoiceEventHandler>(ServiceLifetime.Singleton);
    handlers.AddEventHandlers<SoundboardInteractionHandler>(ServiceLifetime.Singleton);
    handlers.AddEventHandlers<SyncConflictHandler>(ServiceLifetime.Singleton);
    handlers.AddEventHandlers<VoiceJoinLeaveHandler>(ServiceLifetime.Singleton);  // new
    handlers.AddEventHandlers<SoundStatsButtonHandler>(ServiceLifetime.Singleton);
});

discordClientBuilder.UseVoiceNext(new VoiceNextConfiguration());

// ============================================================================
// Build & Run
// ============================================================================
var client = discordClientBuilder.Build();

await client.ServiceProvider.GetRequiredService<IAudioService>().StartAsync();
await client.ConnectAsync();

await Task.Delay(5000);
var voiceService = client.ServiceProvider.GetRequiredService<VoiceChannelService>();
await voiceService.InitializeAllGuildsAsync();

var statsService = client.ServiceProvider.GetRequiredService<SoundStatsService>();
await statsService.StartAsync(CancellationToken.None);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => Task.CompletedTask);

await client.DisconnectAsync();
