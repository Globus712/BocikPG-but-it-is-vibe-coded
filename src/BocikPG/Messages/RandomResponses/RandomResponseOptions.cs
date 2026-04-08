namespace BocikPG;

public class WeightedResponse
{
    public string Text { get; set; } = "";
    public int Weight { get; set; } = 1;
}

public class UserRandomConfig
{
    public double Chance { get; set; } = 0.1;        // 0.0 to 1.0
    public List<WeightedResponse> Responses { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public class RandomResponseOptions
{
    public double DefaultChance { get; set; } = 0.05;        // 5% chance
    public List<WeightedResponse> DefaultResponses { get; set; } = new();
    public bool GlobalEnabled { get; set; } = true;
    public string StorageFile { get; set; } = "RandomResponses.json";
}