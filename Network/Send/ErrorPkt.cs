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

using System.Runtime.InteropServices;
using System;

namespace Crystal.FFXILobbyServer.Network.Send
{
    public struct ErrorPkt
    {
        // Pkt Info
        public const int OPCODE = 0x04;

        // Pkt Data: the client shows the code at offset 4 (packet byte 32, after the 28-byte header) and never
        // reads offset 0; retail sent 0x10 there.
        public uint Unknown = 0x10;
        public uint ErrCode;

        public ErrorPkt() { }

        public byte[] Bytes
        {
            get
            {
                Span<byte> response = new byte[8];
                MemoryMarshal.Write(response, in Unknown);
                MemoryMarshal.Write(response[4..], in ErrCode);
                return response.ToArray();
            }
        }
    }
}
