using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record TextAnnRow(
    int Id,
    int PcId,
    string FilePath,
    string Guid,
    int PageIdx,
    string Text,
    int CreatedBy,
    string CreatedByName,
    string CreatedAt,
    int? ModifiedBy,
    string? ModifiedByName,
    string? ModifiedAt,
    bool IsDeleted,
    int? DeletedBy,
    string? DeletedByName,
    string? DeletedAt);

/// <summary>
/// Persistent audit log of annotations added to PDF files (text + draw strokes).
/// One row per annotation guid; edits update the row, deletes soft-flag it.
/// The .ann.json sidecar is the runtime source of truth for rendering annotations in the viewer;
/// this DB log is an independent who/when/what audit trail and the authoritative ownership record
/// used to enforce "only the original author may modify".
///
/// AnnType column distinguishes 'text' from 'draw' (and any future types). For draws the Text
/// column is empty and ModifiedBy/ModifiedAt stay NULL — draws are immutable post-create
/// (only created or soft-deleted via undo).
/// </summary>
public class TextAnnotationService
{
    private readonly string _connectionString;

    public TextAnnotationService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>INSERT a new annotation row, or UPDATE if the guid already exists.
    /// For text: Text + PageIdx are updatable, ModifiedBy/ModifiedAt are stamped on every edit.
    /// For draw: row is immutable post-create (no edit semantics) — Upsert called only by the
    /// audit POST endpoint when a new stroke is created. Resurrects soft-deleted rows on either type.</summary>
    public void Upsert(int pcId, string filePath, string guid, string annType, int pageIdx, string text, int userId)
    {
        if (string.IsNullOrEmpty(guid)) return;
        if (string.IsNullOrEmpty(annType)) annType = "text";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Check if row exists
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT Id, IsDeleted FROM sys_text_annotations WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g LIMIT 1";
            sel.Parameters.AddWithValue("@pc", pcId);
            sel.Parameters.AddWithValue("@fp", filePath);
            sel.Parameters.AddWithValue("@g",  guid);
            using var r = sel.ExecuteReader();
            if (r.Read())
            {
                bool wasDeleted = r.GetInt32(1) == 1;
                r.Close();
                // UPDATE existing — only text rows record an edit lifecycle. Draw rows that resurrect
                // (rare — undo then re-do same guid) just clear the deleted flag without touching text columns.
                using var upd = conn.CreateCommand();
                if (annType == "draw")
                {
                    upd.CommandText = wasDeleted
                        ? @"UPDATE sys_text_annotations
                            SET PageIdx=@pi, IsDeleted=0, DeletedBy=NULL, DeletedAt=NULL
                            WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g"
                        : @"UPDATE sys_text_annotations
                            SET PageIdx=@pi
                            WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g";
                }
                else
                {
                    upd.CommandText = wasDeleted
                        ? @"UPDATE sys_text_annotations
                            SET Text=@t, PageIdx=@pi,
                                ModifiedBy=@u, ModifiedAt=datetime('now'),
                                IsDeleted=0, DeletedBy=NULL, DeletedAt=NULL
                            WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g"
                        : @"UPDATE sys_text_annotations
                            SET Text=@t, PageIdx=@pi,
                                ModifiedBy=@u, ModifiedAt=datetime('now')
                            WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g";
                    upd.Parameters.AddWithValue("@t", text ?? "");
                    upd.Parameters.AddWithValue("@u", userId);
                }
                upd.Parameters.AddWithValue("@pi", pageIdx);
                upd.Parameters.AddWithValue("@pc", pcId);
                upd.Parameters.AddWithValue("@fp", filePath);
                upd.Parameters.AddWithValue("@g",  guid);
                upd.ExecuteNonQuery();
                return;
            }
        }

        // INSERT new
        using var ins = conn.CreateCommand();
        ins.CommandText = @"
            INSERT INTO sys_text_annotations
                (PcId, FilePath, AnnotationGuid, AnnType, PageIdx, Text, CreatedBy, CreatedAt)
            VALUES
                (@pc, @fp, @g, @at, @pi, @t, @u, datetime('now'))";
        ins.Parameters.AddWithValue("@pc", pcId);
        ins.Parameters.AddWithValue("@fp", filePath);
        ins.Parameters.AddWithValue("@g",  guid);
        ins.Parameters.AddWithValue("@at", annType);
        ins.Parameters.AddWithValue("@pi", pageIdx);
        ins.Parameters.AddWithValue("@t",  annType == "draw" ? "" : (text ?? ""));
        ins.Parameters.AddWithValue("@u",  userId);
        ins.ExecuteNonQuery();
    }

    public void SoftDelete(int pcId, string filePath, string guid, int userId)
    {
        if (string.IsNullOrEmpty(guid)) return;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sys_text_annotations
            SET IsDeleted=1, DeletedBy=@u, DeletedAt=datetime('now')
            WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g AND IsDeleted=0";
        cmd.Parameters.AddWithValue("@u",  userId);
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        cmd.Parameters.AddWithValue("@g",  guid);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Update FilePath for all rows of this PC; called from FolderService when a file is renamed/moved.</summary>
    public void RenamePath(int pcId, string oldPath, string newPath)
    {
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath) || oldPath == newPath) return;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sys_text_annotations SET FilePath=@new WHERE PcId=@pc AND FilePath=@old";
        cmd.Parameters.AddWithValue("@new", newPath);
        cmd.Parameters.AddWithValue("@pc",  pcId);
        cmd.Parameters.AddWithValue("@old", oldPath);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Map of guid → (CreatedByUserId, DisplayName, IsDeleted) for ALL annotation rows on this file
    /// (text + draw, active + soft-deleted). Used by sidecar GET to stamp owner info, and by sidecar POST to
    /// enforce ownership.
    ///
    /// Why include deleted rows: when an owner undoes their own annotation, the soft-delete to the audit log
    /// races with the sidecar save. If we excluded deleted rows, a sidecar save that arrives AFTER soft-delete
    /// would see "no row" and treat the guid as legacy/locked — preventing the owner from removing their own
    /// entry from the sidecar. By keeping the row visible we let the POST merge logic distinguish:
    ///   - row missing entirely → legacy/locked (per "nobody edits unowned" rule)
    ///   - row present + IsDeleted=1 → tombstoned, anyone may garbage-collect from sidecar
    ///   - row present + IsDeleted=0 + owned by other → protected
    ///   - row present + IsDeleted=0 + owned by caller → caller may modify/omit
    ///
    /// Type-agnostic on purpose: guids are unique across types, and the protection logic uses the sidecar
    /// entry's own type to decide whether to apply the check (text/draw yes, bg-change no).</summary>
    public Dictionary<string, (int UserId, string DisplayName, bool IsDeleted)> GetAnnotationOwners(int pcId, string filePath)
    {
        var map = new Dictionary<string, (int, string, bool)>(StringComparer.Ordinal);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.AnnotationGuid, t.CreatedBy,
                   COALESCE(NULLIF(TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')), ''),
                            u.Username, '?') AS DisplayName,
                   t.IsDeleted
            FROM sys_text_annotations t
            LEFT JOIN core_users   u ON u.Id = t.CreatedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE t.PcId=@pc AND t.FilePath=@fp";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var g = r.GetString(0);
            if (string.IsNullOrEmpty(g)) continue;
            map[g] = (r.GetInt32(1), r.GetString(2), r.GetInt32(3) == 1);
        }
        return map;
    }

    /// <summary>Returns the display name for a core_users.Id (FirstName LastName, or Username, or "?").
    /// Used to seed currentUserName in the sidecar GET envelope so the client can show "you" in tooltips.</summary>
    public string GetUserDisplayName(int userId)
    {
        if (userId <= 0) return "";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(NULLIF(TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')), ''),
                            u.Username, '?') AS DisplayName
            FROM core_users u
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.Id = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() as string ?? "";
    }

    /// <summary>Returns CreatedBy for a guid if it exists (any state — even deleted), else null.
    /// Used by /api/text-ann/{upsert,delete} to enforce owner-only writes regardless of soft-delete state.</summary>
    public int? GetExistingOwner(int pcId, string filePath, string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CreatedBy FROM sys_text_annotations WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        cmd.Parameters.AddWithValue("@g",  guid);
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
    }

    /// <summary>List all TEXT annotations for (pcId, filePath). Active first, deleted at the end; both newest-first.
    /// Filtered to AnnType='text' because the TextAnnotationsModal renders these as text history — draw rows
    /// share the same table but have empty Text and no edit lifecycle, so they'd just clutter the view.</summary>
    public List<TextAnnRow> List(int pcId, string filePath)
    {
        var rows = new List<TextAnnRow>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.Id, t.PcId, t.FilePath, t.AnnotationGuid, t.PageIdx, t.Text,
                   t.CreatedBy,
                   COALESCE(TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')), uc.Username, '?') AS CreatedByName,
                   t.CreatedAt,
                   t.ModifiedBy,
                   CASE WHEN t.ModifiedBy IS NOT NULL
                        THEN COALESCE(TRIM(pm.FirstName || ' ' || COALESCE(NULLIF(pm.LastName,''), '')), um.Username, '?')
                        ELSE NULL END AS ModifiedByName,
                   t.ModifiedAt,
                   t.IsDeleted,
                   t.DeletedBy,
                   CASE WHEN t.DeletedBy IS NOT NULL
                        THEN COALESCE(TRIM(pd.FirstName || ' ' || COALESCE(NULLIF(pd.LastName,''), '')), ud.Username, '?')
                        ELSE NULL END AS DeletedByName,
                   t.DeletedAt
            FROM sys_text_annotations t
            LEFT JOIN core_users uc   ON uc.Id = t.CreatedBy
            LEFT JOIN core_persons pc ON pc.PersonId = uc.PersonId
            LEFT JOIN core_users um   ON um.Id = t.ModifiedBy
            LEFT JOIN core_persons pm ON pm.PersonId = um.PersonId
            LEFT JOIN core_users ud   ON ud.Id = t.DeletedBy
            LEFT JOIN core_persons pd ON pd.PersonId = ud.PersonId
            WHERE t.PcId=@pc AND t.FilePath=@fp AND t.AnnType='text'
            ORDER BY t.IsDeleted ASC,
                     COALESCE(t.ModifiedAt, t.CreatedAt) DESC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new TextAnnRow(
                Id:             r.GetInt32(0),
                PcId:           r.GetInt32(1),
                FilePath:       r.GetString(2),
                Guid:           r.GetString(3),
                PageIdx:        r.GetInt32(4),
                Text:           r.GetString(5),
                CreatedBy:      r.GetInt32(6),
                CreatedByName:  r.GetString(7),
                CreatedAt:      r.GetString(8),
                ModifiedBy:     r.IsDBNull(9)  ? null : r.GetInt32(9),
                ModifiedByName: r.IsDBNull(10) ? null : r.GetString(10),
                ModifiedAt:     r.IsDBNull(11) ? null : r.GetString(11),
                IsDeleted:      r.GetInt32(12) == 1,
                DeletedBy:      r.IsDBNull(13) ? null : r.GetInt32(13),
                DeletedByName:  r.IsDBNull(14) ? null : r.GetString(14),
                DeletedAt:      r.IsDBNull(15) ? null : r.GetString(15)));
        }
        return rows;
    }
}
