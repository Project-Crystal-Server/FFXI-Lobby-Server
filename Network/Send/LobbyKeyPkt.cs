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

using System;
using System.Runtime.InteropServices;

namespace Crystal.FFXILobbyServer.Network.Send
{
    public unsafe struct LobbyKeyPkt
    {
        // Pkt Info
        public const int OPCODE = 0x05;
        public const int SIZE = 0x18;

        // Pkt Data
        public uint Key;
        public ulong ServerExpCode;

        public readonly byte[] Bytes
        {
            get
            {
                Span<byte> response = new byte[SIZE];
                MemoryMarshal.Write(response, in Key);
                MemoryMarshal.Write(response[4..], in ServerExpCode);

                return response.ToArray();
            }
        }
    }
}
