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
using Org.BouncyCastle.Bcpg;

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

        private string PolProData = "";
        private string ClientVersion = "";   // version string of the lobby login (the client's patch.ver)
        private uint   ClientExpansions = 0; // expansions the client has installed
        private byte[] Password;
        public uint Md5Key;

        // Creating characters is one at a time across the lobby, so two new characters cannot take the same name or
        // character id; selecting is one at a time per PlayOnline member, so two selections cannot both find the
        // member's characters out of the world.
        private static readonly object CreateLock = new();
        private static readonly ConcurrentDictionary<string, object> MemberLocks = new();

        // Session
        private Character[] CachedCharaList = null;
        private string RequestedNewCharName = "";

        public bool IsDisconnected() => Disconnected;

        public string GetPolProData() => PolProData;

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

        // Request info from PolPro
        public bool Login(LobbyLoginPkt loginPkt, out uint key, out ulong serverExpCode)
        {
            if (!Utils.DecryptAuthPassword(loginPkt.AuthCode, out byte[] authHash, out uint clientIp, out ushort clientPort))
            {
                key = 0;
                serverExpCode = 0;
                return false;
            }

            var polData = Database.GetPlayonlineRandomValue(authHash);

            if (polData == null)
            {
                key = 0;
                serverExpCode = 0;
                return false;
            }

            Password = polData.Item1;
            PolProData = polData.Item2;

            if (Password == null)
            {
                key = 0;
                serverExpCode = 0;
                return false;
            }

            Md5Key = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));

            key = Md5Key;

            // The expansions the account has: the ones lobby.cfg enables (the client's own installed set when it
            // does not say)
            serverExpCode = Server.Expansions ?? loginPkt.ClientExpCode;
            // "20100904_2" followed by padding and a trailing marker: keep the version itself
            ClientVersion    = System.Text.RegularExpressions.Regex.Match(System.Text.Encoding.ASCII.GetString(loginPkt.VersionCode), "^[0-9A-Za-z_]*").Value;
            ClientExpansions = loginPkt.ClientExpCode;
            Program.Log.Info($"Lobby login: server expansions 0x{serverExpCode:X}, client installed 0x{loginPkt.ClientExpCode:X}, version {System.Text.Encoding.ASCII.GetString(loginPkt.VersionCode).TrimEnd('\0')}");

            IsLoggedIn = true;
            return true;
        }

        // Verify the received pwd is right
        public bool VerifyPassword(byte[] incomingPassword)
        {
            Md5Key++;
            if (!IsLoggedIn)
                return false;

            byte[] digest = new byte[0x14];
            Array.Copy(Password, digest, 0x10);
            Array.Copy(BitConverter.GetBytes(Md5Key), 0, digest, 0x10, 4);
            return incomingPassword.SequenceEqual(MD5.HashData(digest));
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

            // Create a new character and update the content id
            WorldContainer world = Server.WorldList[charaInfo.WorldNum];
            lock (CreateLock)
            {
                // The world checks the name again when it creates the character
                uint charId = world.Api.CreateCharacter(contentId, RequestedNewCharName, charaInfo, startZone, out uint error);
                if (charId == 0)
                    return error != 0 ? error : WorldApi.ERR_NAME_SERVER;
                // The character is the world's now; the lobby records which content id slot holds it
                uint newSubId = (world.World.Num << 16) | charId;
                if (!Database.UpdateFFXISubContentId(contentId, newSubId, RequestedNewCharName))
                {
                    // Without the record the character would sit on the world out of anyone's reach
                    world.Api.DeleteCharacter(contentId, charId);
                    return WorldApi.ERR_NAME_SERVER;
                }
            }
            return 0;
        }

        public WorldServerInfo? DoSelect(uint contentId, uint ffxiIdWorld, uint serverAddress, uint port)
        {
            byte[] key = new byte[0x14];
            Array.Copy(Password, key, 0x10);
            Array.Copy(BitConverter.GetBytes(Md5Key + 4), 0, key, 0x10, 4);

            lock (MemberLocks.GetOrAdd(PolProData, _ => new object()))
                return SelectLocked(contentId, ffxiIdWorld, key);
        }

        private WorldServerInfo? SelectLocked(uint contentId, uint ffxiIdWorld, byte[] key)
        {
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
                            return null;
                        }
                    }

                    WorldContainer world = Server.GetWorldFromSubContentId(chara.ContentsSubUserId);
                    uint myIp = BitConverter.ToUInt32(((IPEndPoint)ClientSocket.RemoteEndPoint).Address.GetAddressBytes());
                    // The session names the map server the client is sent to (lobby.cfg's world ip/port), not
                    // the address Server.cs passes in, which is hardcoded.
                    // No session (deleted, database error): the map server would refuse the character anyway, so
                    // fail here and the lobby sends an error instead.
                    if (!world.Api.Enter(contentId, ffxiIdWorld, key, world.ServerIp, world.ServerPort, myIp, ClientVersion, ClientExpansions, AccountService.NewToken()))
                        return null;
                    return new(world.World.Num, world.ServerIp, world.ServerPort, world.CacheIp, world.CachePort);
                }
            }

            return null;
        }

        public void DoDelete(uint contentId, uint ffxiIdWorld)
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
                    if (world.Api.DeleteCharacter(contentId, ffxiIdWorld))
                        Database.UpdateFFXISubContentId(contentId, 0, "");
                    return;
                }
            }
        }

        // 0, or the lobby error code to send
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
                            return WorldApi.ERR_NAME_SERVER;
                    }
                    return 0;
                }
            }
            return WorldApi.ERR_NAME_SERVER;
        }

        public void SendError(uint errCode)
        {
            SendPacket(ErrorPkt.OPCODE, new ErrorPkt() { ErrCode = errCode }.Bytes);
        }

        public void SetRequestedCharaName(string charaName)
        {
            RequestedNewCharName = charaName;
        }
    }
}
