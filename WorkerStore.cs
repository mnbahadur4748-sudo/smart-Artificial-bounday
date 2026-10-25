using Microsoft.Data.Sqlite;
using AIBoundary.Core.Security;

namespace AIBoundary.Core.Workers;

public sealed record WorkerInput(string Code, string Name, string Section, string Department, string IdNumber);

public sealed record Worker(long Id, string Code, string Name, string Section, string Department,
                            string IdNumber, byte[]? Thumb);

public enum WorkerResult { Ok, Duplicate, Error }

/// <summary>শ্রমিকের তথ্য ও ৫ দিকের ছবি। নাম, আইডি, সেকশন, ডিপার্টমেন্ট ও ছবি — সবই এনক্রিপ্ট করে রাখা হয়।</summary>
public static class WorkerStore
{
    public static readonly string[] Angles = { "front", "left", "right", "up", "down" };

    public static event Action? Changed;

    public static List<Worker> List(long userId)
    {
        var list = new List<Worker>();
        try
        {
            using var c = Db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT id,code_enc,name_enc,section_enc,dept_enc,idnum_enc,thumb_enc
                                FROM workers WHERE user_id=$u ORDER BY id DESC";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                byte[]? thumb = null;
                if (!r.IsDBNull(6))
                {
                    try { thumb = CryptoService.DecryptBytes(r.GetFieldValue<byte[]>(6)); } catch { }
                }
                list.Add(new Worker(r.GetInt64(0), Dec(r.GetString(1)), Dec(r.GetString(2)), Dec(r.GetString(3)),
                                    Dec(r.GetString(4)), Dec(r.GetString(5)), thumb));
            }
        }
        catch (Exception ex) { Logger.Error("WorkerList", ex); }
        return list;
    }

    public static Dictionary<string, byte[]> GetPhotos(long userId, long workerId)
    {
        var d = new Dictionary<string, byte[]>();
        try
        {
            using var c = Db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT p.angle, p.data_enc FROM worker_photos p
                                JOIN workers w ON w.id = p.worker_id
                                WHERE w.id=$w AND w.user_id=$u";
            cmd.Parameters.AddWithValue("$w", workerId);
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                try { d[r.GetString(0)] = CryptoService.DecryptBytes(r.GetFieldValue<byte[]>(1)); } catch { }
            }
        }
        catch (Exception ex) { Logger.Error("WorkerPhotos", ex); }
        return d;
    }

    public static WorkerResult Add(long userId, WorkerInput w, Dictionary<string, byte[]> photos, byte[]? thumb)
    {
        try
        {
            using var c = Db.Open();
            var codeHash = CodeHash(userId, w.Code);

            if (Count(c, "SELECT COUNT(1) FROM workers WHERE user_id=$u AND code_hash=$h",
                      ("$u", userId), ("$h", codeHash)) > 0)
                return WorkerResult.Duplicate;

            using var tx = c.BeginTransaction();
            long id;
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO workers(user_id,code_hash,code_enc,name_enc,section_enc,dept_enc,idnum_enc,thumb_enc,created_at)
                                    VALUES($u,$h,$c,$n,$s,$d,$i,$t,$ts); SELECT last_insert_rowid();";
                Fill(cmd, userId, codeHash, w, thumb);
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
                id = Convert.ToInt64(cmd.ExecuteScalar());
            }
            InsertPhotos(c, tx, id, photos);
            tx.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return WorkerResult.Duplicate; }
        catch (Exception ex) { Logger.Error("WorkerAdd", ex); return WorkerResult.Error; }

        Changed?.Invoke();
        return WorkerResult.Ok;
    }

    public static WorkerResult Update(long userId, long id, WorkerInput w, Dictionary<string, byte[]> photos, byte[]? thumb)
    {
        try
        {
            using var c = Db.Open();
            var codeHash = CodeHash(userId, w.Code);

            if (Count(c, "SELECT COUNT(1) FROM workers WHERE user_id=$u AND code_hash=$h AND id<>$id",
                      ("$u", userId), ("$h", codeHash), ("$id", id)) > 0)
                return WorkerResult.Duplicate;

            using var tx = c.BeginTransaction();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"UPDATE workers SET code_hash=$h, code_enc=$c, name_enc=$n, section_enc=$s,
                                    dept_enc=$d, idnum_enc=$i, thumb_enc=$t WHERE id=$id AND user_id=$u";
                Fill(cmd, userId, codeHash, w, thumb);
                cmd.Parameters.AddWithValue("$id", id);
                if (cmd.ExecuteNonQuery() == 0) return WorkerResult.Error;
            }
            using (var del = c.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM worker_photos WHERE worker_id=$w";
                del.Parameters.AddWithValue("$w", id);
                del.ExecuteNonQuery();
            }
            InsertPhotos(c, tx, id, photos);
            tx.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return WorkerResult.Duplicate; }
        catch (Exception ex) { Logger.Error("WorkerUpdate", ex); return WorkerResult.Error; }

        Changed?.Invoke();
        return WorkerResult.Ok;
    }

    public static bool Delete(long userId, long id)
    {
        try
        {
            using var c = Db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM workers WHERE id=$id AND user_id=$u";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$u", userId);
            if (cmd.ExecuteNonQuery() == 0) return false;
        }
        catch (Exception ex) { Logger.Error("WorkerDelete", ex); return false; }

        Changed?.Invoke();
        return true;
    }

    // ---------------- ভেতরের সাহায্যকারী ----------------

    private static string CodeHash(long userId, string code) =>
        CryptoService.Hmac("worker|" + userId + "|" + code.Trim().ToLowerInvariant());

    private static string Dec(string enc)
    {
        try { return CryptoService.Decrypt(enc); } catch { return ""; }
    }

    private static void Fill(SqliteCommand cmd, long userId, string codeHash, WorkerInput w, byte[]? thumb)
    {
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$h", codeHash);
        cmd.Parameters.AddWithValue("$c", CryptoService.Encrypt(w.Code.Trim()));
        cmd.Parameters.AddWithValue("$n", CryptoService.Encrypt(w.Name.Trim()));
        cmd.Parameters.AddWithValue("$s", CryptoService.Encrypt(w.Section.Trim()));
        cmd.Parameters.AddWithValue("$d", CryptoService.Encrypt(w.Department.Trim()));
        cmd.Parameters.AddWithValue("$i", CryptoService.Encrypt(w.IdNumber.Trim()));
        cmd.Parameters.AddWithValue("$t", thumb == null ? DBNull.Value : CryptoService.EncryptBytes(thumb));
    }

    private static void InsertPhotos(SqliteConnection c, SqliteTransaction tx, long workerId, Dictionary<string, byte[]> photos)
    {
        foreach (var kv in photos)
        {
            using var pc = c.CreateCommand();
            pc.Transaction = tx;
            pc.CommandText = "INSERT INTO worker_photos(worker_id,angle,data_enc) VALUES($w,$a,$d)";
            pc.Parameters.AddWithValue("$w", workerId);
            pc.Parameters.AddWithValue("$a", kv.Key);
            pc.Parameters.AddWithValue("$d", CryptoService.EncryptBytes(kv.Value));
            pc.ExecuteNonQuery();
        }
    }

    private static int Count(SqliteConnection c, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
