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

        public static Character[] GetCharacters(List<WorldContainer> worldList, CharacterPrimitive[] contentIdList)
        {
            // Go through each content id. If there is a server id, grab chara data, otherwise set to blank.
            Character[] characters = new Character[contentIdList.Length];
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

                // This content id has a character, grab data. World id is high 32bits of subid.
                ushort worldNum = (ushort)((polChar.ContentsSubUserId >> 16) & 0xFFFF);
                WorldContainer world = worldList.Where(container => container.World.Num == worldNum).FirstOrDefault();
                if (world == null)
                {
                    Program.Log.Error($"Content id {polChar.ContentsId}: character {polChar.ContentsSubUserId & 0xFFFF} is on world {worldNum}, which lobby.cfg does not have");
                    continue;
                }
                using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
                try
                {
                    conn.Open();
                    MySqlCommand cmd = new(
                        @"
                    SELECT charid, charname, doRename, pos_zone, pos_prevzone, mjob,
                    race, face, head, body, hands, legs, feet, main, sub,
                    war, mnk, whm, blm, rdm, thf, pld, drk, bst, brd, rng,
                    sam, nin, drg, smn, blu, cor, pup, dnc, sch, geo, run,
                    gmlevel, nation, size, sjob
                    FROM chars
                    INNER JOIN char_stats USING(charId)
                    INNER JOIN char_look  USING(charId)
                    INNER JOIN char_jobs  USING(charId)
                    WHERE charId = @charId
                    LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("@charId", polChar.ContentsSubUserId & 0xFFFF);

                    using MySqlDataReader reader = cmd.ExecuteReader();
                    if (!reader.Read())
                        Program.Log.Error($"Content id {polChar.ContentsId}: character {polChar.ContentsSubUserId & 0xFFFF} is not in {world.World.Name}'s database");
                    else
                    {
                        CharaInfo characterInfo = new();

                        characters[indx].FFXiId = (uint) (polChar.ContentsId & 0xFFFFFFFFL); // ContentId is 64bit but FFXI truncates it to 32bit.
                        characters[indx].FFXiIdWorld = (ushort) (polChar.ContentsSubUserId & 0xFFFF); // This should match, char id + world id. If 0 it's deleted.
                        characters[indx].WorldId = (ushort) ((polChar.ContentsSubUserId >> 16) & 0xFFFF);
                        //character.FfxiIdWorldTbl = charIdExtra; //Doesn't exist in 2010
                        characters[indx].Status = 1;
                        characters[indx].Rename = (ushort) (reader.GetByte("doRename") == 0 ? 0 : 1);
                        characters[indx].Name = reader.GetString("charname").PadRight(16, '\0')[..16];
                        characters[indx].WorldName = world.World.Name;

                        ushort zone = reader.GetUInt16("pos_zone");
                        byte mainJob = reader.GetByte("mjob");
                        characterInfo.RaceNum = reader.GetUInt16("race");
                        characterInfo.MJobNum = reader.GetByte("mjob");
                        characterInfo.MJobLevel = reader.GetByte(14 + mainJob); // Index-based lookup from C++ logic
                        characterInfo.SJobNum = reader.GetByte("sjob");
                        characterInfo.FaceNum = reader.GetUInt16("face");
                        characterInfo.TownNum = reader.GetByte("nation");

                        characterInfo.ZoneNumLow = (byte)zone;
                        characterInfo.ZoneNumHigh = (byte)((zone >> 8) & 1);

                        characterInfo.HairNum = reader.GetByte("face");
                        characterInfo.Size = reader.GetByte("size");

                        characterInfo.FaceModelId = reader.GetUInt16("face");
                        characterInfo.HeadModelId = reader.GetUInt16("head");
                        characterInfo.BodyModelId = reader.GetUInt16("body");
                        characterInfo.HandsModelId = reader.GetUInt16("hands");
                        characterInfo.LegsModelId = reader.GetUInt16("legs");
                        characterInfo.FeetModelId = reader.GetUInt16("feet");
                        characterInfo.MainWeaponModelId = reader.GetUInt16("main");
                        characterInfo.SubWeaponModelId = reader.GetUInt16("sub");

                        characterInfo.GenFlag = 0;
                        characterInfo.AnonStatusFlag = 0;
                        characterInfo.WorldNum = (ushort) world.World.Num;

                        characters[indx].CharaInfo = characterInfo;
                    }
                }
                catch (MySqlException e)
                {
                    Program.Log.Error(e.ToString());
                    return null;
                }
                finally
                {
                    conn.Dispose();
                }
            }

            return characters;
        }

        // ---- LandSandBoat world database ----------------------------------------------------------------
        // Only LandSandBoat's map and search servers run behind this lobby; its accounts, login and lobby do not.
        // A character belongs to the PlayOnline content id it was made on: chars.accid and accounts_sessions.accid
        // hold that content id (the 32 bits the client knows), and the PlayOnline member that owns the content id
        // is resolved here, never in the world database. The map server needs accid non-zero (0 marks a deleted
        // character) and unique per session (accounts_sessions has a UNIQUE key on it), which a content id is.

        private const uint MAX_CHARID = 0xFFFF; // the charid is the low 16 bits of the PlayOnline sub id

        // Lobby error codes the client shows for a name (LandSandBoat login_errors.h)
        public const uint ERR_NAME_UNAVAILABLE = 313; // "The character name you entered is unavailable."
        public const uint ERR_NAME_SERVER      = 314; // "Failed to register with the name server."

        // 0 if the name can be given to a character of the world, else the error to show: letters only, 3 to 15 of
        // them, and no character of the world has it in any case (deleted ones included: LandSandBoat keeps their rows).
        public static uint CharacterNameError(WorldContainer world, string name)
        {
            if (name.Length < 3 || name.Length > 15 || !name.All(char.IsAsciiLetter))
            {
                Program.Log.Warn($"Character name <{name}> refused: not 3 to 15 letters");
                return ERR_NAME_UNAVAILABLE;
            }

            using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
            try
            {
                conn.Open();
                MySqlCommand cmd = new("SELECT COUNT(*) FROM chars WHERE LOWER(charname) = LOWER(@name)", conn);
                cmd.Parameters.AddWithValue("@name", name);
                if (Convert.ToUInt32(cmd.ExecuteScalar()) != 0)
                {
                    Program.Log.Warn($"Character name <{name}> refused: in use on {world.World.Name}");
                    return ERR_NAME_UNAVAILABLE;
                }
                return 0;
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
                return ERR_NAME_SERVER;
            }
            finally
            {
                conn.Dispose();
            }
        }

        public static uint CreateCharacter(WorldContainer world, CharaInfo charaInfo, string name, uint startZone, uint contentId)
        {
            using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
            try
            {
                conn.Open();

                // The next free charid that fits the 16 bits of the sub id; past 0xFFFF, the first gap.
                uint charId = 0;
                MySqlCommand getCharIdCmd = new("SELECT COALESCE(MAX(charid), 0) + 1 FROM chars WHERE charid <= @max", conn);
                getCharIdCmd.Parameters.AddWithValue("@max", MAX_CHARID);
                charId = Convert.ToUInt32(getCharIdCmd.ExecuteScalar());
                if (charId > MAX_CHARID)
                {
                    MySqlCommand gapCmd = new(@"
                        SELECT MIN(c.charid) + 1 FROM chars c
                        WHERE c.charid < @max AND NOT EXISTS (SELECT 1 FROM chars n WHERE n.charid = c.charid + 1)
                    ", conn);
                    gapCmd.Parameters.AddWithValue("@max", MAX_CHARID);
                    object gap = gapCmd.ExecuteScalar();
                    if (gap == null || gap == DBNull.Value)
                    {
                        Program.Log.Error($"Content id {contentId} - No free character id below 0x10000");
                        return 0;
                    }
                    charId = Convert.ToUInt32(gap);
                }

                // We have a new subid!
                uint newSubId = (world.World.Num << 16) | charId;

                // Create character
                MySqlCommand cmd = new(@"
                    INSERT INTO chars(charid,accid,charname,pos_zone,nation) VALUES(@charId, @accid, @charName, @startZone, @nation);
                    INSERT INTO char_look(charid,face,race,size) VALUES(@charId, @face, @race, @size);
                    INSERT INTO char_stats(charid,mjob) VALUES(@charId, @job);
                    INSERT INTO char_exp(charid) VALUES(@charId) ON DUPLICATE KEY UPDATE charid = charid;
                    INSERT INTO char_flags(charid) VALUES(@charId) ON DUPLICATE KEY UPDATE disconnecting = disconnecting;
                    INSERT INTO char_jobs(charid) VALUES(@charId) ON DUPLICATE KEY UPDATE charid = charid;
                    INSERT INTO char_points(charid) VALUES(@charId) ON DUPLICATE KEY UPDATE charid = charid;
                    INSERT INTO char_unlocks(charid) VALUES(@charId) ON DUPLICATE KEY UPDATE charid = charid;
                    INSERT INTO char_profile(charid) VALUES(@charId) ON DUPLICATE KEY UPDATE charid = charid;
                    INSERT INTO char_storage(charid) VALUES(@charId) ON DUPLICATE KEY UPDATE charid = charid;
                    DELETE FROM char_inventory WHERE charid = @charId;
                    INSERT INTO char_inventory(charid) VALUES(@charId);
                    INSERT INTO char_vars(charid, varname, value) VALUES(@charId, @cutsceneVar, 1);
                ", conn);

                cmd.Parameters.AddWithValue("@charId", charId);
                cmd.Parameters.AddWithValue("@accid", contentId);
                cmd.Parameters.AddWithValue("@charName", name);
                cmd.Parameters.AddWithValue("@startZone", startZone);
                cmd.Parameters.AddWithValue("@nation", charaInfo.TownNum);
                cmd.Parameters.AddWithValue("@face", charaInfo.FaceNum);
                cmd.Parameters.AddWithValue("@race", charaInfo.RaceNum);
                cmd.Parameters.AddWithValue("@size", charaInfo.Size);
                cmd.Parameters.AddWithValue("@job", charaInfo.MJobNum);
                cmd.Parameters.AddWithValue("@cutsceneVar", "HQuest[newCharacterCS]notSeen");

                cmd.ExecuteNonQuery();

                return newSubId;
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
                return 0;
            }
            finally
            {
                conn.Dispose();
            }
        }

        public static bool DeleteCharacter(WorldContainer world, uint ffxiWorldId)
        {
            using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
            try
            {
                conn.Open();
                // LandSandBoat's deleted character: the row (and every char_* row) stays, accid 0 and original_accid the
                // content id it belonged to.
                MySqlCommand cmd = new(@"
                    UPDATE chars SET original_accid = accid, accid = 0 WHERE charid = @ffxiWorldId AND accid <> 0;
                    DELETE FROM accounts_sessions WHERE charid = @ffxiWorldId;
                ", conn);
                cmd.Parameters.AddWithValue("@ffxiWorldId", ffxiWorldId);

                cmd.ExecuteNonQuery();
                return true;
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
            }
            finally
            {
                conn.Dispose();
            }
            return false;
        }

        public static bool RenameCharacter(WorldContainer world, uint ffxiWorldId, string newName)
        {
            using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
            try
            {
                conn.Open();
                string query = @"
                        UPDATE chars
                        SET charname = @newName, doRename = 0
                        WHERE charid = @ffxiWorldId AND doRename <> 0
                        ";

                MySqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@ffxiWorldId", ffxiWorldId);
                cmd.Parameters.AddWithValue("@newName", newName);
                // Only a character flagged for a rename can take a new name
                if (cmd.ExecuteNonQuery() == 0)
                {
                    Program.Log.Warn($"Character {ffxiWorldId} is not flagged for a rename");
                    return false;
                }
                return true;
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
            }
            finally
            {
                conn.Dispose();
            }
            return false;
        }

        // Whether the character is in the world: a session row, once a zone-out the other map server never saw
        // (client_port 0 for over 2 minutes) has been cleared away.
        public static bool IsCharacterOnline(WorldContainer world, uint ffxiWorldId)
        {
            using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
            try
            {
                conn.Open();
                MySqlCommand cmd = new(@"
                    DELETE FROM accounts_sessions
                    WHERE charid = @charId AND client_port = 0 AND last_zoneout_time <= SUBTIME(NOW(), '00:02:00');
                    SELECT COUNT(*) FROM accounts_sessions WHERE charid = @charId;
                ", conn);
                cmd.Parameters.AddWithValue("@charId", ffxiWorldId);
                return Convert.ToUInt32(cmd.ExecuteScalar()) != 0;
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
                return true; // unknown: keep the character out
            }
            finally
            {
                conn.Dispose();
            }
        }

        public static bool AddSession(WorldContainer world, uint contentId, uint ffxiWorldId, byte[] key, uint serverAddress, uint serverPort, uint clientAddress, string clientVersion, uint clientExpansions, string lobbyToken)
        {
            using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
            try
            {
                conn.Open();

                MySqlCommand owned = new("SELECT COUNT(*) FROM chars WHERE charid = @charId AND accid = @contentId", conn);
                owned.Parameters.AddWithValue("@charId", ffxiWorldId);
                owned.Parameters.AddWithValue("@contentId", contentId);
                if (Convert.ToUInt32(owned.ExecuteScalar()) == 0)
                {
                    Program.Log.Error($"Content id {contentId} - Character {ffxiWorldId} is not on this content id (deleted?)");
                    return false;
                }

                MySqlCommand cmd = new(@"
                    INSERT INTO accounts_sessions(accid, charid, session_key, server_addr, server_port, client_addr, version_mismatch, client_version, client_expansions, lobby_token)
                    VALUES(@accid, @charid, @session_key, @server_addr, @server_port, @client_addr, @version_mismatch, @client_version, @client_expansions, @lobby_token)
                ", conn);
                cmd.Parameters.AddWithValue("@accid", contentId);
                cmd.Parameters.AddWithValue("@session_key", key);
                cmd.Parameters.AddWithValue("@charid", ffxiWorldId);
                cmd.Parameters.AddWithValue("@server_addr", serverAddress);
                cmd.Parameters.AddWithValue("@server_port", serverPort);
                cmd.Parameters.AddWithValue("@client_addr", clientAddress);
                cmd.Parameters.AddWithValue("@version_mismatch", false);
                // The client build, for a map server that serves several (the version string of the lobby login and
                // the expansions the client has installed)
                cmd.Parameters.AddWithValue("@client_version", clientVersion);
                cmd.Parameters.AddWithValue("@client_expansions", clientExpansions);
                // What the map server shows the account service to ask about this session's account (AccountService)
                cmd.Parameters.AddWithValue("@lobby_token", lobbyToken);

                cmd.ExecuteNonQuery();
                return true;
            }
            catch (MySqlException e)
            {
                Program.Log.Error(e.ToString());
            }
            finally
            {
                conn.Dispose();
            }
            return false;
        }

        // ---- Account service ------------------------------------------------------------------------------------

        // The content id and character of the world's live session with this token, if there is one
        public static (uint contentId, uint charId)? GetSessionByToken(WorldContainer world, string token)
        {
            using MySqlConnection conn = new($"Server={world.DbHost}; Port={world.DbPort}; Database={world.DbName}; UID={world.DbUser}; Password={world.DbPass}");
            try
            {
                conn.Open();
                MySqlCommand cmd = new("SELECT accid, charid FROM accounts_sessions WHERE lobby_token = @token LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@token", token);
                using MySqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                    return (reader.GetUInt32("accid"), reader.GetUInt32("charid"));
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
