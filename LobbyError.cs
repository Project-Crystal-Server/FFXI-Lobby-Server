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

namespace Crystal.FFXILobbyServer
{
    // Lobby error codes sent in ErrorPkt; the client shows code N as FFXI-3N
    public static class LobbyError
    {
        public const uint None            = 0;
        public const uint LoginFailed     = 100; // What the lobby has always sent for a login it cannot place
        public const uint AlreadyLoggedIn = 201; // "Same character already logged in."
        public const uint WorldServer     = 305; // "Unable to connect to the world server. Specified operation failed."
        public const uint NameUnavailable = 313; // "The character name you entered is unavailable."
        public const uint NameServer      = 314; // "Failed to register with the name server."
        public const uint Banned          = 332; // "Could not connect to lobby server. Please check this title's news for announcements."
    }
}
