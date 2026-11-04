-- One time, for a world database used before worlds kept the POL ID on each character (world migrations 102 and
-- 103): give every character the numeric POL ID of the member whose content id it is on, and move the phoenix
-- module's account_vars from the lobby's old account number (one of the member's content ids) onto the POL ID.
--
-- Characters left out here still get their POL ID the next time they enter the world, but until then account-only
-- deliveries to them and !jailaccount do not find them, and their old account_vars rows would be lost.
--
-- Both databases must be on the same MySQL server. Set the two names below (lobby.cfg: <poldb database> and the
-- world's dbName), back both up, and run it with the map server stopped, after dbtool has run migrations 102 and 103.

SET @pol_db   = 'playonline';
SET @world_db = 'xidb';

-- POL ID on every character, live or deleted (a deleted one keeps its content id in original_accid)
SET @sql = CONCAT(
    'UPDATE ', @world_db, '.chars c ',
    'JOIN ', @pol_db, '.characters p ON p.contentClass = 1 ',
    '  AND (p.id & 0xFFFFFFFF) = IF(c.accid <> 0, c.accid, c.original_accid) ',
    'SET c.pol_id = ', @pol_db, '.PolProDataToPolId(p.polId, 0, 0) ',
    'WHERE c.pol_id = 0');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Report: characters no content id could be found for. They get a POL ID at their next enter.
SET @sql = CONCAT(
    'SELECT charid, charname, accid, original_accid FROM ', @world_db, '.chars WHERE pol_id = 0');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- account_vars: rows keyed by a content id (32 bits) move to that member's POL ID. Where one member ends up with two
-- rows for a variable, the larger value and expiry win: a later Dynamis lockout and a fuller fishing meter are the
-- strict side. Rows already keyed by a POL ID (above 32 bits) stay as they are.
SET @sql = CONCAT('CREATE TABLE IF NOT EXISTS ', @world_db, '.account_vars_pol LIKE ', @world_db, '.account_vars');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @sql = CONCAT('DELETE FROM ', @world_db, '.account_vars_pol');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @sql = CONCAT(
    'INSERT INTO ', @world_db, '.account_vars_pol (accountid, varname, value, expiry) ',
    'SELECT k.accountid, k.varname, MAX(k.value), MAX(k.expiry) FROM ( ',
    '  SELECT ', @pol_db, '.PolProDataToPolId(p.polId, 0, 0) AS accountid, v.varname, v.value, v.expiry ',
    '  FROM ', @world_db, '.account_vars v ',
    '  JOIN ', @pol_db, '.characters p ON p.contentClass = 1 AND (p.id & 0xFFFFFFFF) = v.accountid ',
    '  WHERE v.accountid <= 0xFFFFFFFF ',
    '  UNION ALL ',
    '  SELECT accountid, varname, value, expiry FROM ', @world_db, '.account_vars WHERE accountid > 0xFFFFFFFF ',
    ') k WHERE k.accountid <> 0 GROUP BY k.accountid, k.varname');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Report: rows that matched no content id. They are left behind in account_vars_pre_pol.
SET @sql = CONCAT(
    'SELECT v.* FROM ', @world_db, '.account_vars v ',
    'LEFT JOIN ', @pol_db, '.characters p ON p.contentClass = 1 AND (p.id & 0xFFFFFFFF) = v.accountid ',
    'WHERE v.accountid <= 0xFFFFFFFF AND p.id IS NULL');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Swap the tables. account_vars_pre_pol stays for you to drop once the new keys look right; on a second run, drop it
-- first or this rename stops with "table exists".
SET @sql = CONCAT(
    'RENAME TABLE ', @world_db, '.account_vars TO ', @world_db, '.account_vars_pre_pol, ',
    @world_db, '.account_vars_pol TO ', @world_db, '.account_vars');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
