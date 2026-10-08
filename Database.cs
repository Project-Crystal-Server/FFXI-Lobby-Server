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
using Crystal.POLProfile.DataObjects.Pol.Character;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Crystal.FFXILobbyServer
{
    class Database
    {
        // The lobby's own database: PlayOnline members and the content ids they hold. A world's characters are
        // reached through the world's API (WorldApi), never its database.
        public static string POL_DB_HOST = "127.0.0.1";
        public static string POL_DB_PORT = "3306";
        public static string POL_DB_NAME = "playonline";
        public static string POL_DB_USERNAME = "root";
        public static string POL_DB_PASSWORD = "";

        public static Tuple<byte[], string> GetPlayonlineRandomValue(byte[] authHash)
        {
            using MySqlConnection conn = new($"Server={POL_DB_HOST}; Port={POL_DB_PORT}; Database={POL_DB_NAME}; UID={POL_DB_USERNAME}; Password={POL_DB_PASSWORD}");
            try
            {
                conn.Open();
                MySqlCommand cmd = new("SELECT polRandomValueBinary, polId FROM sessions WHERE polContentAuthHash = @authHash", conn);
                cmd.Parameters.AddWithValue("@authHash", authHash);

                using MySqlDataReader Reader = cmd.ExecuteReader();
                while (Reader.Read())
                {
                    byte[] randomValue = new byte[0x10];
                    long bytesRead = Reader.GetBytes("polRandomValueBinary", 0, randomValue, 0, 0x10);
                    if (bytesRead == 0x10)
                    {
                        string polProData = Reader.GetString("polId");
                        return new(randomValue, polProData);
                    }
                }
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
            }
            finally
            {
                conn.Dispose();
            }
            return null;
        }

        public static CharacterPrimitive[] GetFFXIContentIds(string polProData)
        {
            List<CharacterPrimitive> charaPrims = new();

            using MySqlConnection conn = new($"Server={POL_DB_HOST}; Port={POL_DB_PORT}; Database={POL_DB_NAME}; UID={POL_DB_USERNAME}; Password={POL_DB_PASSWORD}");
            {
                try
                {
                    conn.Open();
                    string query = @"
                        SELECT id, subId FROM characters 
                        WHERE 
                            polId = @polPro AND 
                            contentClass = 1
                        ORDER BY linkPosition
                        ";

                    MySqlCommand cmd = new(query, conn);
                    cmd.Parameters.AddWithValue("@polPro", polProData);
                    using MySqlDataReader reader = cmd.ExecuteReader();
                    byte i = 0;
                    while (reader.Read())
                    {
                        ulong cId = reader.GetUInt64("id");
                        uint cSubId = reader.GetUInt32("subId");

                        CharacterPrimitive characterPrimitive = new()
                        {
                            IsValid = 1,
                            AttachOrder = i++,
                            ContentsClass = 1,
                            ContentsSubUserId = cSubId,
                            ContentsId = cId
                        };

                        charaPrims.Add(characterPrimitive);
                    }
                    return [.. charaPrims];
                }
                catch (MySqlException e)
                {
                    Program.Log.Error(e.ToString());
                }
                finally
                {
                    conn.Dispose();
                }
            }
            return [];
        }

        public static bool UpdateFFXISubContentId(ulong contentId, uint subContentId, string name)
        {
            using MySqlConnection conn = new($"Server={POL_DB_HOST}; Port={POL_DB_PORT}; Database={POL_DB_NAME}; UID={POL_DB_USERNAME}; Password={POL_DB_PASSWORD}");
            {
                try
                {
                    conn.Open();
                    string query = @"
                        UPDATE characters
                        SET subId = @subContentId, name = @name
                        WHERE id = @contentId
                        ";

                    MySqlCommand cmd = new(query, conn);
                    cmd.Parameters.AddWithValue("@contentId", contentId);
                    cmd.Parameters.AddWithValue("@subContentId", subContentId);
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.ExecuteNonQuery();
                }
                catch (MySqlException e)
                {
                    Program.Log.Error(e.ToString());
                    return false;
                }
                finally
                {
                    conn.Dispose();
                }
            }
            return true;
        }

        // The characters of the content ids, as their worlds list them (World.ListCharacters). The lobby's own
        // records say which world and character a content id holds (the sub id); the world says what that is.
        // Null when a world that holds one of them cannot answer.
        public static Character[] GetCharacters(List<WorldContainer> worldList, CharacterPrimitive[] contentIdList)
        {
            // Go through each content id. If there is a server id, grab chara data, otherwise set to blank.
            Character[] characters = new Character[contentIdList.Length];
            Dictionary<WorldContainer, List<int>> asked = [];
            for (int indx = 0; indx < contentIdList.Length; indx++)
            {
                CharacterPrimitive polChar = contentIdList[indx];

                // Empty until a character is found: one per content id, in order, whatever happens to the others
                characters[indx].FFXiId = (uint)(polChar.ContentsId & 0xFFFFFFFFL);
                characters[indx].FFXiIdWorld = 0;
                characters[indx].WorldId = 0;
                characters[indx].Status = 1;
                characters[indx].Name = " ";

                // This content id does not have a character
                if (polChar.ContentsSubUserId == 0)
                    continue;

                // This content id has a character. World id is high 16 bits of subid.
                ushort worldNum = (ushort)((polChar.ContentsSubUserId >> 16) & 0xFFFF);
                WorldContainer world = worldList.Where(container => container.World.Num == worldNum).FirstOrDefault();
                if (world == null)
                {
                    Program.Log.Error($"Content id {polChar.ContentsId}: character {polChar.ContentsSubUserId & 0xFFFF} is on world {worldNum}, which lobby.cfg does not have");
                    continue;
                }
                if (!asked.TryGetValue(world, out List<int> indexes))
                    asked[world] = indexes = [];
                indexes.Add(indx);
            }

            foreach (var (world, indexes) in asked)
            {
                List<WorldCharacter> found = world.Api.ListCharacters(
                    [.. indexes.Select(i => ((uint)(contentIdList[i].ContentsId & 0xFFFFFFFFL), contentIdList[i].ContentsSubUserId & 0xFFFF))]);
                if (found == null)
                    return null;

                for (int n = 0; n < indexes.Count; n++)
                {
                    int indx = indexes[n];
                    CharacterPrimitive polChar = contentIdList[indx];
                    WorldCharacter c = found[n];
                    if (c == null)
                    {
                        Program.Log.Error($"Content id {polChar.ContentsId}: character {polChar.ContentsSubUserId & 0xFFFF} is not in {world.World.Name}'s characters");
                        continue;
                    }

                    CharaInfo characterInfo = new();

                    characters[indx].FFXiId = (uint) (polChar.ContentsId & 0xFFFFFFFFL); // ContentId is 64bit but FFXI truncates it to 32bit.
                    characters[indx].FFXiIdWorld = (ushort) (polChar.ContentsSubUserId & 0xFFFF); // This should match, char id + world id. If 0 it's deleted.
                    characters[indx].WorldId = (ushort) ((polChar.ContentsSubUserId >> 16) & 0xFFFF);
                    //character.FfxiIdWorldTbl = charIdExtra; //Doesn't exist in 2010
                    characters[indx].Status = 1;
                    characters[indx].Rename = (ushort) (c.Rename ? 1 : 0);
                    characters[indx].Name = c.Name.PadRight(16, ' ')[..16];
                    characters[indx].WorldName = world.World.Name;

                    characterInfo.RaceNum = c.Race;
                    characterInfo.MJobNum = c.MainJob;
                    characterInfo.MJobLevel = c.MainJobLevel;
                    characterInfo.SJobNum = c.SubJob;
                    characterInfo.FaceNum = c.Face;
                    characterInfo.TownNum = c.Nation;

                    characterInfo.ZoneNumLow = (byte)c.Zone;
                    characterInfo.ZoneNumHigh = (byte)((c.Zone >> 8) & 1);

                    characterInfo.HairNum = (byte)c.Face;
                    characterInfo.Size = c.Size;

                    characterInfo.FaceModelId = c.Face;
                    characterInfo.HeadModelId = c.Head;
                    characterInfo.BodyModelId = c.Body;
                    characterInfo.HandsModelId = c.Hands;
                    characterInfo.LegsModelId = c.Legs;
                    characterInfo.FeetModelId = c.Feet;
                    characterInfo.MainWeaponModelId = c.Main;
                    characterInfo.SubWeaponModelId = c.Sub;

                    characterInfo.GenFlag = 0;
                    characterInfo.AnonStatusFlag = 0;
                    characterInfo.WorldNum = (ushort) world.World.Num;

                    characters[indx].CharaInfo = characterInfo;
                }
            }

            return characters;
        }

        // ---- Account service ------------------------------------------------------------------------------------

        // The PlayOnline member a content id (the 32 bits the client knows) belongs to, and the sub id of its slot
        public static (string polId, uint subId)? GetContentIdOwner(uint contentId)
        {
            using MySqlConnection conn = new($"Server={POL_DB_HOST}; Port={POL_DB_PORT}; Database={POL_DB_NAME}; UID={POL_DB_USERNAME}; Password={POL_DB_PASSWORD}");
            try
            {
                conn.Open();
                MySqlCommand cmd = new("SELECT polId, subId FROM characters WHERE (id & 0xFFFFFFFF) = @contentId AND contentClass = 1 LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@contentId", contentId);
                using MySqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                    return (reader.GetString("polId"), reader.GetUInt32("subId"));
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
            }
            finally
            {
                conn.Dispose();
            }
            return null;
        }

        // Every FFXI content id of a PlayOnline member, as the client knows them
        public static List<uint> GetMemberContentIds(string polId)
        {
            List<uint> contentIds = [];
            using MySqlConnection conn = new($"Server={POL_DB_HOST}; Port={POL_DB_PORT}; Database={POL_DB_NAME}; UID={POL_DB_USERNAME}; Password={POL_DB_PASSWORD}");
            try
            {
                conn.Open();
                MySqlCommand cmd = new("SELECT id FROM characters WHERE polId = @polId AND contentClass = 1 ORDER BY id", conn);
                cmd.Parameters.AddWithValue("@polId", polId);
                using MySqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                    contentIds.Add((uint)(reader.GetUInt64("id") & 0xFFFFFFFF));
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
                contentIds.Clear();
            }
            finally
            {
                conn.Dispose();
            }
            return contentIds;
        }
    }
}
