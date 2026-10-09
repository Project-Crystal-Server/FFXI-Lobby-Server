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

        // Expansion bits of the lobby login answer (LandSandBoat login_helpers.h EXPANSION_DISPLAY), by the name
        // <expansions enabled="..."/> uses. The base game is always enabled.
        private static readonly Dictionary<string, uint> ExpansionBits = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ROTZ"] = 0x0002,
            ["COP"] = 0x0004,
            ["TOAU"] = 0x0008,
            ["WOTG"] = 0x0010,
            ["ACP"] = 0x0020,
            ["AMK"] = 0x0040,
            ["ASA"] = 0x0080,
            ["ABYSSEA"] = 0x0100 | 0x0200 | 0x0400, // Visions, Scars, Heroes
            ["SOA"] = 0x0800,
        };

        // The expansions the lobby reports as enabled (<expansions enabled="ROTZ,COP,..."/>); null: not set, so the
        // client's own installed set stands
        public readonly uint? Expansions;

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
                if (cfgChildNode.Name.Equals("expansions") && cfgChildNode.Attributes["enabled"] != null)
                {
                    uint mask = 0x0001; // base game
                    foreach (string name in cfgChildNode.Attributes["enabled"].InnerText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!ExpansionBits.TryGetValue(name, out uint bits))
                            throw new FormatException($"<expansions enabled=\"...\"/> names {name}, which is not one of {string.Join(", ", ExpansionBits.Keys)}");
                        mask |= bits;
                    }
                    Expansions = mask;
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
                            // The client's world list holds 16 bytes for a name, the last of them its end
                            if (name?.Length > 15)
                                throw new FormatException($"world name \"{name}\" is longer than 15 characters");
                            string apiUrl = worldNode.Attributes["api"]?.InnerText;
                            string apiKey = worldNode.Attributes["apiKey"]?.InnerText;
                            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey))
                                throw new FormatException($"world {name} needs api (its HTTP API address) and apiKey (its LOBBY_API_KEY)");

                            uint srvIp = BitConverter.ToUInt32(IPAddress.Parse(worldNode.Attributes["ip"]?.InnerText).GetAddressBytes()); 
                            uint srvPort = uint.Parse(worldNode.Attributes["port"]?.InnerText);
                            uint cacheIp = BitConverter.ToUInt32(IPAddress.Parse(worldNode.Attributes["cacheIp"]?.InnerText).GetAddressBytes());
                            uint cachePort = uint.Parse(worldNode.Attributes["cachePort"]?.InnerText);

                            tempWorldList.Add(new(
                                new World() { Num = num, Name = name},
                                apiUrl,
                                apiKey,
                                srvIp,
                                srvPort,
                                cacheIp,
                                cachePort
                            )
                            {
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