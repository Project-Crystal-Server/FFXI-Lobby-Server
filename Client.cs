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

using Crystal.FFXILobbyServer.Network;
using Crystal.FFXILobbyServer.Network.Receive;
using Crystal.FFXILobbyServer.Network.Send;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Crystal.FFXILobbyServer.Network.Models;
using Crystal.POLProfile.DataObjects.Pol.Character;

namespace Crystal.FFXILobbyServer
{
    class Client
    {
        // Connection stuff
        public Server Server;
        public Socket ClientSocket;
        private IPAddress ClientIp;
        public int ClientPort;
        public byte[] Buffer = new byte[0xffff];
        public int LastPartialSize = 0;
        private bool Disconnected = false;

        // PolPro
        private bool IsLoggedIn = false;
        public byte[] AuthCode { get; private set; }
        private byte[] Password;
        private ulong PolId = 0;
        public string PolProData => Utils.PolIdToPolProData(PolId);

        // Client Info
        private string ClientVersion = "";
        private uint ClientExpansions = 0;
        public uint Md5Key;

        // Session
        private Character[] CachedCharaList = null;
        private string RequestedNewCharName = "";
        private WorldContainer RequestedNewCharWorld = null;

        private static readonly object CreateLock = new();
        private static readonly ConcurrentDictionary<ulong, object> MemberLocks = new();

        public bool IsDisconnected() => Disconnected;

        public string GetAddress() => $"{ClientIp}:{ClientPort}";

        public Client(Server server, Socket socket)
        {
            var endpoint = ((IPEndPoint)socket.RemoteEndPoint);

            Server = server;
            ClientSocket = socket;
            ClientIp = endpoint.Address;
            ClientPort = endpoint.Port;
        }

        public void SendPacket(uint opcode, byte[] data)
        {
            FFXIPacket packet = new(opcode, data);
            ClientSocket.Send(packet.GetPacketBytes());

            //Program.Log.Debug("\n" + Utils.ByteArrayToHex(packet.GetPacketBytes()));
        }

        public void SendBytes(byte[] packetBytes)
        {
            ClientSocket.Send(packetBytes);
        }

        public void Disconnect()
        {
            if (Disconnected)
                return;

            Disconnected = true;

            try
            {
                ClientSocket.Shutdown(SocketShutdown.Both);
                ClientSocket.Close();
            }
            catch (Exception) { }
        }

        public override string ToString()
        {
            return IsLoggedIn ? PolProData : ClientIp.ToString();
        }

        // Request info from PolPro. 0, or the lobby error code to send.
        public uint Login(LobbyLoginPkt loginPkt, out uint key, out ulong serverExpCode)
        {
            serverExpCode = Server.Expansions ?? loginPkt.ClientExpCode;
            ClientVersion = System.Text.RegularExpressions.Regex.Match(System.Text.Encoding.ASCII.GetString(loginPkt.VersionCode), "^[0-9A-Za-z_]*").Value;
            ClientExpansions = loginPkt.ClientExpCode;

            // Check Version - No version accepts anything
            if (Server.Version != null && !ClientVersion.Equals(Server.Version))
            {
                key = 0;
                serverExpCode = 0;
                return LobbyError.VersionMismatch;
            }

            // Try to decrypt auth password and retrieve profile data
            if (!Utils.DecryptAuthPassword(loginPkt.AuthCode, out byte[] authHash, out uint clientIp, out ushort clientPort))
            {
                key = 0;
                serverExpCode = 0;
                return LobbyError.LoginFailed;
            }

            PolProfileData profile = Database.RetrievePolProfileData(authHash);

            if (profile == null)
            {
                key = 0;
                serverExpCode = 0;
                return LobbyError.LoginFailed;
            }

            Password = profile.RandomValue;
            PolId = profile.PolId;
            AuthCode = (byte[])loginPkt.AuthCode.Clone();

            if (PolId == 0)
                Program.Log.Error($"{ClientIp} - Session's POL ID is malformed (Utils.PolProDataToPolId gave 0); refusing the login");

            if (Password == null || PolId == 0)
            {
                key = 0;
                serverExpCode = 0;
                return LobbyError.LoginFailed;
            }

            if (profile.IsPolAccountSuspended)
            {
                Program.Log.Warn($"{PolProData} - Account is suspended on the POL side; refusing the login");
                key = 0;
                serverExpCode = 0;
                return LobbyError.Banned;
            }

            Md5Key = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));

            key = Md5Key;

            Program.Log.Info($"Lobby login: server expansions 0x{serverExpCode:X}, client installed 0x{loginPkt.ClientExpCode:X}, version {System.Text.Encoding.ASCII.GetString(loginPkt.VersionCode).TrimEnd('\0')}");

            IsLoggedIn = true;
            return LobbyError.None;
        }

        public bool VerifyPassword(byte[] incomingPassword)
        {
            Md5Key++;
            if (!IsLoggedIn)
                return false;

            byte[] digest = new byte[0x14];
            Array.Copy(Password, digest, 0x10);
            Array.Copy(BitConverter.GetBytes(Md5Key), 0, digest, 0x10, 4);
            if (incomingPassword.SequenceEqual(MD5.HashData(digest)))
                return true;
            Program.Log.Warn($"{PolProData} - Request password did not match the lobby key");
            return false;
        }

        public Character[] GetCharacters(bool invalidate)
        {
            if (!invalidate && CachedCharaList != null)
                return [.. CachedCharaList];

            // Get all the ids
            CharacterPrimitive[] contentIds = Database.GetFFXIContentIds(PolProData);

            // Grab chara data from each server
            CachedCharaList = Database.GetCharacters(Server.WorldList, contentIds);
            return CachedCharaList;
        }

        public void ClearCharacters()
        {
            CachedCharaList = null;
        }

        // 0, or the lobby error code to send
        public uint CreateCharacter(uint contentId, byte[] password, CharaInfo charaInfo)
        {
            // Check Race

            // Set starting zone
            uint[] bastokStartingZones = { 0xEA, 0xEB, 0xEC };
            uint[] sandoriaStartingZones = { 0xE6, 0xE7, 0xE8 };
            uint[] windurstStartingZones = { 0xEE, 0xF0, 0xF1 };
            Random random = new();
            uint startZone = 0;

            switch (charaInfo.TownNum)
            {
                case 0x02: // windy start
                    {
                        startZone = windurstStartingZones[random.Next(3)];
                        break;
                    }
                case 0x01: // bastok start
                    {
                        startZone = bastokStartingZones[random.Next(3)];
                        break;
                    }
                case 0x00: // sandy start
                    {
                        startZone = sandoriaStartingZones[random.Next(3)];
                        break;
                    }
            }

            // Create the character on the world the name was checked against, which the client picked by name
            WorldContainer world = RequestedNewCharWorld;
            if (world == null)
                return LobbyError.WorldServer;
            // The client names the content id, so create only on one of the member's own empty slots
            if (!Database.IsEmptyFFXISlot(contentId, PolProData))
            {
                Program.Log.Warn($"{PolProData} - Refused to create a character on content id {contentId}, which is not an empty slot of theirs");
                return LobbyError.NameServer;
            }

            // The world checks the name again when it creates the character
            uint charId = world.Api.CreateCharacter(contentId, PolId, RequestedNewCharName, charaInfo, startZone, out uint error);
            if (charId == 0)
                return error != 0 ? error : LobbyError.NameServer;

            // The character is the world's now; the lobby records which content id slot holds it
            uint newSubId = (world.World.Num << 16) | charId;
            if (!Database.UpdateFFXISubContentId(contentId, newSubId, RequestedNewCharName))
            {
                // Without the record the character would sit on the world out of anyone's reach
                world.Api.DeleteCharacter(contentId, charId);
                return LobbyError.NameServer;
            }
            return 0;
        }

        // The world to send the client to, or null with the lobby error code to send
        public WorldServerInfo? DoSelect(uint contentId, uint ffxiIdWorld, out uint error)
        {
            byte[] key = new byte[0x14];
            Array.Copy(Password, key, 0x10);
            Array.Copy(BitConverter.GetBytes(Md5Key + 4), 0, key, 0x10, 4);

            lock (MemberLocks.GetOrAdd(PolId, _ => new object()))
                return SelectLocked(contentId, ffxiIdWorld, key, out error);
        }

        private WorldServerInfo? SelectLocked(uint contentId, uint ffxiIdWorld, byte[] key, out uint error)
        {
            error = LobbyError.WorldServer;
            // This shit is stupid but we gotta find the world id to delete from the correct work.
            CharacterPrimitive[] contentIds = Database.GetFFXIContentIds(PolProData);
            foreach (CharacterPrimitive chara in contentIds)
            {
                if (chara.ContentsId == contentId && (chara.ContentsSubUserId & 0xFFFF) == ffxiIdWorld)
                {
                    // One character in the world per PlayOnline member, whichever of the member's content ids it is on
                    foreach (CharacterPrimitive other in contentIds)
                    {
                        WorldContainer otherWorld = other.ContentsSubUserId == 0 ? null : Server.GetWorldFromSubContentId(other.ContentsSubUserId);
                        if (otherWorld != null && otherWorld.Api.AnyOnline([other.ContentsSubUserId & 0xFFFF]))
                        {
                            Program.Log.Warn($"{PolProData} - Character {other.ContentsSubUserId & 0xFFFF} is still logged in");
                            error = LobbyError.AlreadyLoggedIn;
                            return null;
                        }
                    }

                    WorldContainer world = Server.GetWorldFromSubContentId(chara.ContentsSubUserId);
                    uint myIp = BitConverter.ToUInt32(((IPEndPoint)ClientSocket.RemoteEndPoint).Address.GetAddressBytes());
                    // The session names the map server the client is sent to (lobby.cfg's world ip/port), not
                    // the address Server.cs passes in, which is hardcoded.
                    // No session (deleted, database error): the map server would refuse the character anyway, so
                    // fail here and the lobby sends an error instead.
                    error = world.Api.Enter(contentId, ffxiIdWorld, PolId, key, world.ServerIp, world.ServerPort, myIp, ClientVersion, ClientExpansions);
                    if (error != 0)
                        return null;
                    error = 0;
                    return new(world.World.Num, world.ServerIp, world.ServerPort, world.CacheIp, world.CachePort);
                }
            }

            return null;
        }

        // Whether the character is gone: the world let it go and the lobby freed its slot
        public bool DoDelete(uint contentId, uint ffxiIdWorld)
        {
            byte[] key = new byte[0x14];
            Array.Copy(Password, key, 0x10);
            Array.Copy(BitConverter.GetBytes(Md5Key + 4), 0, key, 0x10, 4);

            // This shit is stupid but we gotta find the world id to delete from the correct work.
            CharacterPrimitive[] contentIds = Database.GetFFXIContentIds(PolProData);
            foreach (CharacterPrimitive chara in contentIds)
            {
                if (chara.ContentsId == contentId && (chara.ContentsSubUserId & 0xFFFF) == ffxiIdWorld)
                {
                    WorldContainer world = Server.GetWorldFromSubContentId(chara.ContentsSubUserId);
                    // The slot is freed only once the world has let the character go
                    return world != null && world.Api.DeleteCharacter(contentId, ffxiIdWorld) && Database.UpdateFFXISubContentId(contentId, 0, "");
                }
            }
            return false;
        }

        public uint DoRename(uint contentId, uint ffxiIdWorld, string newName)
        {
            byte[] key = new byte[0x14];
            Array.Copy(Password, key, 0x10);
            Array.Copy(BitConverter.GetBytes(Md5Key + 4), 0, key, 0x10, 4);

            // This shit is stupid but we gotta find the world id to delete from the correct work.
            CharacterPrimitive[] contentIds = Database.GetFFXIContentIds(PolProData);
            foreach (CharacterPrimitive chara in contentIds)
            {
                if (chara.ContentsId == contentId && (chara.ContentsSubUserId & 0xFFFF) == ffxiIdWorld)
                {
                    WorldContainer world = Server.GetWorldFromSubContentId(chara.ContentsSubUserId);
                    lock (CreateLock)
                    {
                        uint error = world.Api.RenameCharacter(contentId, ffxiIdWorld, newName);
                        if (error != 0)
                            return error;
                        if (!Database.UpdateFFXISubContentId(contentId, chara.ContentsSubUserId, newName))
                            return LobbyError.NameServer;
                    }
                    return LobbyError.None;
                }
            }
            return LobbyError.NameServer;
        }

        // The client moves its key on an error answer just as on an OK one
        public void SendError(uint errCode)
        {
            Md5Key++;
            Program.Log.Warn($"{this} - Sent lobby error {errCode}");
            SendPacket(ErrorPkt.OPCODE, new ErrorPkt() { ErrCode = errCode }.Bytes);
        }

        public void SetRequestedCharaName(string charaName, WorldContainer world)
        {
            RequestedNewCharName = charaName;
            RequestedNewCharWorld = world;
        }
    }
}
