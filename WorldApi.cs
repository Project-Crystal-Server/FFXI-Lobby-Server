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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace Crystal.FFXILobbyServer
{
    // How the lobby reaches a world: the world server's /api/lobby, never its database. Every call carries the world's
    // apiKey (the LOBBY_API_KEY of its settings/network.lua) as a bearer token. A world that cannot be reached or
    // answers wrongly gives the caller its "unknown" (null, false, ERR_NAME_SERVER), so a lobby never takes a world's
    // silence for an answer.
    public class WorldApi
    {
        // Lobby error codes the client shows for a name (LandSandBoat login_errors.h)
        public const uint ERR_NAME_UNAVAILABLE = 313; // "The character name you entered is unavailable."
        public const uint ERR_NAME_SERVER      = 314; // "Failed to register with the name server."

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private readonly WorldContainer World;

        public WorldApi(WorldContainer world)
        {
            World = world;
        }

        // The status and JSON body of a call; (0, null) when the world could not be reached or its answer is not JSON
        private (int Status, JsonObject Body) Send(HttpMethod method, string path, JsonObject body = null, string session = null)
        {
            debugLogSend(method.ToString(), path, body?.ToString() ?? "");

            try
            {
                using HttpRequestMessage request = new(method, World.ApiUrl.TrimEnd('/') + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", World.ApiKey);
                if (session != null)
                    request.Headers.Add("X-Lobby-Session", session);
                if (body != null)
                    request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = Http.Send(request);
                string text = new System.IO.StreamReader(response.Content.ReadAsStream()).ReadToEnd();
                debugLogReceive(response.StatusCode.ToString(), path, text?.ToString() ?? "");
                JsonObject answer = null;
                try { answer = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
                if (answer == null)
                {
                    Program.Log.Error($"World {World.World.Name}: {path} answered {(int)response.StatusCode} without JSON");
                    return (0, null);
                }
                return ((int)response.StatusCode, answer);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
            {
                Program.Log.Error($"World {World.World.Name}: {path} failed: {e.Message}");
                return (0, null);
            }
        }

        private static uint ErrorOf(JsonObject body, uint otherwise) =>
            body?["error"] is JsonNode e && e.GetValue<uint>() != 0 ? e.GetValue<uint>() : otherwise;

        private void LogRefusal(string what, int status, JsonObject body) =>
            Program.Log.Warn($"World {World.World.Name} refused to {what}: {status} {body?["message"]}");

        // One answer per request, in order: the character, or null when it is not on that content id of the world.
        // The whole result is null when the world could not answer.
        public List<WorldCharacter> ListCharacters(IReadOnlyList<(uint ContentId, uint CharId)> requested)
        {
            JsonArray characters = [];
            foreach (var (contentId, charId) in requested)
                characters.Add(new JsonObject { ["contentId"] = contentId, ["charId"] = charId });

            var (status, body) = Send(HttpMethod.Post, "/api/lobby/characters/list", new JsonObject { ["characters"] = characters });
            if (status != 200 || body["characters"] is not JsonArray answers || answers.Count != requested.Count)
            {
                if (body != null)
                    LogRefusal("list characters", status, body);
                return null;
            }
            return [.. answers.Select(a => a is JsonObject o && o["found"]?.GetValue<bool>() == true ? WorldCharacter.From(o) : null)];
        }

        // 0 if the name can be given to a character of the world, else the error to show
        public uint NameError(string name)
        {
            var (status, body) = Send(HttpMethod.Post, "/api/lobby/characters/check-name", new JsonObject { ["name"] = name });
            if (status != 200)
                return ErrorOf(body, ERR_NAME_SERVER);
            return body["error"]?.GetValue<uint>() ?? ERR_NAME_SERVER;
        }

        // The new character's id on the world, or 0 with the error to show
        public uint CreateCharacter(uint contentId, string name, CharaInfo info, uint startZone, out uint error)
        {
            error = 0;
            var (status, body) = Send(HttpMethod.Post, "/api/lobby/characters/create", new JsonObject
            {
                ["contentId"] = contentId,
                ["name"] = name,
                ["race"] = info.RaceNum,
                // The face is the low byte of FaceModelId (0-15, 8B = 15): the same byte LandSandBoat's own login server
                // takes from the create packet (offset 60). FaceNum is only the face number (8A and 8B are both 7).
                ["face"] = info.FaceModelId & 0xFF,
                ["size"] = info.Size,
                ["job"] = info.MJobNum,
                ["nation"] = info.TownNum,
                ["startZone"] = startZone,
            });
            if (status == 200 && body["charId"] is JsonNode charId)
                return charId.GetValue<uint>();

            error = ErrorOf(body, ERR_NAME_SERVER);
            if (body != null)
                LogRefusal($"create {name}", status, body);
            return 0;
        }

        // 0, or the error to show
        public uint RenameCharacter(uint contentId, uint charId, string name)
        {
            var (status, body) = Send(HttpMethod.Post, "/api/lobby/characters/rename", new JsonObject { ["contentId"] = contentId, ["charId"] = charId, ["name"] = name });
            if (status == 200)
                return 0;
            if (body != null)
                LogRefusal($"rename character {charId}", status, body);
            return ErrorOf(body, ERR_NAME_SERVER);
        }

        public bool DeleteCharacter(uint contentId, uint charId)
        {
            var (status, body) = Send(HttpMethod.Post, "/api/lobby/characters/delete", new JsonObject { ["contentId"] = contentId, ["charId"] = charId });
            if (status != 200 && body != null)
                LogRefusal($"delete character {charId}", status, body);
            return status == 200;
        }

        // Whether any of the characters is in the world. Unknown counts as online: keep the character out.
        public bool AnyOnline(IEnumerable<uint> charIds)
        {
            JsonArray ids = [.. charIds.Select(id => (JsonNode)id)];
            var (status, body) = Send(HttpMethod.Post, "/api/lobby/characters/online", new JsonObject { ["charIds"] = ids });
            return status != 200 || body["online"]?.GetValue<bool>() != false;
        }

        // Puts the character's session in the world, for the map server the client is sent to. False when the world
        // refused (the character is still in the world, is not on that content id) or could not be asked.
        public bool Enter(uint contentId, uint charId, byte[] key, uint serverAddress, uint serverPort, uint clientAddress, string clientVersion, uint clientExpansions, string lobbyToken)
        {
            var (status, body) = Send(HttpMethod.Post, "/api/lobby/sessions/enter", new JsonObject
            {
                ["contentId"] = contentId,
                ["charId"] = charId,
                ["key"] = Convert.ToHexString(key).ToLowerInvariant(),
                ["serverAddr"] = serverAddress,
                ["serverPort"] = serverPort,
                ["clientAddr"] = clientAddress,
                ["clientVersion"] = clientVersion,
                ["clientExpansions"] = clientExpansions,
                ["lobbyToken"] = lobbyToken,
            });
            if (status != 200 && body != null)
                LogRefusal($"admit character {charId}", status, body);
            return status == 200;
        }

        // The content id and character of the world's live session with this token, if there is one
        public (uint ContentId, uint CharId)? SessionByToken(string token)
        {
            var (status, body) = Send(HttpMethod.Get, "/api/lobby/session", session: token);
            if (status == 200 && body["contentId"] is JsonNode contentId && body["charId"] is JsonNode charId)
                return (contentId.GetValue<uint>(), charId.GetValue<uint>());
            return null;
        }

        private void debugLogSend(string method, string path, string body)
        {
            string log =
$@"
╔════════════════════════════════════════════════╗
║ SENDING
║ Path: {path}
║ Method: {method}

{body}

╚════════════════════════════════════════════════╝

";
            Program.Log.Debug(log);
        }
        private void debugLogReceive(string responseCode, string path, string body)
        {
            string log =
$@"
╔════════════════════════════════════════════════╗
║ RECEIVNG
║ Path: {path}
║ Status: {responseCode}

{body}

╚════════════════════════════════════════════════╝

";
            Program.Log.Debug(log);
        }
    }

    // A character as the world lists it
    public class WorldCharacter
    {
        public uint ContentId, CharId;
        public string Name;
        public bool Rename;
        public ushort Zone;
        public byte MainJob, MainJobLevel, SubJob, Nation, Size;
        public ushort Race, Face, Head, Body, Hands, Legs, Feet, Main, Sub;

        public static WorldCharacter From(JsonObject o) => new()
        {
            ContentId = o["contentId"].GetValue<uint>(),
            CharId = o["charId"].GetValue<uint>(),
            Name = o["name"].GetValue<string>(),
            Rename = o["rename"].GetValue<bool>(),
            Zone = o["zone"].GetValue<ushort>(),
            MainJob = o["mainJob"].GetValue<byte>(),
            MainJobLevel = o["mainJobLevel"].GetValue<byte>(),
            SubJob = o["subJob"].GetValue<byte>(),
            Race = o["race"].GetValue<ushort>(),
            Face = o["face"].GetValue<ushort>(),
            Head = o["head"].GetValue<ushort>(),
            Body = o["body"].GetValue<ushort>(),
            Hands = o["hands"].GetValue<ushort>(),
            Legs = o["legs"].GetValue<ushort>(),
            Feet = o["feet"].GetValue<ushort>(),
            Main = o["main"].GetValue<ushort>(),
            Sub = o["sub"].GetValue<ushort>(),
            Nation = o["nation"].GetValue<byte>(),
            Size = o["size"].GetValue<byte>(),
        };
    }
}
