public class SoundDefinition
{
	public string Name { get; set; } = "";
	public string Emoji { get; set; } = "🔊";
	public string Filename { get; set; } = "";
	public int Volume { get; set; } = 100;
}

public class SoundboardData
{
	public List<SoundDefinition> Sounds { get; set; } = new();
}