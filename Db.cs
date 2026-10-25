using Microsoft.Data.Sqlite;

namespace AIBoundary.Core.Security;

public static class Db
{
    private static readonly object _initLock = new();
    private static bool _ready;

    private static string DbFile => System.IO.Path.Combine(AppPaths.DataDir, "app.db");

    public static SqliteConnection Open()
    {
        Init();
        var c = new SqliteConnection($"Data Source={DbFile}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private static void Init()
    {
        lock (_initLock)
        {
            if (_ready) return;
            using var c = new SqliteConnection($"Data Source={DbFile}");
            c.Open();

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS users(
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    username        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    email_enc       TEXT NOT NULL,
    email_hash      TEXT NOT NULL UNIQUE,
    pw_hash         TEXT NOT NULL,
    country_enc     TEXT NOT NULL,
    language        TEXT NOT NULL DEFAULT 'bn',
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    locked_until    TEXT NULL,
    created_at      TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS otp_codes(
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id    INTEGER NOT NULL,
    code_hmac  TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    attempts   INTEGER NOT NULL DEFAULT 0,
    used       INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS cameras(
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id     INTEGER NOT NULL,
    type        TEXT NOT NULL,
    name        TEXT NOT NULL,
    label       TEXT NOT NULL,
    usb_index   INTEGER NULL,
    source_enc  TEXT NULL,
    source_hash TEXT NOT NULL,
    enabled     INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
    UNIQUE(user_id, source_hash)
);
CREATE TABLE IF NOT EXISTS boundaries(
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id      INTEGER NOT NULL,
    camera_id    INTEGER NOT NULL,
    name         TEXT NOT NULL,
    kind         TEXT NOT NULL,
    points_json  TEXT NOT NULL,
    objects_json TEXT NOT NULL DEFAULT '[""person""]',
    created_at   TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY(camera_id) REFERENCES cameras(id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS telegram_bots(
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id      INTEGER NOT NULL,
    serial       INTEGER NOT NULL,
    name         TEXT NOT NULL,
    token_enc    TEXT NOT NULL,
    chat_id      TEXT NOT NULL,
    bot_username TEXT NULL,
    token_hash   TEXT NOT NULL,
    created_at   TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
    UNIQUE(user_id, serial),
    UNIQUE(user_id, token_hash)
);
CREATE TABLE IF NOT EXISTS workers(
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id     INTEGER NOT NULL,
    code_hash   TEXT NOT NULL,
    code_enc    TEXT NOT NULL,
    name_enc    TEXT NOT NULL,
    section_enc TEXT NOT NULL,
    dept_enc    TEXT NOT NULL,
    idnum_enc   TEXT NOT NULL,
    thumb_enc   BLOB NULL,
    created_at  TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
    UNIQUE(user_id, code_hash)
);
CREATE TABLE IF NOT EXISTS worker_photos(
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    worker_id INTEGER NOT NULL,
    angle     TEXT NOT NULL,
    data_enc  BLOB NOT NULL,
    FOREIGN KEY(worker_id) REFERENCES workers(id) ON DELETE CASCADE,
    UNIQUE(worker_id, angle)
);";
                cmd.ExecuteNonQuery();
            }

            // আগের ডেটাবেসে cameras টেবিলে zone কলাম না থাকলে যোগ করা হয় (ডেটা মোছে না)
            bool hasZone = false;
            using (var info = c.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(cameras)";
                using var r = info.ExecuteReader();
                while (r.Read())
                    if (string.Equals(r.GetString(1), "zone", StringComparison.OrdinalIgnoreCase)) hasZone = true;
            }
            if (!hasZone)
            {
                using var alter = c.CreateCommand();
                alter.CommandText = "ALTER TABLE cameras ADD COLUMN zone TEXT NULL";
                alter.ExecuteNonQuery();
            }

            _ready = true;
        }
    }
}
