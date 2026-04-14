using BocikPG.Sync;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace BocikPG.Soundboard;

public sealed class SoundboardUploadHandler
{
	private static readonly HashSet<string> AudioMimeTypes =
	[
		"audio/mpeg", "audio/mp3", "audio/ogg", "audio/wav", "audio/wave",
	"audio/x-wav", "audio/flac", "audio/aac", "audio/mp4", "audio/x-m4a",
	"audio/webm", "video/ogg", "video/webm"
	];

	private static readonly HashSet<string> AudioExtensions =
[
".mp3", ".ogg", ".wav", ".flac", ".aac", ".m4a", ".webm", ".opus"
];

	private readonly SoundboardService _soundboardService;
	private readonly SoundboardBoardService _boardService;
	private readonly GitSyncService _syncService;
	private readonly SoundboardOptions _options;
	private readonly ILogger<SoundboardUploadHandler> _logger;

	public SoundboardUploadHandler(
		SoundboardService soundboardService,
		SoundboardBoardService boardService,
		GitSyncService syncService,
		IOptions<SoundboardOptions> options,
		ILogger<SoundboardUploadHandler> logger)
	{
		_soundboardService = soundboardService;
		_boardService = boardService;
		_syncService = syncService;
		_options = options.Value;
		_logger = logger;
	}

	public async Task<bool> TryHandleAsync(DiscordClient sender, MessageCreatedEventArgs args)
	{
		if (args.Author.IsCurrent) return false;
		if (args.Message.Attachments.Count != 1) return false;

		_logger.LogInformation("attachment found");

		var attachment = args.Message.Attachments[0];

		if (!await IsAudioAsync(attachment.Url, attachment.FileName)) return false;

		_logger.LogInformation("attachment is in correct format");

		var content = args.Message.Content?.Trim() ?? "";
		if (!LooksLikeUploadIntent(content)) return false;

		_logger.LogInformation("upload intent");

		// From here — looked like an intent, return true regardless of outcome
		var parsed = ParseMessage(content);
		if (parsed is null)
		{
			await args.Message.RespondAsync(
				"⚠️ Looks like you're trying to add a sound. Use: `<emote> <name> [volume]`\n" +
				"Example: `🔥 Explosion 80`");
			return true;
		}

		var (rawEmoji, soundName, volume) = parsed.Value;
		_logger.LogInformation("emoji parsed");

		if (_soundboardService.GetSound(soundName) is not null)
		{
			await args.Message.RespondAsync($"⚠️ A sound named **{soundName}** already exists.");
			return true;
		}

		var fileName = Path.GetFileName(attachment.FileName);
		var savePath = Path.Combine(AppContext.BaseDirectory, _options.SoundFilesPath, fileName);

		if (File.Exists(savePath))
		{
			await args.Message.RespondAsync(
				$"⚠️ A file named `{fileName}` already exists on disk. Rename your file and try again.");
			return true;
		}
		_logger.LogInformation("file get");

		try
		{
			_ = Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
			using var http = new HttpClient();
			using var stream = await http.GetStreamAsync(attachment.Url);
			using var fileStream = File.Create(savePath);
			await stream.CopyToAsync(fileStream);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to download attachment from {Url}", attachment.Url);
			await args.Message.RespondAsync("❌ Failed to download the file. Please try again.");
			return true;
		}

		var definition = new SoundDefinition
		{
			Name = soundName,
			Emoji = ResolveEmojiString(rawEmoji),
			Volume = volume ?? 100,
			Filename = fileName
		};

		_soundboardService.AddSound(definition);
		await _syncService.SyncFileAsync(Path.GetFullPath(savePath), $"Add sound: {soundName}");

		await args.Message.RespondAsync(
			$"✅ **{soundName}** added! The soundboard will update shortly...");

		_ = Task.Run(() => _boardService.UpdateAllAsync());
		return true;
	}

	// ── Parsing ───────────────────────────────────────────────────────────────

	private static readonly Regex ParseRegex = new(
		@"^(?<emoji>\S+)\s+(?<name>[^\d]\S*(?:\s+[^\d]\S*)*?)\s*(?<volume>\d+)?$",
		RegexOptions.Compiled);

	private static (string emoji, string name, int? volume)? ParseMessage(string content)
	{
		// Split on whitespace, last token is volume if numeric
		var tokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length < 2) return null;

		var emoji = tokens[0];

		int? volume = null;
		int nameEnd = tokens.Length;

		if (int.TryParse(tokens[^1], out var vol))
		{
			volume = Math.Clamp(vol, 0, 200);
			nameEnd = tokens.Length - 1;
		}

		if (nameEnd < 2) return null; // no name left

		var name = string.Join(' ', tokens[1..nameEnd]);
		return (emoji, name, volume);
	}

	// ── Emoji resolution ──────────────────────────────────────────────────────

	/// <summary>
	/// Converts whatever the user typed into a storage string:
	/// - Discord animated/static emote &lt;a:name:id&gt; or &lt;:name:id&gt; → raw snowflake id string
	/// - Unicode emoji                                                       → the emoji character(s) as-is
	/// </summary>
	private static string ResolveEmojiString(string raw)
	{
		// Discord custom emote: <a:name:123456> or <:name:123456>
		if (raw.StartsWith('<') && raw.EndsWith('>'))
		{
			var inner = raw.Trim('<', '>');
			// strip optional 'a' for animated
			if (inner.StartsWith("a:")) inner = inner[2..];
			else if (inner.StartsWith(':')) inner = inner[1..];

			var parts = inner.Split(':');
			if (parts.Length == 2 && ulong.TryParse(parts[1], out _))
				return parts[1]; // store as snowflake ID string

			return raw; // malformed — keep raw
		}

		// Plain snowflake pasted directly
		if (ulong.TryParse(raw, out _))
			return raw;

		// Unicode emoji — keep as-is (strip surrounding : if someone typed :fire:)
		return raw.Trim(':');
	}

	// ── MIME probing ──────────────────────────────────────────────────────────

	private static async Task<string?> ProbeContentTypeAsync(string url)
	{
		try
		{
			using var http = new HttpClient();
			using var request = new HttpRequestMessage(HttpMethod.Head, url);
			using var resp = await http.SendAsync(request);
			return resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
		}
		catch
		{
			return null;
		}
	}



	private static async Task<bool> IsAudioAsync(string url, string fileName)
	{
		var mimeType = await ProbeContentTypeAsync(url);

		if (mimeType is not null && mimeType != "application/octet-stream")
			return AudioMimeTypes.Contains(mimeType);

		// MIME unavailable or generic — fall back to extension
		var ext = Path.GetExtension(fileName).ToLowerInvariant();
		return AudioExtensions.Contains(ext);
	}

	private static readonly Regex DiscordEmoteRegex = new(
	@"^<a?:[a-zA-Z0-9_]+:\d+>$",
	RegexOptions.Compiled);

	private static bool LooksLikeUploadIntent(string content)
	{
		if (string.IsNullOrWhiteSpace(content)) return false;

		var tokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		// Need at least: <emote> <something>
		if (tokens.Length < 2) return false;

		// Way too many tokens — not an upload command (5+ extra beyond emote+name+volume)
		if (tokens.Length > 7) return false;

		var first = tokens[0];

		// Discord custom emote: <:name:id> or <a:name:id>
		if (DiscordEmoteRegex.IsMatch(first)) return true;

		// Plain snowflake
		if (ulong.TryParse(first, out _)) return true;

		// Unicode emoji — check if it starts with a character outside the basic ASCII range
		// (emoji are all non-ASCII, so this filters out plain words)
		var firstChar = first[0];
		return firstChar > 127;
	}
}