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
using System.Text;

namespace Crystal.FFXILobbyServer.Network.Models
{
    [StructLayout(LayoutKind.Sequential, Size = SIZE)]
    public unsafe struct World
    {
        public const int SIZE = 0x14;

        public uint Num;
        public fixed byte Name_[0x10];

        public string Name
        {
            get
            {
                fixed (byte* ptr = &Name_[0])
                {
                    string str = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(ptr, 0x10));
                    return str[..str.IndexOf('\0')];
                }
            }

            set
            {
                ReadOnlySpan<byte> name = Encoding.ASCII.GetBytes(value);
                int len = name.Length <= 0x10 ? name.Length : 0x10;
                fixed (byte* pName = &Name_[0])
                {
                    name.CopyTo(new Span<byte>(pName, len));
                }
            }
        }
    }
}
