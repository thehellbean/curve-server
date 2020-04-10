using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using WebSocketManager;

namespace AttentecDieKurve
{
    public class GameMessageHandler : WebSocketHandler
    {
        public GameMessageHandler(WebSocketConnectionManager webSocketConnectionManager) : base(webSocketConnectionManager)
        {
            gameInstance = new GameInstance(_socketQueue, _gameQueue);
        }

        private ConcurrentQueue<ServerGameMessage> _socketQueue = new ConcurrentQueue<ServerGameMessage>();
        private ConcurrentQueue<ServerGameMessage> _gameQueue = new ConcurrentQueue<ServerGameMessage>();
        private ConcurrentDictionary<WebSocket, int> socketPlayerId = new ConcurrentDictionary<WebSocket, int>();
		private GameInstance gameInstance;

        public override async Task OnConnected(WebSocket socket)
        {
            await base.OnConnected(socket);

            var socketId = WebSocketConnectionManager.GetId(socket);
            var serverMessage = new ServerGameMessage
            {
                type = "connection",
                content = "",
                playerId = socketId,
                gameFrame = -1
            };

            var outMessage = JsonSerializer.Serialize<ServerGameMessage>(serverMessage);

            await SendMessageToAllAsync(outMessage);

            var configContent = JsonSerializer.Serialize<GameConfig>(gameInstance.gameConfig);
            serverMessage = new ServerGameMessage
            {
                type = "updateConfig",
                content = configContent,
                playerId = -1,
                gameFrame = -1,
            };

            outMessage = JsonSerializer.Serialize<ServerGameMessage>(serverMessage);
            await SendMessageAsync(socket, outMessage);

            serverMessage = new ServerGameMessage
            {
                type = "identity",
                content = $"{socketId}",
                playerId = socketId,
                gameFrame = -1
            };

            outMessage = JsonSerializer.Serialize<ServerGameMessage>(serverMessage);
            await SendMessageAsync(socket, outMessage);

			serverMessage = gameInstance.GetWholeStateMessage();
            outMessage = JsonSerializer.Serialize<ServerGameMessage>(serverMessage);
			await SendMessageAsync(socket, outMessage);
        }

        public override async Task OnDisconnected(WebSocket socket) {
            if (socketPlayerId.TryGetValue(socket, out int id)) {
                var suspect = gameInstance.gameState.players.Find(x => x.id == id);
                if (suspect != null) {
                    suspect.connected = false;

                    var disconnectedMessage = new ServerGameMessage
                    {
                        type = "playerDisconnected",
                        content = $"{suspect.id}",
                        playerId = suspect.id
                    };

                    System.Console.WriteLine($"Sending disconnected for {suspect.id}");
                    await SendMessageToAllAsync(JsonSerializer.Serialize<ServerGameMessage>(disconnectedMessage));
                }
            }

            socketPlayerId.TryRemove(socket, out int _);
        }

        public void queueGameMessage(ServerGameMessage message) {
            _gameQueue.Enqueue(message);
        }

        public override async Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, byte[] buffer)
        {
            var socketId = WebSocketConnectionManager.GetId(socket);
            var messageString = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var clientMessage = JsonSerializer.Deserialize<ClientGameMessage>(messageString, new JsonSerializerOptions());
            if (socketPlayerId.TryGetValue(socket, out int playerId)) {
                var serverMessage = new ServerGameMessage
                {
                    type = clientMessage.type,
                    content = clientMessage.content,
                    gameFrame = clientMessage.gameFrame,
                    playerId = playerId,
                };

                if (clientMessage.type == "startGame") {
                    var gameThread = new Thread(this.startGameInstance);
                    gameThread.Start();
                } else if (clientMessage.type == "updateConfig") {
                    await SendMessageToAllAsync(gameInstance.updateConfig(serverMessage));
                } else {
                    System.Console.WriteLine("Received message about " + serverMessage.type);
                    queueGameMessage(serverMessage);
                }
            } else if (clientMessage.type == "registerPlayer") {
                var serverMessage = new ServerGameMessage
                {
                    type = clientMessage.type,
                    content = clientMessage.content,
                    gameFrame = clientMessage.gameFrame,
                    playerId = -1,
                };

                socketPlayerId.TryAdd(socket, gameInstance.idIncrement);
                var response = gameInstance.registerPlayer(serverMessage);
                await SendMessageToAllAsync(response);
            }
        }

        public async void startGameInstance()
        {
            if (gameInstance.IsRunning()) {
                return;
            }

            foreach (var player in gameInstance.gameState.players) {
                if (!player.connected) {
                    var removedMessage = new ServerGameMessage
                    {
                        playerId = player.id,
                        content = "",
                        type = "playerRemoved"
                    };

                    await SendMessageToAllAsync(JsonSerializer.Serialize<ServerGameMessage>(removedMessage)); 
                }
            }

            gameInstance.Reset();
            var autoEvent = new AutoResetEvent(false);
            System.Console.WriteLine("Starting timer");

            var startGameMessage = new ServerGameMessage
            {
                type = "startGame",
                content = "",
                playerId = -1,
                gameFrame = 0,
            };

            var outMessage = JsonSerializer.Serialize<ServerGameMessage>(startGameMessage);
            await SendMessageToAllAsync(outMessage);

            gameInstance.run(autoEvent);
            autoEvent.WaitOne();
            handleSocketMessages();

            Thread.Sleep(2000);

            var gameTimer = new Timer(gameInstance.run, autoEvent, 0, 17);

            while (gameInstance.IsRunning() || !gameInstance.HasStarted()) {
                autoEvent.WaitOne();
                handleSocketMessages();
            }

            System.Console.WriteLine("Destroying timer");
            gameTimer.Dispose();

            gameInstance.Reset();
        }

        private async void handleSocketMessages() {
            while (_socketQueue.TryDequeue(out ServerGameMessage message)) {
                var outMessage = JsonSerializer.Serialize<ServerGameMessage>(message);

                await SendMessageToAllAsync(outMessage);
            } 
        }

    }
}
