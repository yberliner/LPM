using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record PcListItem(int PcId, string FullName, string ExternalId);
public record PcDetailInfo(int PcId, string FirstName, string LastName, string ExternalId,
    string Phone, string Email, string Notes, string StartDate, string DateOfBirth, string Sex)
{
    public string FullName => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";
}
public record PcSessionInfo(int SessionId, string Date, string AuditorName,
    int LengthSec, int AdminSec, bool IsFree, string VerifiedStatus);
public record PcPayment(int PaymentId, string Date, int HoursBought, int AmountPaid, string? Notes);
public record PcStats(int TotalSessions, int FreeSessions, long UsedSec,
    int TotalHoursPurchased, int TotalAmountPaid, string? LastSessionDate);

public class PcService
{
    private readonly string _connectionString;

    public PcService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Payments table
        using var c1 = conn.CreateCommand();
        c1.CommandText = @"
            CREATE TABLE IF NOT EXISTS Payments (
                PaymentId   INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId        INTEGER NOT NULL,
                PaymentDate TEXT    NOT NULL,
                HoursBought INTEGER NOT NULL DEFAULT 0,
                AmountPaid  INTEGER NOT NULL DEFAULT 0,
                Notes       TEXT,
                CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
            )";
        c1.ExecuteNonQuery();

        // Extra columns on PCs (ExternalId, Notes, StartDate only — Phone/Email/Age/Sex live on Persons)
        foreach (var (col, type) in new[] {
            ("Phone",     "TEXT"),   // kept for legacy read; new writes go to Persons
            ("Email",     "TEXT"),
            ("Notes",     "TEXT"),
            ("StartDate", "TEXT") })
        {
            using var ck = conn.CreateCommand();
            ck.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('PCs') WHERE name='{col}'";
            if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
            {
                using var alt = conn.CreateCommand();
                alt.CommandText = $"ALTER TABLE PCs ADD COLUMN {col} {type}";
                alt.ExecuteNonQuery();
            }
        }

        // Ensure Persons has Phone/Email/DateOfBirth/Sex columns (AcademyService may have added some already)
        foreach (var (col, type) in new[] {
            ("Phone", "TEXT"), ("Email", "TEXT"), ("Age", "INTEGER"), ("DateOfBirth", "TEXT"), ("Sex", "TEXT") })
        {
            using var ck = conn.CreateCommand();
            ck.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Persons') WHERE name='{col}'";
            if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
            {
                using var alt = conn.CreateCommand();
                alt.CommandText = $"ALTER TABLE Persons ADD COLUMN {col} {type}";
                alt.ExecuteNonQuery();
            }
        }

        // One-time migration: copy legacy Phone/Email from PCs → Persons (where Persons row is still NULL)
        using var migPhone = conn.CreateCommand();
        migPhone.CommandText = @"
            UPDATE Persons SET Phone = (SELECT Phone FROM PCs WHERE PCs.PcId = Persons.PersonId)
            WHERE PersonId IN (SELECT PcId FROM PCs WHERE Phone IS NOT NULL AND Phone != '')
              AND (Phone IS NULL OR Phone = '')";
        migPhone.ExecuteNonQuery();

        using var migEmail = conn.CreateCommand();
        migEmail.CommandText = @"
            UPDATE Persons SET Email = (SELECT Email FROM PCs WHERE PCs.PcId = Persons.PersonId)
            WHERE PersonId IN (SELECT PcId FROM PCs WHERE Email IS NOT NULL AND Email != '')
              AND (Email IS NULL OR Email = '')";
        migEmail.ExecuteNonQuery();
    }

    public List<PcListItem> GetAllPcs()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(pc.ExternalId, '') AS ExternalId
            FROM PCs pc
            JOIN Persons p ON p.PersonId = pc.PcId
            ORDER BY p.FirstName, p.LastName";
        var list = new List<PcListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcListItem(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public int AddPcWithPerson(string firstName, string lastName,
        string phone, string email, string dateOfBirth, string sex)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            INSERT INTO Persons (FirstName, LastName, Phone, Email, DateOfBirth, Sex)
            VALUES (@fn, @ln, @ph, @em, @dob, @sex)";
        pCmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",  Nv(phone));
        pCmd.Parameters.AddWithValue("@em",  Nv(email));
        pCmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@sex", Nv(sex));
        pCmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var personId = (int)(long)idCmd.ExecuteScalar()!;

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT INTO PCs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", personId);
        pcCmd.ExecuteNonQuery();

        return personId;
    }

    public PcDetailInfo? GetPcDetail(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.FirstName,             COALESCE(p.LastName,''),
                   COALESCE(pc.ExternalId,''), COALESCE(p.Phone,''),
                   COALESCE(p.Email,''),        COALESCE(pc.Notes,''),
                   COALESCE(pc.StartDate,''),   COALESCE(p.DateOfBirth,''), COALESCE(p.Sex,'')
            FROM PCs pc
            JOIN Persons p ON p.PersonId = pc.PcId
            WHERE pc.PcId = @id";
        cmd.Parameters.AddWithValue("@id", pcId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PcDetailInfo(pcId,
            r.GetString(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
            r.GetString(7), r.GetString(8));
    }

    public void UpdatePcDetail(int pcId, string firstName, string lastName,
        string externalId, string phone, string email, string startDate, string notes,
        string dateOfBirth, string sex)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            UPDATE Persons SET FirstName=@fn, LastName=@ln,
                               Phone=@ph, Email=@em, DateOfBirth=@dob, Sex=@sex
            WHERE PersonId=@id";
        pCmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",  Nv(phone));
        pCmd.Parameters.AddWithValue("@em",  Nv(email));
        pCmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@sex", Nv(sex));
        pCmd.Parameters.AddWithValue("@id",  pcId);
        pCmd.ExecuteNonQuery();

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = @"
            UPDATE PCs SET ExternalId=@ext, StartDate=@sd, Notes=@nt
            WHERE PcId=@id";
        pcCmd.Parameters.AddWithValue("@ext", Nv(externalId));
        pcCmd.Parameters.AddWithValue("@sd",  Nv(startDate));
        pcCmd.Parameters.AddWithValue("@nt",  Nv(notes));
        pcCmd.Parameters.AddWithValue("@id",  pcId);
        pcCmd.ExecuteNonQuery();
    }

    public PcStats GetPcStats(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var sCmd = conn.CreateCommand();
        sCmd.CommandText = @"
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN IsFreeSession=1 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN IsFreeSession=0 THEN LengthSeconds ELSE 0 END), 0),
                   MAX(SessionDate)
            FROM Sessions WHERE PcId=@id";
        sCmd.Parameters.AddWithValue("@id", pcId);
        using var sr = sCmd.ExecuteReader();
        sr.Read();
        int    total       = sr.GetInt32(0);
        int    free        = sr.GetInt32(1);
        long   usedSec     = sr.GetInt64(2);
        string? lastDate   = sr.IsDBNull(3) ? null : sr.GetString(3);

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            SELECT COALESCE(SUM(HoursBought),0), COALESCE(SUM(AmountPaid),0)
            FROM Payments WHERE PcId=@id";
        pCmd.Parameters.AddWithValue("@id", pcId);
        using var pr = pCmd.ExecuteReader();
        pr.Read();
        int hours  = pr.GetInt32(0);
        int amount = pr.GetInt32(1);

        return new PcStats(total, free, usedSec, hours, amount, lastDate);
    }

    public List<PcSessionInfo> GetPcSessions(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.SessionId, s.SessionDate, p.FirstName,
                   s.LengthSeconds, s.AdminSeconds, s.IsFreeSession,
                   COALESCE(s.VerifiedStatus,'Draft')
            FROM Sessions s
            JOIN Persons p ON p.PersonId = s.AuditorId
            WHERE s.PcId=@id
            ORDER BY s.SessionDate DESC, s.SequenceInDay DESC";
        cmd.Parameters.AddWithValue("@id", pcId);
        var list = new List<PcSessionInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcSessionInfo(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.GetInt32(3), r.GetInt32(4),
                r.GetInt32(5) == 1, r.GetString(6)));
        return list;
    }

    public List<PcPayment> GetPayments(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT PaymentId, PaymentDate, HoursBought, AmountPaid, Notes
            FROM Payments WHERE PcId=@id
            ORDER BY PaymentDate DESC";
        cmd.Parameters.AddWithValue("@id", pcId);
        var list = new List<PcPayment>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcPayment(
                r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    public void AddPayment(int pcId, string date, int hoursBought, int amountPaid, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Payments (PcId, PaymentDate, HoursBought, AmountPaid, Notes)
            VALUES (@pcId, @date, @hrs, @amt, @notes)";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@hrs",   hoursBought);
        cmd.Parameters.AddWithValue("@amt",   amountPaid);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeletePayment(int paymentId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Payments WHERE PaymentId=@id";
        cmd.Parameters.AddWithValue("@id", paymentId);
        cmd.ExecuteNonQuery();
    }

    private static object Nv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
}
