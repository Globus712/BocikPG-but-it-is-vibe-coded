public class KeywordResponse
{
    public Dictionary<string, List<ResponseEntry>> Keywords { get; set; } = new();
}

public class ResponseEntry
{
    public string Text { get; set; } = "";
    public int Weight { get; set; } = 1;  // Default weight = 1
}