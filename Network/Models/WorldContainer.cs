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
        // The world server's API (its /api/lobby) and the key it takes: the lobby reaches a world only through it
        public readonly string ApiUrl;
        public readonly string ApiKey;

        public readonly uint ServerIp;
        public readonly uint ServerPort;
        public readonly uint CacheIp;
        public readonly uint CachePort;

        // The key this world's map servers show the account service (AccountService). Empty: the world cannot use it.
        public string AccountsKey = "";

        public readonly WorldApi Api;

        public WorldContainer(World world, string apiUrl, string apiKey, uint srvIp, uint srvPort, uint cacheIp, uint cachePort)
        {
            World = world;
            ApiUrl = apiUrl;
            ApiKey = apiKey;
            Api = new WorldApi(this);

            ServerIp = srvIp;
            ServerPort = srvPort;
            CacheIp = cacheIp;
            CachePort = cachePort;
        }
    }
}
