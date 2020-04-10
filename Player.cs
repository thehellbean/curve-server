using System;

public class ClientPlayerInfo {
    public string name { get; set;}
    public string color { get; set; }
}

public class Player {
    public int id { get; set; }
    public int score { get; set; }
    public string color { get; set; }
    public string name { get; set; }
    public bool connected { get; set; }
    public bool alive { get; set; }
    public double x;
    public double y;
    public double movementAngle;
    public int latestGameFrame;
    public int noTrailFrames;

    public bool leftKeyPressed;
    public bool rightKeyPressed;
    private GameConfig gameConfig;

    public Player(int id, string color, GameConfig gameConfig) {
        this.id = id;
        this.name = "Unnamed";
        this.color = color;
        this.gameConfig = gameConfig;
        this.connected = true;
        this.score = 0;

        this.Reset();
        this.alive = false;
    }

    public Player(ClientPlayerInfo playerInfo, int id, GameConfig gameConfig) {
        this.id = id;
        this.name = playerInfo.name;
        this.color = playerInfo.color;
        this.gameConfig = gameConfig;
        this.connected = true;
        this.score = 0;

        this.Reset();
        this.alive = false;
    }

    public void processTurn(int multiplier) {
        if (this.leftKeyPressed) {
            this.movementAngle -= this.gameConfig.turnSpeed * multiplier;
        } else if (this.rightKeyPressed) {
            this.movementAngle += this.gameConfig.turnSpeed * multiplier;
        }
    }

    public void processFrame() {
        processTurn(1);
        processMove(1);
    }

    public void interpolateTurn(int multiplier, int direction) {
        this.movementAngle += this.gameConfig.turnSpeed * multiplier * direction;
    }

    public void ProcessFrameBackwards() {
        processTurn(-1);
        processMove(-1);
    }

    public void processMove(int multiplier) {
        double dx = System.Math.Cos(this.movementAngle);
        double dy = System.Math.Sin(this.movementAngle);

        this.x = this.x + dx * multiplier;
        this.y = this.y + dy * multiplier;
    }

    public void Reset() {
        this.alive = true;
        this.noTrailFrames = 0;

        Random rnd = new Random();
        this.x = rnd.Next(200, 600);
        this.y = rnd.Next(200, 600);

        this.movementAngle = 0;
        this.leftKeyPressed = this.rightKeyPressed = false;
    }
}

public class SimplePlayerState {
    public bool alive { get; set; }

    public SimplePlayerState(Player player) {
        alive = player.alive;
    }
}
