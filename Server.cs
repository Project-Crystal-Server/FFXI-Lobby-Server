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
using Crystal.FFXILobbyServer.Network.Models;
using Crystal.FFXILobbyServer.Network.Receive;
using Crystal.FFXILobbyServer.Network.Send;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Crystal.FFXILobbyServer
{
    public class Server
    {
        public const int BUFFER_SIZE = 0xFFFF;
        public const int BACKLOG = 100;

        private Socket ServerSocket;

        public readonly List<WorldContainer> WorldList;

        // Started next to the lobby when lobby.cfg has <accounts listen="..."/>
        public AccountService Accounts;

        private readonly List<Client> ClientList = [];

        public Server(List<WorldContainer> worldList)
        {
            WorldList = worldList;
        }

        public WorldContainer GetWorldFromSubContentId(uint subContentId)
        {
            int worldNum = (int)(subContentId >> 16) & 0xFFFF;
            return WorldList.Where(container => container.World.Num == worldNum).FirstOrDefault();
        }

        public WorldContainer GetWorldFromName(string name)
        {
            return WorldList.Where(container => container.World.Name == name).FirstOrDefault();
        }

        #region Socket Handling
        public bool StartServer(int port)
        {
            IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), port);

            try
            {
                ServerSocket = new Socket(serverEndPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not Create socket, check to make sure not duplicating port", e);
            }
            try
            {
                ServerSocket.Bind(serverEndPoint);
                ServerSocket.Listen(BACKLOG);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Error occured while binding socket, check inner exception", e);
            }
            try
            {
                ServerSocket.BeginAccept(new AsyncCallback(AcceptCallback), ServerSocket);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Error occured starting listeners, check inner exception", e);
            }

            Console.ForegroundColor = ConsoleColor.White;
            Program.Log.Info("Lobby Server has started @ {0}:{1}", (ServerSocket.LocalEndPoint as IPEndPoint).Address, (ServerSocket.LocalEndPoint as IPEndPoint).Port);
            Console.ForegroundColor = ConsoleColor.Gray;

            return true;
        }

        private void AcceptCallback(IAsyncResult result)
        {
            // Grab client socket and add to the list
            Client conn = null;
            lock (ClientList)
            {
                Socket serverSocket = (Socket)result.AsyncState;
                try
                {
                    conn = new(this, serverSocket.EndAccept(result));
                    conn.Buffer = new byte[BUFFER_SIZE];
                }
                catch (SocketException)
                {
                    return; // Server was shut down
                }

                ClientList.Add(conn);
            }

            // Start receiving from client
            try
            {
                //Queue receiving of data from the connection
                conn.ClientSocket.BeginReceive(conn.Buffer, 0, conn.Buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), conn);
                Program.Log.Info($"Connection {conn} has connected.");
            }
            catch (SocketException)
            {
                // Client socket failed
                if (conn.ClientSocket != null)
                {
                    conn.ClientSocket.Close();
                    lock (ClientList)
                    {
                        ClientList.Remove(conn);
                    }
                }
            }

            //Queue the accept of the next incomming connection
            ServerSocket.BeginAccept(new AsyncCallback(AcceptCallback), ServerSocket);
        }

        private void ReceiveCallback(IAsyncResult result)
        {
            Client conn = (Client)result.AsyncState;

            try
            {
                int bytesRead = conn.ClientSocket.EndReceive(result);

                bytesRead += conn.LastPartialSize;

                if (bytesRead > 0)
                {
                    int offset = 0;

                    //Build packets until can no longer or out of data
                    while (true)
                    {
                        if (bytesRead - offset < FFXIPacket.HEADER_SIZE)
                            break;

                        FFXIPacket packet = new FFXIPacket(conn.Buffer, ref offset);
                        if (packet == null)
                            break;

                        //packet.DebugPrintPacket();

                        // Process Packets
                        if (packet.Header.opcode == LobbyLoginPkt.OPCODE)
                        {
                            LobbyLoginPkt loginPkt = LobbyLoginPkt.Cast(packet.Data);
                            if (conn.Login(loginPkt, out uint key, out ulong expCode))
                            {
                                LobbyKeyPkt responsePkt = new()
                                {
                                    Key = key,
                                    ServerExpCode = expCode
                                };
                                conn.SendPacket(LobbyKeyPkt.OPCODE, responsePkt.Bytes);
                                Program.Log.Info($"{conn.GetPolProData()} - Logged into lobby server");
                            }
                            else
                                conn.SendError(100);
                        }
                        else if (packet.Header.opcode == GetCharPkt.OPCODE)
                        {
                            GetCharPkt requestPkt = GetCharPkt.Cast(packet.Data);
                            if (conn.VerifyPassword(requestPkt.Password))
                            {
                                Character[] chara = conn.GetCharacters(false);
                                if (chara != null)
                                {
                                    CharaListPkt responsePkt = new()
                                    {
                                        NumChara = (uint) chara.Length,
                                        Characters = chara,
                                    };

                                    conn.Md5Key++;

                                    conn.SendPacket(CharaListPkt.OPCODE, responsePkt.Bytes);
                                    Program.Log.Info($"{conn.GetPolProData()} - Retrieving characters");
                                }
                                else
                                {
                                    conn.SendError(0);
                                }
                            }
                            else
                                conn.SendError(0);

                        }
                        else if (packet.Header.opcode == QueryWorldListPkt.OPCODE)
                        {
                            QueryWorldListPkt requestPkt = QueryWorldListPkt.Cast(packet.Data);
                            if (conn.VerifyPassword(requestPkt.Password))
                            {
                                WorldListPkt responsePkt = new()
                                {
                                    NumWorlds = (uint) WorldList.Count,
                                    WorldList = WorldList.Select(container => container.World).ToArray(),
                                };

                                conn.Md5Key++;
                                conn.SendPacket(WorldListPkt.OPCODE, responsePkt.Bytes);
                                Program.Log.Info($"{conn.GetPolProData()} - Retrieving world list");
                            }
                            else
                                conn.SendError(0);
                        }
                        else if (packet.Header.opcode == CreateCharPrePkt.OPCODE)
                        {
                            CreateCharPrePkt requestPkt = CreateCharPrePkt.Cast(packet.Data);
                            if (conn.VerifyPassword(requestPkt.Password))
                            {
                                WorldContainer world = GetWorldFromName(requestPkt.WorldName);
                                uint nameError = world == null ? 0 : Database.CharacterNameError(world, requestPkt.Name);
                                if (nameError != 0)
                                    conn.SendError(nameError);
                                else if (world != null)
                                {
                                    conn.SetRequestedCharaName(requestPkt.Name);
                                    conn.Md5Key++;
                                    conn.SendPacket(OkPkt.OPCODE, new OkPkt().Bytes);
                                    Program.Log.Info($"{conn.GetPolProData()} - PreCreateing a character");
                                }
                                else
                                    conn.SendError(0);
                            }
                            else
                                conn.SendError(0);
                        }
                        else if (packet.Header.opcode == CreateCharPkt.OPCODE)
                        {
                            CreateCharPkt requestPkt = CreateCharPkt.Cast(packet.Data);
                            if (conn.VerifyPassword(requestPkt.Password))
                            {
                                uint error = conn.CreateCharacter(requestPkt.FFXIId, requestPkt.Password, requestPkt.NewCharaInfo);
                                if (error != 0)
                                    conn.SendError(error);
                                else
                                {
                                    conn.Md5Key++;
                                    conn.SendPacket(OkPkt.OPCODE, new OkPkt().Bytes);
                                    conn.ClearCharacters();
                                    Program.Log.Info($"{conn.GetPolProData()} - Creating a character");
                                }
                            }
                            else
                                conn.SendError(0);
                        }
                        else if (packet.Header.opcode == SelectCharPkt.OPCODE)
                        {
                            SelectCharPkt requestPkt = SelectCharPkt.Cast(packet.Data);
                            if (conn.VerifyPassword(requestPkt.Password))
                            {
                                conn.GetCharacters(true);

                                WorldServerInfo info = conn.DoSelect(requestPkt.FFXIId, requestPkt.FFXIIdWorld, 0x2601A8C0, 54230);

                                if (info != null)
                                {
                                    NextLoginPkt response = new()
                                    {
                                        FFXIId = requestPkt.FFXIId,
                                        FFXIIdWorld = requestPkt.FFXIIdWorld,
                                        CharacterName = requestPkt.CharacterName,
                                        ServerId = info.Id,
                                        ServerIp = info.ServerIp,
                                        ServerPort = info.ServerPort,
                                        CacheIp = info.CacheIp,
                                        CachePort = info.CachePort

                                    };

                                    conn.Md5Key++;
                                    conn.SendPacket(NextLoginPkt.OPCODE, response.Bytes);
                                    conn.Disconnect();
                                    ClientList.Remove(conn);
                                    Program.Log.Info($"{conn.GetPolProData()} - Selected a character");
                                }
                                else
                                    conn.SendError(0);
                            }
                            else
                                conn.SendError(0);
                        }
                        else if (packet.Header.opcode == DeleteCharPkt.OPCODE)
                        {   
                            DeleteCharPkt requestPkt = DeleteCharPkt.Cast(packet.Data);
                            if (conn.VerifyPassword(requestPkt.Password))
                            {
                                conn.DoDelete(requestPkt.FFXIId, requestPkt.FFXIIdWorld);
                                conn.Md5Key++;
                                conn.SendPacket(OkPkt.OPCODE, new OkPkt().Bytes);
                                conn.ClearCharacters();
                                Program.Log.Info($"{conn.GetPolProData()} - Deleting a character");
                            }
                            else
                                conn.SendError(0);
                        }
                        else if (packet.Header.opcode == RenameCharPkt.OPCODE)
                        {
                            RenameCharPkt requestPkt = RenameCharPkt.Cast(packet.Data);
                            if (conn.VerifyPassword(requestPkt.Password))
                            {
                                uint error = conn.DoRename(requestPkt.FFXIId, requestPkt.FFXIIdWorld, requestPkt.NewName);
                                if (error != 0)
                                    conn.SendError(error);
                                else
                                {
                                    conn.Md5Key++;
                                    conn.SendPacket(OkPkt.OPCODE, new OkPkt().Bytes);
                                    conn.ClearCharacters();
                                    Program.Log.Info($"{conn.GetPolProData()} - Renaming a character");
                                }
                            }
                            else
                                conn.SendError(0);
                        }
                    }

                    if (!conn.IsDisconnected())
                        conn.ClientSocket.BeginReceive(conn.Buffer, 0, conn.Buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), conn);
                }
                else
                {
                    Program.Log.Info("{0} has disconnected.", conn.GetAddress());

                    lock (ClientList)
                    {
                        conn.Disconnect();
                        ClientList.Remove(conn);
                    }
                }
            }
            catch (SocketException)
            {
                if (conn.ClientSocket != null)
                {
                    Program.Log.Info("{0} has disconnected.", conn.GetAddress());

                    lock (ClientList)
                    {
                        ClientList.Remove(conn);
                    }
                }
            }
        }

        #endregion
    }
}
