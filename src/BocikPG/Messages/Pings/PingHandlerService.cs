using System.Collections.Concurrent;
using System.Text.Json;
using BocikPG;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class PingHandlerService
{
	private readonly string _filePath = "PersonalizedResponses.json";
	private readonly PingOptions _options;
	private readonly ILogger<PingHandlerService>? _logger;  // optional
	private Dictionary<ulong, string> _personalizedResponses;
	private ConcurrentDictionary<ulong, (int count, DateTime timeoutUntil)> _userPingState = new();
	public bool DecayEnabled { get; set; } = true;
	public int DecayIntervalMinutes { get; set; } = 60;

	// Inject ILogger optionally
	public PingHandlerService(IOptions<PingOptions> options, ILogger<PingHandlerService>? logger = null)
	{
		_options = options.Value;
		_logger = logger;
		LoadPersonalizedResponses();
	}
	private void LoadPersonalizedResponses()
	{
		if (!File.Exists(_filePath))
		{
			_personalizedResponses = new();
			Save();
			return;
		}
		var json = File.ReadAllText(_filePath);
		var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
		_personalizedResponses = dict?.ToDictionary(kv => ulong.Parse(kv.Key), kv => kv.Value) ?? new();
	}

	private void Save()
	{
		var dict = _personalizedResponses.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
		var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(_filePath, json);
	}

	public string GetResponseForUser(ulong userId)
	{
		if (_personalizedResponses.TryGetValue(userId, out var custom))
			return custom;
		return _options.DefaultResponse;
	}

	public async Task<string?> CanPingAsync(ulong userId, DiscordMember? member)
	{
		string? response = null;
		// Check if currently timed out by the bot's internal cooldown
		if (_userPingState.TryGetValue(userId, out var state) && DateTime.UtcNow < state.timeoutUntil)
		{
			response = null;
			return response;
		}

		// Update ping count
		var newState = _userPingState.AddOrUpdate(userId,
			(1, DateTime.MinValue),
			(_, old) => (old.count + 1, old.timeoutUntil));

		// Warning threshold reached
		if (newState.count == _options.MaxPings - 1)
		{
			response = string.Format(_options.WarningMessage, 1, _options.TimeoutSeconds);
			return response;
		}

		// Max pings reached → apply internal timeout + optional Discord server timeout
		if (newState.count >= _options.MaxPings)
		{
			var timeoutUntil = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds);
			_userPingState[userId] = (0, timeoutUntil);
			response = string.Format(_options.MaxPingsMessage, _options.MaxPings, _options.TimeoutSeconds);

			// --- Discord server timeout (if enabled and member is valid) ---
			if (_options.ServerTimeoutMinutes > 0 && member != null)
			{
				try
				{
					var until = DateTimeOffset.UtcNow.AddMinutes(_options.ServerTimeoutMinutes);
					await member.TimeoutAsync(until, "Exceeded max ping limit.");
					_logger?.LogInformation("Timed out user {UserId} for {Minutes} minutes.", userId, _options.ServerTimeoutMinutes);
				}
				catch (Exception ex)
				{
					_logger?.LogError(ex, "Failed to timeout user {UserId}. Missing permissions?", userId);
				}
			}

			return response;
		}

		// Normal response
		response = GetResponseForUser(userId);
		return response;
	}

	public void SetPersonalizedResponse(ulong userId, string response)
	{
		_personalizedResponses[userId] = response;
		Save();
	}

	public bool RemovePersonalizedResponse(ulong userId)
	{
		var removed = _personalizedResponses.Remove(userId);
		if (removed) Save();
		return removed;
	}

	public Dictionary<ulong, string> GetAllPersonalizedResponses()
	{
		return new Dictionary<ulong, string>(_personalizedResponses);
	}

	public void DecayPingCounts()
	{
		// Get a snapshot of all user IDs currently in the dictionary
		var users = _userPingState.Keys.ToList();

		foreach (var userId in users)
		{
			// Retrieve current state
			if (!_userPingState.TryGetValue(userId, out var state))
				continue;

			// Do not decay if the user is currently timed out
			if (state.timeoutUntil > DateTime.UtcNow)
				continue;

			int newCount = Math.Max(0, state.count - 1);

			if (newCount == 0)
			{
				// Remove entry entirely if count reaches zero and no timeout
				_userPingState.TryRemove(userId, out _);
			}
			else
			{
				// Update with the reduced count
				_userPingState.AddOrUpdate(userId,
					(newCount, DateTime.MinValue),
					(_, _) => (newCount, DateTime.MinValue));
			}
		}
	}
}