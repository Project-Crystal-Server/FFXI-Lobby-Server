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
using System.Net;
using System.Xml;

namespace Crystal.FFXILobbyServer
{
    public class FFXILobbyConfig
    {
        public readonly string ServerIp;

        public readonly string PolProNotiferId;
        public readonly string PolProNotiferPassword;
        public readonly string PolProNotiferIp;

        public readonly string PolDbHost;
        public readonly string PolDbPort;
        public readonly string PolDbName;
        public readonly string PolDbUsername;
        public readonly string PolDbPassword;

        public readonly List<WorldContainer> WorldList;

        // Where the account service listens (<accounts listen="127.0.0.1:54005"/>); null: not started
        public readonly string AccountsListen;

        public FFXILobbyConfig(string path) 
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Program.Log.Info($"Loading config: {path}");
            XmlDocument doc = new();

            doc.Load(path);

            // Load the server configs
            XmlNode cfgNode = doc.DocumentElement.SelectSingleNode("/lobbycfg");
            ServerIp = cfgNode.Attributes["serverIp"]?.InnerText;

            // Go through subsettings
            List<WorldContainer> tempWorldList = [];
            foreach (XmlNode cfgChildNode in doc.DocumentElement.ChildNodes)
            {
                if (cfgChildNode.Name.Equals("poldb"))
                {
                    PolDbHost = cfgChildNode.Attributes["host"]?.InnerText;
                    PolDbPort = cfgChildNode.Attributes["port"]?.InnerText;
                    PolDbName = cfgChildNode.Attributes["database"]?.InnerText;
                    PolDbUsername = cfgChildNode.Attributes["username"]?.InnerText;
                    PolDbPassword = cfgChildNode.Attributes["password"]?.InnerText;
                }
                if (cfgChildNode.Name.Equals("accounts"))
                {
                    AccountsListen = cfgChildNode.Attributes["listen"]?.InnerText;
                }
                if (cfgChildNode.Name.Equals("worlds"))
                {
                    foreach (XmlNode worldNode in cfgChildNode.ChildNodes)
                    {
                        if (worldNode.Name.Equals("world"))
                        {
                            ushort num = ushort.Parse(worldNode.Attributes["id"]?.InnerText);
                            string name = worldNode.Attributes["name"]?.InnerText;
                            string srvDbHost = worldNode.Attributes["dbHost"]?.InnerText;
                            string srvDbPort = worldNode.Attributes["dbPort"]?.InnerText;
                            string srvName = worldNode.Attributes["dbName"]?.InnerText;
                            string srvUser = worldNode.Attributes["dbUser"]?.InnerText;
                            string srvPass = worldNode.Attributes["dbPass"]?.InnerText;

                            uint srvIp = BitConverter.ToUInt32(IPAddress.Parse(worldNode.Attributes["ip"]?.InnerText).GetAddressBytes()); 
                            uint srvPort = uint.Parse(worldNode.Attributes["port"]?.InnerText);
                            uint cacheIp = BitConverter.ToUInt32(IPAddress.Parse(worldNode.Attributes["cacheIp"]?.InnerText).GetAddressBytes());
                            uint cachePort = uint.Parse(worldNode.Attributes["cachePort"]?.InnerText);

                            tempWorldList.Add(new(
                                new World() { Num = num, Name = name},
                                srvDbHost,
                                srvDbPort, 
                                srvName, 
                                srvUser, 
                                srvPass,
                                srvIp,
                                srvPort,
                                cacheIp,
                                cachePort
                            )
                            {
                                SettingsDir = worldNode.Attributes["settingsDir"]?.InnerText ?? "",
                                AccountsKey = worldNode.Attributes["accountsKey"]?.InnerText ?? "",
                            });
                        }
                    }
                    WorldList = tempWorldList;
                }
            }
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }

}