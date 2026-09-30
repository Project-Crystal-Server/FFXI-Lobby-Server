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

namespace Crystal.FFXILobbyServer.Network.Models
{
    public class WorldContainer
    {
        public readonly World World;
        public readonly string DbHost;
        public readonly string DbPort;
        public readonly string DbName;
        public readonly string DbUser;
        public readonly string DbPass;

        public readonly uint ServerIp;
        public readonly uint ServerPort;
        public readonly uint CacheIp;
        public readonly uint CachePort;

        // The world server's settings folder (LandSandBoat / Phoenix `settings`): the expansions it enables are
        // the ones the lobby reports (Utils.ServerExpansions). Empty: not configured.
        public string SettingsDir = "";

        public WorldContainer(World world, string host, string port, string name, string usr, string pass, uint srvIp, uint srvPort, uint cacheIp, uint cachePort)
        {
            World = world;
            DbHost = host;
            DbPort = port;
            DbName = name;
            DbUser = usr;
            DbPass = pass;

            ServerIp = srvIp;
            ServerPort = srvPort;
            CacheIp = cacheIp;
            CachePort = cachePort;
        }
    }
}
