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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Crystal.FFXILobbyServer
{
    public class Utils
    {
        public class SqFileEncryption
        {
            private ulong[] SBox = new ulong[32];
            private uint BytesRead, CheckSum;

            private static byte[] EncodeTable = {
                0x88, 0x89, 0x8a, 0x8b, 0x8c, 0x8d, 0x8e, 0x8f, 0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97,
                0x98, 0x99, 0x9a, 0x9b, 0x9c, 0x9d, 0x9e, 0x9f, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
                0xa8, 0xa9, 0xaa, 0xab, 0xac, 0xad, 0xae, 0xaf, 0xb0, 0xb1, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7,
                0xb8, 0xb9, 0xba, 0xbb, 0xbc, 0xbd, 0xbe, 0xbf, 0xc0, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7,
                0xc8, 0xc9, 0xca, 0xcb, 0xcc, 0xcd, 0xce, 0xcf, 0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7,
                0xd8, 0xd9, 0xda, 0xdb, 0xdc, 0xdd, 0xde, 0xdf, 0xe0, 0xe1, 0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7,
                0xe8, 0xe9, 0xea, 0xeb, 0xec, 0xed, 0xee, 0xef, 0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7,
                0xf8, 0xf9, 0xfa, 0xfb, 0xfc, 0xfd, 0xfe, 0xff, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
                0x28, 0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37,
                0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
                0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
                0x58, 0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
                0x68, 0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77,
                0x78, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
                0x78, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87
            };

            private static byte[] DecodeTable = {
                0x78, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
                0x88, 0x89, 0x8a, 0x8b, 0x8c, 0x8d, 0x8e, 0x8f, 0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97,
                0x98, 0x99, 0x9a, 0x9b, 0x9c, 0x9d, 0x9e, 0x9f, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
                0xa8, 0xa9, 0xaa, 0xab, 0xac, 0xad, 0xae, 0xaf, 0xb0, 0xb1, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7,
                0xb8, 0xb9, 0xba, 0xbb, 0xbc, 0xbd, 0xbe, 0xbf, 0xc0, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7,
                0xc8, 0xc9, 0xca, 0xcb, 0xcc, 0xcd, 0xce, 0xcf, 0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7,
                0xd8, 0xd9, 0xda, 0xdb, 0xdc, 0xdd, 0xde, 0xdf, 0xe0, 0xe1, 0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7,
                0xe8, 0xe9, 0xea, 0xeb, 0xec, 0xed, 0xee, 0xef, 0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7,
                0xf8, 0xf9, 0xfa, 0xfb, 0xfc, 0xfd, 0xfe, 0xff, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
                0x28, 0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37,
                0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
                0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
                0x58, 0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
                0x68, 0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77
            };

            public void Init(ulong seed)
            {
                // Setup Key
                uint seedHI = (uint)(seed >> 32);
                uint seedLO = (uint)(seed & 0xFFFFFFFF);
                seedLO = Utils.RotateLeft32(seedLO, 0x08);
                seedHI = Utils.RotateRight32(seedHI, 0x10);
                seed = ((ulong)seedLO << 32) | seedHI;

                byte[] bytes = BitConverter.GetBytes(seed);
                bytes[0] += 0x45;
                for (int i = 1; i < 8; i++)
                {
                    byte tmp = (byte)(bytes[i] + bytes[i - 1] - 0x2c);
                    bytes[i] = (byte)(bytes[i - 1] << 2 ^ tmp ^ 0x45);
                }

                // Build Table
                SBox[0] = BitConverter.ToUInt64(bytes, 0);
                for (int i = 0x1; i < SBox.Length; i++)
                    SBox[i] = SBox[i - 1] * 5;

                // Zero these two
                BytesRead = CheckSum = 0;
            }

            public uint Encrypt(byte[] src, int size)
            {
                return Encrypt(src, src, size);
            }

            public uint Encrypt(byte[] src, byte[] dst, int size)
            {
                BytesRead = CheckSum = 0;

                int remainderSize = size & 0x7;
                EncryptDataInner(src, 0, dst, 0, size - remainderSize);

                // If this was not divisible by 8, encrypt the last bytes.
                if (remainderSize != 0)
                {
                    ulong remainingBytes = 0;
                    for (int i = remainderSize; i >= 0; i--)
                        remainingBytes = (remainingBytes << 8) | src[size - remainderSize + i];
                    EncryptDataInner(BitConverter.GetBytes(remainingBytes), 0, dst, size - remainderSize, 8);
                }

                // Append the size and checksum at the end if room               
                int dataEnd = remainderSize == 0 ? size : size - remainderSize + 8;
                if (dst.Length >= dataEnd + 8)
                {
                    ulong sizeAndChecksum = ((ulong)CheckSum << 32) | BytesRead;
                    EncryptDataInner(BitConverter.GetBytes(sizeAndChecksum), 0, dst, dataEnd, 8);
                }

                return BytesRead;
            }

            private void EncryptDataInner(byte[] src, int srcOffset, byte[] dst, int dstOffset, int size)
            {
                for (int i = 0; i < size / 8; i++)
                {
                    int dataOffset = i * 8;
                    CheckSum += (uint)(src[srcOffset + dataOffset] + src[srcOffset + dataOffset + 4]);
                    FirstEncrypt(src, srcOffset + dataOffset, dst, dstOffset + dataOffset);
                    SecondEncrypt(dst, dstOffset + dataOffset, dst, dstOffset + dataOffset);
                    BytesRead += 8;
                }
            }

            private void FirstEncrypt(byte[] src, int srcOffset, byte[] dst, int dstOffset)
            {
                ulong data = BitConverter.ToUInt64(src, srcOffset);
                data = (data >> 32) | (data << 32);

                ulong shiftedSize = ((ulong)BytesRead << 0xA) | BytesRead;
                shiftedSize = (shiftedSize << 0xA) | BytesRead;
                shiftedSize = (shiftedSize << 0xA) | BytesRead;
                shiftedSize += 0xA1652347;

                data ^= SBox[(BytesRead >> 3) & 0x1F];
                data ^= shiftedSize;
                data += SBox[(BytesRead >> 3) & 0x1F];

                Array.Copy(BitConverter.GetBytes(data), 0, dst, dstOffset, 8);
            }

            private void SecondEncrypt(byte[] src, int srcOffset, byte[] dst, int dstOffset)
            {
                byte xorByte1 = (byte)((BytesRead >> 3) ^ 0x45);
                for (int i = 0; i < 8; i++)
                {
                    byte xorByte2 = src[srcOffset + i];
                    for (int j = 0; j < 8; j++)
                    {
                        // Bitmath to extract the byte from the SBox since it's ulongs. BytesRead/8, then
                        // shift by j bytes (*8 bits) and cut off the rest.
                        byte sboxVal = (byte)((SBox[(BytesRead >> 3) & 0x1F] >> (j * 8)) & 0xFF);
                        xorByte2 = EncodeTable[(sboxVal + xorByte2) & 0xFF];
                    }
                    dst[dstOffset + i] = xorByte1 = (byte)(xorByte1 ^ xorByte2);
                }
            }

            public int Decrypt(byte[] src, int size)
            {
                return Decrypt(src, src, size);
            }

            public int Decrypt(byte[] src, byte[] dst, int size)
            {
                if ((size & ~7) == 0)
                    return -1;

                if ((size & ~7) != size)
                    return -1;

                BytesRead = CheckSum = 0;

                DecryptDataInner(src, 0, dst, 0, size - 8);

                uint dataChecksum = CheckSum;

                DecryptDataInner(src, size - 8, dst, size - 8, 8);

                if (dataChecksum == BitConverter.ToUInt32(dst, size - 4))
                    return BitConverter.ToInt32(dst, size - 8);

                return -1;
            }

            private void DecryptDataInner(byte[] src, int srcOffset, byte[] dst, int dstOffset, int size)
            {
                for (int i = 0; i < size / 8; i++)
                {
                    int dataOffset = i * 8;
                    SecondDecrypt(src, srcOffset + dataOffset, dst, dstOffset + dataOffset);
                    FirstDecrypt(dst, dstOffset + dataOffset, dst, dstOffset + dataOffset);
                    CheckSum += (uint)(dst[dstOffset + dataOffset] + dst[dstOffset + dataOffset + 4]);
                    BytesRead += 8;
                }
            }

            private void FirstDecrypt(byte[] src, int srcOffset, byte[] dst, int dstOffset)
            {
                ulong data = BitConverter.ToUInt64(src, srcOffset);

                ulong shiftedSize = ((ulong)BytesRead << 0xA) | BytesRead;
                shiftedSize = (shiftedSize << 0xA) | BytesRead;
                shiftedSize = (shiftedSize << 0xA) | BytesRead;
                shiftedSize += 0xA1652347;

                data -= SBox[(BytesRead >> 3) & 0x1F];
                data ^= shiftedSize;
                data ^= SBox[(BytesRead >> 3) & 0x1F];

                data = (data >> 32) | (data << 32);
                Array.Copy(BitConverter.GetBytes(data), 0, dst, dstOffset, 8);
            }

            private void SecondDecrypt(byte[] src, int srcOffset, byte[] dst, int dstOffset)
            {
                byte xorByte1 = (byte)(((BytesRead >> 3) ^ 0x45) & 0xFF);

                for (int i = 0; i < 8; i++)
                {
                    byte xorByte2 = (byte)(src[srcOffset + i] ^ xorByte1);

                    for (int j = 0; j < 8; j++)
                    {
                        // Bitmath to extract the byte from the SBox since it's ulongs. BytesRead/8, then
                        // shift by j bytes (*8 bits) and cut off the rest.
                        byte sboxVal = (byte)((SBox[(BytesRead >> 3) & 0x1F] >> (j * 8)) & 0xFF);
                        xorByte2 = (byte)(DecodeTable[xorByte2] - sboxVal);
                    }

                    byte temp = src[srcOffset + i];
                    dst[dstOffset + i] = xorByte2;
                    xorByte1 = temp;
                }
            }
        }

        public static uint RotateLeft32(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        public static uint RotateRight32(uint value, int bits)
        {
            return (value >> bits) | (value << (32 - bits));
        }


        public static bool DecryptAuthPassword(byte[] authPassword, out byte[] outAuthHash, out uint outClientIp, out ushort outClientPort)
        {
            outAuthHash = null;
            outClientIp = 0;
            outClientPort = 0;

            if (authPassword == null)
                return false;

            // Verify
            if (authPassword[1] != (authPassword[6] ^ authPassword[12]) ||
                authPassword[2] != (authPassword[8] ^ authPassword[10]) ||
                authPassword[3] != (authPassword[9] ^ authPassword[13]))
                return false;
            byte checksumByte = authPassword[14];

            // Decode
            for (int i = 51; i > 4; --i)
                authPassword[i] ^= authPassword[i - 1];

            byte[] decrypted = new byte[0x30];
            Array.Copy(authPassword, 4, decrypted, 0, 0x30);

            // Decrypt
            SqFileEncryption sqDecryptFile = new();
            sqDecryptFile.Init(0xA9E3EBAB72483CB);
            sqDecryptFile.Decrypt(decrypted, 0x30);

            // Verify 2
            if (authPassword[0] != (decrypted[1] ^ checksumByte) ||
                decrypted[0] != (decrypted[27] ^ decrypted[37]))
                return false;

            // Copy data
            byte[] authHash = new byte[0x10];
            Array.Copy(decrypted, 0x18, authHash, 0, 0x10);

            outAuthHash = authHash;
            outClientPort = BitConverter.ToUInt16(decrypted, 0x6);
            outClientIp = BitConverter.ToUInt32(decrypted, 0x8);

            return true;
        }

        public static string ByteArrayToHex(byte[] bytes, int offset = 0, int bytesPerLine = 16)
        {
            if (bytes == null)
            {
                return string.Empty;
            }

            var hexChars = "0123456789ABCDEF".ToCharArray();

            var offsetBlock = 8 + 3;
            var byteBlock = offsetBlock + bytesPerLine * 3 + (bytesPerLine - 1) / 8 + 2;
            var lineLength = byteBlock + bytesPerLine + Environment.NewLine.Length;

            var line = (new string(' ', lineLength - Environment.NewLine.Length) + Environment.NewLine).ToCharArray();
            var numLines = (bytes.Length + bytesPerLine - 1) / bytesPerLine;

            var sb = new StringBuilder(numLines * lineLength);

            for (var i = 0; i < bytes.Length; i += bytesPerLine)
            {
                var h = i + offset;

                line[0] = hexChars[(h >> 28) & 0xF];
                line[1] = hexChars[(h >> 24) & 0xF];
                line[2] = hexChars[(h >> 20) & 0xF];
                line[3] = hexChars[(h >> 16) & 0xF];
                line[4] = hexChars[(h >> 12) & 0xF];
                line[5] = hexChars[(h >> 8) & 0xF];
                line[6] = hexChars[(h >> 4) & 0xF];
                line[7] = hexChars[(h >> 0) & 0xF];

                var hexColumn = offsetBlock;
                var charColumn = byteBlock;

                for (var j = 0; j < bytesPerLine; j++)
                {
                    if (j > 0 && (j & 7) == 0)
                    {
                        hexColumn++;
                    }

                    if (i + j >= bytes.Length)
                    {
                        line[hexColumn] = ' ';
                        line[hexColumn + 1] = ' ';
                        line[charColumn] = ' ';
                    }
                    else
                    {
                        var by = bytes[i + j];
                        line[hexColumn] = hexChars[(by >> 4) & 0xF];
                        line[hexColumn + 1] = hexChars[by & 0xF];
                        line[charColumn] = by < 32 ? '.' : (char)by;
                    }

                    hexColumn += 3;
                    charColumn++;
                }

                sb.Append(line);
            }

            return sb.ToString().TrimEnd(Environment.NewLine.ToCharArray());
        }

        public static uint UnixTimeStampUTC(DateTime? time = null)
        {
            uint unixTimeStamp;
            var currentTime = time ?? DateTime.Now;
            var zuluTime = currentTime.ToUniversalTime();
            var unixEpoch = new DateTime(1970, 1, 1);
            unixTimeStamp = (uint)zuluTime.Subtract(unixEpoch).TotalSeconds;

            return unixTimeStamp;
        }

        public static ulong MilisUnixTimeStampUTC(DateTime? time = null)
        {
            ulong unixTimeStamp;
            var currentTime = time ?? DateTime.Now;
            var zuluTime = currentTime.ToUniversalTime();
            var unixEpoch = new DateTime(1970, 1, 1);
            unixTimeStamp = (ulong)zuluTime.Subtract(unixEpoch).TotalMilliseconds;

            return unixTimeStamp;
        }

        public static ulong SwapEndian(ulong input)
        {
            return 0x00000000000000FF & (input >> 56) |
                   0x000000000000FF00 & (input >> 40) |
                   0x0000000000FF0000 & (input >> 24) |
                   0x00000000FF000000 & (input >> 8) |
                   0x000000FF00000000 & (input << 8) |
                   0x0000FF0000000000 & (input << 24) |
                   0x00FF000000000000 & (input << 40) |
                   0xFF00000000000000 & (input << 56);
        }

        public static uint SwapEndian(uint input)
        {
            return ((input >> 24) & 0xff) |
                   ((input << 8) & 0xff0000) |
                   ((input >> 8) & 0xff00) |
                   ((input << 24) & 0xff000000);
        }

        public static int SwapEndian(int input)
        {
            var inputAsUint = (uint)input;

            input = (int)
                (((inputAsUint >> 24) & 0xff) |
                 ((inputAsUint << 8) & 0xff0000) |
                 ((inputAsUint >> 8) & 0xff00) |
                 ((inputAsUint << 24) & 0xff000000));

            return input;
        }

        public static ushort SwapEndian(ushort input)
        {
            return (ushort)(((input << 8) & 0xff00) |
                            ((input >> 8) & 0x00ff));
        }

    
        // Expansion bits of the lobby login answer (LandSandBoat login_helpers.h EXPANSION_DISPLAY).
        private static readonly (string Key, uint Bits)[] ExpansionSettings =
        [
            ("ENABLE_ROTZ", 0x0002),
            ("ENABLE_COP", 0x0004),
            ("ENABLE_TOAU", 0x0008),
            ("ENABLE_WOTG", 0x0010),
            ("ENABLE_ACP", 0x0020),
            ("ENABLE_AMK", 0x0040),
            ("ENABLE_ASA", 0x0080),
            ("ENABLE_ABYSSEA", 0x0100 | 0x0200 | 0x0400), // Visions, Scars, Heroes
            ("ENABLE_SOA", 0x0800),
        ];

        // The expansions a LandSandBoat / Phoenix world enables: the ENABLE_* switches of its settings/main.lua
        // (over settings/default/main.lua), as the lobby login's expansion bitmask. Null if unreadable.
        public static uint? ServerExpansions(string settingsDir)
        {
            if (string.IsNullOrEmpty(settingsDir))
                return null;

            Dictionary<string, string> values = [];
            foreach (string file in new[] { Path.Combine(settingsDir, "default", "main.lua"), Path.Combine(settingsDir, "main.lua") })
            {
                if (!File.Exists(file))
                    continue;
                foreach (string line in File.ReadAllLines(file))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(ENABLE_[A-Z]+)\s*=\s*([A-Za-z0-9]+)");
                    if (m.Success)
                        values[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
            if (values.Count == 0)
                return null;

            uint mask = 0x0001; // base game
            foreach (var (key, bits) in ExpansionSettings)
            {
                if (values.TryGetValue(key, out string v) && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    mask |= bits;
            }
            return mask;
        }
}
}
