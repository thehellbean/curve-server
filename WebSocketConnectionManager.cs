using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;

namespace WebSocketManager 
{
    public class WebSocketConnectionManager
    {
        private ConcurrentDictionary<int, WebSocket> _sockets = new ConcurrentDictionary<int, WebSocket>();

        public WebSocket GetSocketById(int id)
        {
            return _sockets.FirstOrDefault(p => p.Key == id).Value;
        }

        public ConcurrentDictionary<int, WebSocket> GetAll()
        {
            return _sockets;
        }

        public int GetId(WebSocket socket)
        {
            return _sockets.FirstOrDefault(p => p.Value == socket).Key;
        }

        public void AddSocket(WebSocket socket)
        {
            _sockets.TryAdd(_sockets.Count, socket);
        }

        public async Task RemoveSocket(int id) 
        {
            WebSocket socket;
            _sockets.TryRemove(id, out socket);

            if (socket != null) {
                await socket.CloseAsync(closeStatus: WebSocketCloseStatus.NormalClosure, 
                                        statusDescription: "Closed by the WebSocketManager", 
                                        cancellationToken: CancellationToken.None);
            }
        }
    }
}
