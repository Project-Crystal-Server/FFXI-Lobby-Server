-- One time, for a world database used before the lobby kept characters on their content ids: give every character
-- a PlayOnline slot points at the content id of that slot (chars.accid), the way the lobby now creates them.
--
-- Both databases must be on the same MySQL server. Set the two names below (lobby.cfg: <poldb database> and the
-- world's dbName) and the world number (lobby.cfg: <world num>), then run it with the map server stopped.

SET @pol_db   = 'playonline';
SET @world_db = 'xidb';
SET @world    = 100;

SET @sql = CONCAT(
    'UPDATE ', @world_db, '.chars c ',
    'JOIN ', @pol_db, '.characters p ON p.contentClass = 1 AND p.subId = (', @world, ' << 16) | c.charid ',
    'SET c.accid = p.id & 0xFFFFFFFF, c.original_accid = 0');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Characters no slot points at were deleted: LandSandBoat keeps them with accid 0.
SET @sql = CONCAT(
    'UPDATE ', @world_db, '.chars c ',
    'SET c.original_accid = c.accid, c.accid = 0 ',
    'WHERE c.accid <> 0 AND NOT EXISTS (SELECT 1 FROM ', @pol_db, '.characters p ',
    'WHERE p.contentClass = 1 AND p.subId = (', @world, ' << 16) | c.charid)');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Sessions from before (accid of an old account) are dropped; characters log in again through the lobby.
SET @sql = CONCAT('DELETE FROM ', @world_db, '.accounts_sessions');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
