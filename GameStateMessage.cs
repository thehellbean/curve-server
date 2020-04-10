public class ClientGameMessage
{
    public string type { get; set; }
    public string content { get; set; }
    public int gameFrame { get; set; }
}

public class ServerGameMessage
{
    public int playerId { get; set; }
    public string type { get; set; }
    public string content { get; set; }
    public int gameFrame { get; set; }
}
