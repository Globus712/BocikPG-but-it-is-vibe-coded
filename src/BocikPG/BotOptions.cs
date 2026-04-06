namespace BocikPG;

public class BotOptions
{
    public string Token { get; set; } = string.Empty;
}

public class LavalinkOptions
{
    public string Host     { get; set; } = "localhost";
    public int    Port     { get; set; } = 2333;
    public string Password { get; set; } = "youshallnotpass";
}
