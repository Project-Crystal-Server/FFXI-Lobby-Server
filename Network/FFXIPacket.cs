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
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NLog;
using NLog.Targets;

namespace Crystal.FFXILobbyServer.Network
{
    public class FFXIPacket
    {
        public const int HEADER_SIZE = 28;
        public const uint HEADER_SIGNATURE = 0x46465849; //FFXI

        public FFXIPacketHeader Header;
        public byte[] Data;

        //Loads a sniffed packet from a file
        public unsafe FFXIPacket(string path)
        {
            var bytes = File.ReadAllBytes(path);

            if (bytes.Length < HEADER_SIZE)
                throw new OverflowException("Packet Error: Packet was too small");

            fixed (byte* pdata = &bytes[0])
            {
                Header = (FFXIPacketHeader)Marshal.PtrToStructure(new nint(pdata), typeof(FFXIPacketHeader));
            }

            if (bytes.Length < Header.packetSize)
                throw new OverflowException("Packet Error: Packet size didn't equal given size");

            uint packetSize = Header.packetSize;

            if (packetSize - HEADER_SIZE != 0)
            {
                Data = new byte[packetSize - HEADER_SIZE];
                Array.Copy(bytes, HEADER_SIZE, Data, 0, packetSize - HEADER_SIZE);
            }
            else
                Data = [];
        }

        //Loads a sniffed packet from a byte array
        public unsafe FFXIPacket(byte[] bytes)
        {
            if (bytes.Length < HEADER_SIZE)
                throw new OverflowException("Packet Error: Packet was too small");

            fixed (byte* pdata = &bytes[0])
            {
                Header = (FFXIPacketHeader)Marshal.PtrToStructure(new nint(pdata), typeof(FFXIPacketHeader));
            }

            if (bytes.Length < Header.packetSize)
                throw new OverflowException("Packet Error: Packet size didn't equal given size");

            uint packetSize = Header.packetSize;

            Data = new byte[packetSize - HEADER_SIZE];
            Array.Copy(bytes, HEADER_SIZE, Data, 0, packetSize - HEADER_SIZE);
        }

        public unsafe FFXIPacket(byte[] bytes, ref int offset)
        {
            if (bytes.Length < offset + HEADER_SIZE)
                throw new OverflowException("Packet Error: Packet was too small");

            fixed (byte* pdata = &bytes[offset])
            {
                Header = (FFXIPacketHeader)Marshal.PtrToStructure(new nint(pdata), typeof(FFXIPacketHeader));
            }

            if (Header.signature != HEADER_SIGNATURE)
                throw new Exception("Signature was not FFXI");

            int packetSize = (int)Header.packetSize;

            if (bytes.Length < offset + Header.packetSize)
                throw new OverflowException("Packet Error: Packet size didn't equal given size");

            Data = new byte[packetSize - HEADER_SIZE];
            Array.Copy(bytes, offset + HEADER_SIZE, Data, 0, packetSize - HEADER_SIZE);

            offset += packetSize; // packetSize counts the header
        }

        public FFXIPacket(FFXIPacketHeader header, byte[] data)
        {
            this.Header = header;
            this.Header.packetSize = (uint)data.Length + HEADER_SIZE;
            this.Data = data;
            CreateHash();
        }

        public FFXIPacket(uint opcode, byte[] packetData)
        {
            Header = new FFXIPacketHeader();
            Header.signature = HEADER_SIGNATURE;
            Header.opcode = opcode;
            Header.packetSize = (packetData != null ? (uint)packetData.Length : 0) + HEADER_SIZE;
            Data = packetData != null ? packetData : [];
            CreateHash();
        }

        public static unsafe FFXIPacketHeader GetHeader(byte[] bytes)
        {
            FFXIPacketHeader header;
            if (bytes.Length < HEADER_SIZE)
                throw new OverflowException("Packet Error: Packet was too small");

            fixed (byte* pdata = &bytes[0])
            {
                header = (FFXIPacketHeader)Marshal.PtrToStructure(new nint(pdata), typeof(FFXIPacketHeader));
            }

            return header;
        }

        public static FFXIPacketHeader CreateHeader(uint opcode)
        {
            FFXIPacketHeader header = new FFXIPacketHeader
            {
                signature = HEADER_SIGNATURE,
                opcode = opcode
            };
            return header;
        }

        public byte[] GetHeaderBytes()
        {
            var size = Marshal.SizeOf(Header);
            var arr = new byte[size];

            var ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(Header, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        public byte[] GetDataBytes()
        {
            var outBytes = new byte[Data.Length];
            Array.Copy(Data, 0, outBytes, 0, Data.Length);
            return outBytes;
        }

        public byte[] GetPacketBytes()
        {
            var outBytes = new byte[Header.packetSize];
            Array.Copy(GetHeaderBytes(), 0, outBytes, 0, HEADER_SIZE);
            Array.Copy(Data, 0, outBytes, HEADER_SIZE, Data.Length);
            return outBytes;
        }

        private byte[] CreateHash()
        {
            using (MD5 md5Hash = MD5.Create())
            {
                byte[] hash = md5Hash.ComputeHash(GetPacketBytes());
                Header.md5Hash = hash;
                return hash;
            }
        }

        public void DebugPrintPacket()
        {
            Program.Log.Debug(
                string.Format("Size:0x{0:X} Opcode:0x{1:X}",
                    Header.packetSize, Header.opcode));
            Program.Log.Debug(Environment.NewLine + Utils.ByteArrayToHex(GetHeaderBytes()));
            Program.Log.Debug(Environment.NewLine + Utils.ByteArrayToHex(GetDataBytes()));
        }
    }

    public static class LoggerExtensions
    {
        public static void ColorDebug(this Logger logger, string message, ConsoleOutputColor color)
        {
            var logEvent = new LogEventInfo(LogLevel.Debug, logger.Name, message);
            logEvent.Properties["color"] = (int)color;
            logger.Log(logEvent);
        }
    }

}