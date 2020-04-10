using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

public class GameConfig {
    public int cellSize { get; set;}
    public int boardWidth { get; set; }
    public int boardHeight { get; set; }

    public double turnSpeed;
    public double speed;

    public GameConfig() {
        cellSize = 2;
        turnSpeed = 0.05;
        boardWidth = 1024;
        boardHeight = 768;
        speed = 2;
    }
}

public class Cell {
    public int x { get; set; }
    public int y { get; set; }
    public string color { get; set; }
    public int cellSize { get; set; }

    public bool collision(int other_x, int other_y) {
        return (other_x < this.x + this.cellSize &&
        other_x + this.cellSize > this.x &&
        other_y < this.y + this.cellSize &&
        other_y + this.cellSize > this.y);
    }

    public Cell(int x, int y, string color, int cellSize) {
        this.x = x;
        this.y = y;
        this.color = color;
        this.cellSize = cellSize;
    }
}

public class GameState {
    [JsonIgnore]
    public Cell[,] cells { get; set; }
    public List<Player> players { get; set; }
    public int gameFrame { get; set; }

    private GameConfig gameConfig;

    public GameState(List<Player> players, GameConfig config) {
        this.gameConfig = config;

        this.cells = new Cell[config.boardWidth, config.boardHeight];
        this.players = players;
        this.gameFrame = 0;
    }

    public void Reset() {
        this.cells = new Cell[gameConfig.boardWidth, gameConfig.boardHeight];
        this.gameFrame = 0;
        this.players.RemoveAll(x => !x.connected);

        foreach (var player in players) {
            player.Reset();
        }
    }
}

public class PartialState {
    public List<Cell> cells { get; set; }
    public int gameFrame { get; set; }
    public List<SimplePlayerState> players { get; set; }

    public PartialState(List<Player> players) {
        this.cells = new List<Cell>();
        this.gameFrame = 0;
        this.players = new List<SimplePlayerState>();

        foreach (var player in players) {
            this.players.Add(new SimplePlayerState(player));
        }
    }
}

public class GameInstance
{
    private ConcurrentQueue<ServerGameMessage> _socketQueue;
    private ConcurrentQueue<ServerGameMessage> _gameQueue;

    public GameState gameState;
    public GameConfig gameConfig;
    private bool isRunning;
    private bool hasStarted;
    private PartialState partialState;
    public int idIncrement = 0;

    public GameInstance(ConcurrentQueue<ServerGameMessage> socketQueue, ConcurrentQueue<ServerGameMessage> gameQueue)
    {
        _socketQueue = socketQueue;
        _gameQueue = gameQueue;
        this.gameConfig = new GameConfig();

        List<Player> players = new List<Player>();
        this.gameState = new GameState(players, this.gameConfig);

        this.isRunning = false;
        this.hasStarted = false;
        this.partialState = new PartialState(gameState.players);
    }

    public void Reset() {
        this.isRunning = false;
        this.hasStarted = false;

        this.gameState.Reset();
        this.partialState.cells.Clear();
    }

    public bool HasStarted() {
        return hasStarted;
    }

    public string registerPlayer(ServerGameMessage playerMessage) {
        var playerInfo = JsonSerializer.Deserialize<ClientPlayerInfo>(playerMessage.content);
        var newPlayer = new Player(playerInfo, this.idIncrement++, this.gameConfig);
        this.gameState.players.Add(newPlayer);

        var serverMessage = new ServerGameMessage
        {
            type = "registerPlayer",
            content = JsonSerializer.Serialize<Player>(newPlayer),
            playerId = newPlayer.id,
            gameFrame = this.gameState.gameFrame
        };

        return JsonSerializer.Serialize<ServerGameMessage>(serverMessage);
    }

    public string updateConfig(ServerGameMessage playerMessage) {
        if (this.hasStarted) {
            return "{ \"type\": \"rejected\", \"content\": \"no}\"}";
        }

        var configInfo = JsonSerializer.Deserialize<GameConfig>(playerMessage.content);

        this.gameConfig.boardWidth = Math.Min(configInfo.boardWidth, 1500);
        this.gameConfig.boardHeight = Math.Min(configInfo.boardHeight, 900);
        this.gameConfig.cellSize = Math.Min(configInfo.cellSize, 10);

        var responseMessage = new ServerGameMessage
        {
            type = "updateConfig",
            content = JsonSerializer.Serialize<GameConfig>(this.gameConfig)
        };

        return JsonSerializer.Serialize<ServerGameMessage>(responseMessage);
    }

    public void handleEvents(AutoResetEvent autoEvent) {
        while (_gameQueue.TryDequeue(out ServerGameMessage message)) {
            bool shouldInterpolate = false;
            int frameDiff = 0;

            var player = gameState.players.Find(x => x.id == message.playerId);

            if (player == null) {
                continue;
            }

            if (message.gameFrame > player.latestGameFrame && message.gameFrame <= gameState.gameFrame) {

                frameDiff = gameState.gameFrame - message.gameFrame;
                shouldInterpolate = true;

                for (int i = 0; i < frameDiff; i++) {
                    player.ProcessFrameBackwards();
                }

                player.latestGameFrame = message.gameFrame;
            }

            if (message.type == "keydown") {
                if (message.content == "left") {
                    player.leftKeyPressed = true;
                    player.rightKeyPressed = false;
                } else if (message.content == "right") {
                    player.rightKeyPressed = true;
                    player.leftKeyPressed = false;
                }
            } else if (message.type == "keyup") {
                if (message.content == "left") {
                    player.leftKeyPressed = false;
                } else if (message.content == "right") {
                    player.rightKeyPressed = false;
                }
            }


            if (shouldInterpolate) {
                for (int i = 0; i < frameDiff; i++) {
                   player.processFrame();
                }
            }
        }
    }

    public void run(Object stateInfo)
    {
        AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
        isRunning = true;
        hasStarted = true;

        handleEvents(autoEvent);

        int alivePlayers = 0;
        foreach (var player in gameState.players) {
            if (player.alive) {
                alivePlayers++;
            }
        }

        for (int i = 0; i < this.gameConfig.speed; i++) {
            alivePlayers = processFrame(alivePlayers);
        }

        if (alivePlayers <= 1) {
            var serverMessage = new ServerGameMessage
            {
                playerId = -1,
                type = "gameOver",
                content = "",
            };

            isRunning = false;

            _socketQueue.Enqueue(serverMessage);

            autoEvent.Set();
        } 

        _socketQueue.Enqueue(GetPartialStateMessage());
        autoEvent.Set();

        this.gameState.gameFrame++;
    }

    public bool IsRunning() {
        return isRunning;
    }

    public int processFrame(int alivePlayers) 
    {
        int deathCount = 0;
        foreach (var player in gameState.players) {
            if (!player.alive) {
                continue;
            }

            player.processFrame();

            Random rnd = new Random();
            if (player.noTrailFrames > 0) {
                continue;
            } else if (rnd.Next(0, 100) <= 1) {
                player.noTrailFrames = rnd.Next(1, 10);
            }

            bool diedThisFrame = false;

            if (player.x < 0 || player.y < 0 || player.x >= gameConfig.boardWidth || player.y >= gameConfig.boardHeight) {
                System.Console.WriteLine("Out of bounds");
                System.Console.WriteLine($"{player.x}, {player.y}");
                diedThisFrame = true;
            }

            var playerYDirection = Math.Sign(Math.Sin(player.movementAngle));
            var playerXDirection = Math.Sign(Math.Cos(player.movementAngle));
            for (int deltaX = -1; deltaX < 2; deltaX++) {
                for (int deltaY = -1; deltaY < 2; deltaY++) {
                    if (player.x + deltaX >= gameConfig.boardWidth || player.x + deltaX < 0) {
                        continue;
                    } else if (player.y + deltaY >= gameConfig.boardHeight || player.y + deltaY < 0) {
                        continue;
                    }

                    if (gameState.cells[(int)player.x + deltaX, (int)player.y + deltaY] == null) {
                        continue;
                    }

                    if ((deltaX == playerXDirection) && (deltaY == playerYDirection)) {
                        System.Console.WriteLine($"{deltaX}, {deltaY}, {playerXDirection}");
                        if (gameState.cells[(int)player.x + deltaX, (int)player.y + deltaY].collision((int)player.x, (int)player.y)) {
                            System.Console.WriteLine("Collision");
                            System.Console.WriteLine($"{player.x}, {player.y}");
                            System.Console.WriteLine($"{player.x + deltaX}, {player.y + deltaY}");
                            diedThisFrame = true;
                            break;
                        }
                    }
                }
            }

            if (diedThisFrame) {
                player.alive = false;
                deathCount++;

                var serverMessage = new ServerGameMessage
                {
                    playerId = player.id,
                    type = "playerDied",
                    content = "",
                };

                foreach (var alivePlayer in gameState.players) {
                    if (alivePlayer.alive) {
                        alivePlayer.score++;
                    }
                }

                _socketQueue.Enqueue(serverMessage);
            }
        }

        foreach (var player in gameState.players) {
            if (!player.alive) { 
                continue;
            } else if (player.noTrailFrames > 0) {
                player.noTrailFrames--;
                continue;
            }

            if (player.x < gameConfig.boardWidth && player.y < gameConfig.boardHeight && player.x >= 0 && player.y >= 0) {
                gameState.cells[(int)player.x, (int)player.y] = new Cell((int)player.x, (int)player.y, player.color, gameConfig.cellSize);
                partialState.cells.Add(new Cell((int)player.x, (int)player.y, player.color, gameConfig.cellSize));
            }
        }

        return alivePlayers - deathCount;
    }

    public ServerGameMessage GetWholeStateMessage() {
        string content = JsonSerializer.Serialize<GameState>(gameState);
        ServerGameMessage message = new ServerGameMessage
        {
            content = content,
            type = "gameState",
            playerId = -1,
        };

        return message;
    }

    public ServerGameMessage GetPartialStateMessage() {
        partialState.gameFrame = gameState.gameFrame;
        for (int i = 0; i < gameState.players.Count; i++) {
            if (i < partialState.players.Count) {
                partialState.players[i].alive = gameState.players[i].alive;
            } else {
                partialState.players.Add(new SimplePlayerState(gameState.players[i]));
            }
        }

        string content = JsonSerializer.Serialize<PartialState>(partialState);
        ServerGameMessage message = new ServerGameMessage
        {
            content = content,
            type = "partialState",
            playerId = -1,
        };

        partialState.cells.Clear();

        return message;
    }
}
