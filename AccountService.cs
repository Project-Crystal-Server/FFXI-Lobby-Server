/*
===========================================================================
Copyright (C) 2019-2026 Project Crystal Dev Team

This file is part of Project Crystal Server.

Project Crystal Server is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Project Crystal Server is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with Project Crystal Server. If not, see <https://www.gnu.org/licenses/>.
===========================================================================
*/

using Crystal.FFXILobbyServer.Network.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Crystal.FFXILobbyServer
{
    // The account service: a world's map server asks which PlayOnline account a character it is serving belongs to.
    //
    //   GET /v1/session
    //   Authorization: Bearer <the world's accountsKey from lobby.cfg>
    //   X-Lobby-Session: <the lobby_token of the character's session row>
    //
    //   200 {"contentId": 2, "account": 1, "contentIds": [1, 2]}
    //   401 no world has that key
    //   403 no live session with that token on that world
    //
    // A token lives in the session row the lobby writes at selection and goes with it when the map server ends the
    // session, so a token answers for exactly one session. The account is the member's lowest content id (Crystal
    // has no numeric member id; content ids never move between members), and the POL ID never leaves the lobby.
    public class AccountService(IPEndPoint endPoint, List<WorldContainer> worldList)
    {
        private const int MAX_REQUEST_BYTES = 8192;
        private static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(5);

        private readonly IPEndPoint EndPoint = endPoint;
        private readonly List<WorldContainer> WorldList = worldList;
        private TcpListener Listener;

        public void Start(CancellationToken stoppingToken)
        {
            Listener = new TcpListener(EndPoint);
            Listener.Start();
            Program.Log.Info($"Account service listening on {EndPoint}");
            _ = AcceptLoop(stoppingToken);
        }

        public void Stop()
        {
            Listener?.Stop();
        }

        private async Task AcceptLoop(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await Listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException || e is SocketException)
                {
                    return;
                }
                _ = Handle(client, stoppingToken);
            }
        }

        private async Task Handle(TcpClient client, CancellationToken stoppingToken)
        {
            using (client)
            {
                try
                {
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(REQUEST_TIMEOUT);
                    NetworkStream stream = client.GetStream();

                    string request = await ReadHead(stream, timeout.Token);
                    (int status, string body) = request == null ? (400, Error("bad request")) : Answer(request, client);

                    byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                    string head = $"HTTP/1.1 {status} {Reason(status)}\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head), timeout.Token);
                    await stream.WriteAsync(bodyBytes, timeout.Token);
                }
                catch (Exception e) when (e is OperationCanceledException || e is System.IO.IOException || e is SocketException)
                {
                    // Gone or too slow: nothing to answer
                }
                catch (Exception e)
                {
                    Program.Log.Error($"Account service: {e}");
                }
            }
        }

        // The request line and headers, or null if they do not end within MAX_REQUEST_BYTES
        private static async Task<string> ReadHead(NetworkStream stream, CancellationToken token)
        {
            byte[] buffer = new byte[MAX_REQUEST_BYTES];
            int length = 0;
            while (length < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length), token);
                if (read == 0)
                    return null;
                length += read;
                string text = Encoding.ASCII.GetString(buffer, 0, length);
                int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end >= 0)
                    return text[..end];
            }
            return null;
        }

        private (int, string) Answer(string request, TcpClient client)
        {
            string[] lines = request.Split("\r\n");
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3)
                return (400, Error("bad request"));
            if (requestLine[1] != "/v1/session")
                return (404, Error("not found"));
            if (requestLine[0] != "GET")
                return (405, Error("method not allowed"));

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            string caller = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

            // Which world is asking
            WorldContainer world = null;
            if (headers.TryGetValue("Authorization", out string authorization) && authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                world = WorldFromKey(authorization["Bearer ".Length..].Trim());
            if (world == null)
            {
                Program.Log.Warn($"Account service: {caller} sent no valid world key");
                return (401, Error("unknown world key"));
            }

            // About which session
            if (!headers.TryGetValue("X-Lobby-Session", out string sessionToken) || !IsToken(sessionToken))
                return (403, Error("no session"));
            (uint contentId, uint charId)? session = world.Api.SessionByToken(sessionToken);
            if (session == null)
                return (403, Error("no session"));

            // Whose content id, and the member's others. The session's character must be the one the content id's
            // slot points at on this world.
            (string polId, uint subId)? owner = Database.GetContentIdOwner(session.Value.contentId);
            if (owner == null || owner.Value.subId != ((world.World.Num << 16) | session.Value.charId))
            {
                Program.Log.Warn($"Account service: {world.World.Name} session of character {session.Value.charId} is not on content id {session.Value.contentId}");
                return (403, Error("no session"));
            }
            List<uint> contentIds = Database.GetMemberContentIds(owner.Value.polId);
            if (contentIds.Count == 0)
                return (403, Error("no session"));

            string body = $"{{\"contentId\":{session.Value.contentId},\"account\":{contentIds.Min()},\"contentIds\":[{string.Join(",", contentIds)}]}}";
            return (200, body);
        }

        private WorldContainer WorldFromKey(string key)
        {
            byte[] given = Encoding.UTF8.GetBytes(key);
            WorldContainer found = null;
            foreach (WorldContainer world in WorldList)
            {
                if (string.IsNullOrEmpty(world.AccountsKey))
                    continue;
                if (CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(world.AccountsKey)))
                    found = world;
            }
            return found;
        }

        // 32 bytes as lowercase hex, as the lobby issues them
        private static bool IsToken(string token)
        {
            return token.Length == 64 && token.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));
        }

        public static string NewToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        }

        private static string Error(string message) => $"{{\"error\":\"{message}\"}}";

        private static string Reason(int status) => status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Error",
        };
    }
}
