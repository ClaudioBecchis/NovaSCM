using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PolarisManager;

/// <summary>
/// Layer SQLite locale — unica source of truth per tutte le entità dell'app.
/// Path: %APPDATA%\PolarisManager\novascm.db
/// </summary>
public static class Database
{
    private static readonly string DbPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PolarisManager", "novascm.db");

    private static SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    // ── Inizializzazione schema ────────────────────────────────────────────────
    public static void Initialize()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                ip              TEXT PRIMARY KEY,
                mac             TEXT NOT NULL DEFAULT '—',
                vendor          TEXT NOT NULL DEFAULT '—',
                name            TEXT NOT NULL DEFAULT '—',
                icon            TEXT NOT NULL DEFAULT '❔',
                device_type     TEXT NOT NULL DEFAULT '—',
                connection_type TEXT NOT NULL DEFAULT '❔',
                status          TEXT NOT NULL DEFAULT '—',
                cert_status     TEXT NOT NULL DEFAULT '⬜ No',
                last_seen       TEXT NOT NULL DEFAULT '',
                notes           TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS certificates (
                mac     TEXT PRIMARY KEY,
                icon    TEXT NOT NULL DEFAULT '💻',
                name    TEXT NOT NULL DEFAULT '',
                created TEXT NOT NULL DEFAULT '',
                expires TEXT NOT NULL DEFAULT '',
                status  TEXT NOT NULL DEFAULT '✅ Attivo'
            );

            CREATE TABLE IF NOT EXISTS managed_pcs (
                name    TEXT PRIMARY KEY,
                icon    TEXT NOT NULL DEFAULT '💻',
                ip      TEXT NOT NULL DEFAULT '—',
                os      TEXT NOT NULL DEFAULT '—',
                cpu     TEXT NOT NULL DEFAULT '—',
                ram     TEXT NOT NULL DEFAULT '—',
                status  TEXT NOT NULL DEFAULT '—',
                agent   TEXT NOT NULL DEFAULT '—'
            );

            CREATE TABLE IF NOT EXISTS app_queue (
                mac     TEXT PRIMARY KEY,
                pc      TEXT NOT NULL DEFAULT '',
                ip      TEXT NOT NULL DEFAULT '',
                apps    TEXT NOT NULL DEFAULT '',
                status  TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS opsi_packages (
                name    TEXT PRIMARY KEY,
                version TEXT NOT NULL DEFAULT '',
                status  TEXT NOT NULL DEFAULT '',
                updated TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS workflows (
                id          INTEGER PRIMARY KEY,
                nome        TEXT NOT NULL DEFAULT '',
                descrizione TEXT NOT NULL DEFAULT '',
                versione    INTEGER NOT NULL DEFAULT 1,
                step_count  INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS workflow_steps (
                id          INTEGER PRIMARY KEY,
                workflow_id INTEGER NOT NULL,
                ordine      INTEGER NOT NULL DEFAULT 0,
                nome        TEXT NOT NULL DEFAULT '',
                tipo        TEXT NOT NULL DEFAULT '',
                parametri   TEXT NOT NULL DEFAULT '{}',
                platform    TEXT NOT NULL DEFAULT 'all',
                su_errore   TEXT NOT NULL DEFAULT 'stop',
                FOREIGN KEY (workflow_id) REFERENCES workflows(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS workflow_assignments (
                id            INTEGER PRIMARY KEY,
                pc_name       TEXT NOT NULL DEFAULT '',
                workflow_nome TEXT NOT NULL DEFAULT '',
                workflow_id   INTEGER NOT NULL DEFAULT 0,
                status        TEXT NOT NULL DEFAULT '',
                progress      INTEGER NOT NULL DEFAULT 0,
                assigned_at   TEXT NOT NULL DEFAULT '',
                last_seen     TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS change_requests (
                id            INTEGER PRIMARY KEY,
                pc_name       TEXT NOT NULL DEFAULT '',
                domain        TEXT NOT NULL DEFAULT '',
                ou            TEXT NOT NULL DEFAULT '',
                assigned_user TEXT NOT NULL DEFAULT '',
                status        TEXT NOT NULL DEFAULT 'pending',
                created_at    TEXT NOT NULL DEFAULT '',
                notes         TEXT NOT NULL DEFAULT '',
                last_seen     TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();

        // Migration: aggiunge colonna notes se il database è precedente alla v1.7.2
        try
        {
            using var migCmd = db.CreateCommand();
            migCmd.CommandText = "ALTER TABLE devices ADD COLUMN notes TEXT NOT NULL DEFAULT '';";
            migCmd.ExecuteNonQuery();
        }
        catch (SqliteException) { } // colonna già presente — ignorato
    }

    // ════════════════════════════════════════════════════════════════════════
    // DEVICES
    // ════════════════════════════════════════════════════════════════════════

    public static void UpsertDevice(DeviceRow d)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices (ip, mac, vendor, name, icon, device_type, connection_type, status, cert_status, last_seen)
            VALUES ($ip,$mac,$vendor,$name,$icon,$dt,$ct,$st,$cs,$ls)
            ON CONFLICT(ip) DO UPDATE SET
                mac=$mac, vendor=$vendor, name=$name, icon=$icon,
                device_type=$dt, connection_type=$ct, status=$st,
                cert_status=$cs, last_seen=$ls;
            """;
        cmd.Parameters.AddWithValue("$ip",  d.Ip);
        cmd.Parameters.AddWithValue("$mac", d.Mac);
        cmd.Parameters.AddWithValue("$vendor", d.Vendor);
        cmd.Parameters.AddWithValue("$name",   d.Name);
        cmd.Parameters.AddWithValue("$icon",   d.Icon);
        cmd.Parameters.AddWithValue("$dt",  d.DeviceType);
        cmd.Parameters.AddWithValue("$ct",  d.ConnectionType);
        cmd.Parameters.AddWithValue("$st",  d.Status);
        cmd.Parameters.AddWithValue("$cs",  d.CertStatus);
        cmd.Parameters.AddWithValue("$ls",  DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        cmd.ExecuteNonQuery();
    }

    public static List<DeviceRow> GetDevices()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT ip,mac,vendor,name,icon,device_type,connection_type,status,cert_status FROM devices ORDER BY ip;";
        var list = new List<DeviceRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DeviceRow
            {
                Ip             = r.GetString(0),
                Mac            = r.GetString(1),
                Vendor         = r.GetString(2),
                Name           = r.GetString(3),
                Icon           = r.GetString(4),
                DeviceType     = r.GetString(5),
                ConnectionType = r.GetString(6),
                Status         = r.GetString(7),
                CertStatus     = r.GetString(8),
            });
        return list;
    }

    public static void DeleteDevice(string ip)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM devices WHERE ip=$ip;";
        cmd.Parameters.AddWithValue("$ip", ip);
        cmd.ExecuteNonQuery();
    }

    // ════════════════════════════════════════════════════════════════════════
    // CERTIFICATES
    // ════════════════════════════════════════════════════════════════════════

    public static void UpsertCert(CertRow c)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO certificates (mac, icon, name, created, expires, status)
            VALUES ($mac,$icon,$name,$created,$expires,$status)
            ON CONFLICT(mac) DO UPDATE SET
                icon=$icon, name=$name, created=$created, expires=$expires, status=$status;
            """;
        cmd.Parameters.AddWithValue("$mac",     c.Mac);
        cmd.Parameters.AddWithValue("$icon",    c.Icon);
        cmd.Parameters.AddWithValue("$name",    c.Name);
        cmd.Parameters.AddWithValue("$created", c.Created);
        cmd.Parameters.AddWithValue("$expires", c.Expires);
        cmd.Parameters.AddWithValue("$status",  c.Status);
        cmd.ExecuteNonQuery();
    }

    public static List<CertRow> GetCerts()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT icon,name,mac,created,expires,status FROM certificates ORDER BY name;";
        var list = new List<CertRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CertRow(r.GetString(0), r.GetString(1), r.GetString(2),
                                 r.GetString(3), r.GetString(4), r.GetString(5)));
        return list;
    }

    public static void DeleteCert(string mac)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM certificates WHERE mac=$mac;";
        cmd.Parameters.AddWithValue("$mac", mac);
        cmd.ExecuteNonQuery();
    }

    // ════════════════════════════════════════════════════════════════════════
    // MANAGED PCs
    // ════════════════════════════════════════════════════════════════════════

    public static void UpsertPc(PcRow p)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO managed_pcs (name, icon, ip, os, cpu, ram, status, agent)
            VALUES ($name,$icon,$ip,$os,$cpu,$ram,$status,$agent)
            ON CONFLICT(name) DO UPDATE SET
                icon=$icon, ip=$ip, os=$os, cpu=$cpu, ram=$ram, status=$status, agent=$agent;
            """;
        cmd.Parameters.AddWithValue("$name",   p.Name);
        cmd.Parameters.AddWithValue("$icon",   p.Icon);
        cmd.Parameters.AddWithValue("$ip",     p.Ip);
        cmd.Parameters.AddWithValue("$os",     p.Os);
        cmd.Parameters.AddWithValue("$cpu",    p.Cpu);
        cmd.Parameters.AddWithValue("$ram",    p.Ram);
        cmd.Parameters.AddWithValue("$status", p.Status);
        cmd.Parameters.AddWithValue("$agent",  p.Agent);
        cmd.ExecuteNonQuery();
    }

    public static List<PcRow> GetPcs()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT icon,name,ip,os,cpu,ram,status,agent FROM managed_pcs ORDER BY name;";
        var list = new List<PcRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                               r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7)));
        return list;
    }

    public static void DeletePc(string name)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM managed_pcs WHERE name=$name;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    // ════════════════════════════════════════════════════════════════════════
    // APP QUEUE
    // ════════════════════════════════════════════════════════════════════════

    public static void UpsertAppQueue(AppQueueRow row)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_queue (mac, pc, ip, apps, status)
            VALUES ($mac,$pc,$ip,$apps,$status)
            ON CONFLICT(mac) DO UPDATE SET pc=$pc, ip=$ip, apps=$apps, status=$status;
            """;
        cmd.Parameters.AddWithValue("$mac",    row.Mac);
        cmd.Parameters.AddWithValue("$pc",     row.Pc);
        cmd.Parameters.AddWithValue("$ip",     row.Ip);
        cmd.Parameters.AddWithValue("$apps",   row.Apps);
        cmd.Parameters.AddWithValue("$status", row.Status);
        cmd.ExecuteNonQuery();
    }

    public static List<AppQueueRow> GetAppQueue()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT pc,ip,mac,apps,status FROM app_queue ORDER BY pc;";
        var list = new List<AppQueueRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AppQueueRow(r.GetString(0), r.GetString(1), r.GetString(2),
                                    r.GetString(3), r.GetString(4)));
        return list;
    }

    // ════════════════════════════════════════════════════════════════════════
    // OPSI PACKAGES
    // ════════════════════════════════════════════════════════════════════════

    public static void UpsertOpsi(OpsiRow row)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO opsi_packages (name, version, status, updated)
            VALUES ($name,$version,$status,$updated)
            ON CONFLICT(name) DO UPDATE SET version=$version, status=$status, updated=$updated;
            """;
        cmd.Parameters.AddWithValue("$name",    row.Name);
        cmd.Parameters.AddWithValue("$version", row.Version);
        cmd.Parameters.AddWithValue("$status",  row.Status);
        cmd.Parameters.AddWithValue("$updated", row.Updated);
        cmd.ExecuteNonQuery();
    }

    public static List<OpsiRow> GetOpsi()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT name,version,status,updated FROM opsi_packages ORDER BY name;";
        var list = new List<OpsiRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OpsiRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    public static void DeleteOpsi(string name)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM opsi_packages WHERE name=$name;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    // ════════════════════════════════════════════════════════════════════════
    // WORKFLOWS
    // ════════════════════════════════════════════════════════════════════════

    public static void UpsertWorkflow(WfRow w)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO workflows (id, nome, descrizione, versione, step_count)
            VALUES ($id,$nome,$desc,$ver,$sc)
            ON CONFLICT(id) DO UPDATE SET nome=$nome, descrizione=$desc, versione=$ver, step_count=$sc;
            """;
        cmd.Parameters.AddWithValue("$id",   w.Id);
        cmd.Parameters.AddWithValue("$nome", w.Nome);
        cmd.Parameters.AddWithValue("$desc", w.Descrizione);
        cmd.Parameters.AddWithValue("$ver",  w.Versione);
        cmd.Parameters.AddWithValue("$sc",   w.StepCount);
        cmd.ExecuteNonQuery();
    }

    public static List<WfRow> GetWorkflows()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,nome,descrizione,versione,step_count FROM workflows ORDER BY id;";
        var list = new List<WfRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WfRow
            {
                Id          = r.GetInt32(0),
                Nome        = r.GetString(1),
                Descrizione = r.GetString(2),
                Versione    = r.GetInt32(3),
                StepCount   = r.GetInt32(4),
            });
        return list;
    }

    public static void DeleteWorkflow(int id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM workflows WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void UpsertWorkflowStep(WfStepRow s)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO workflow_steps (id, workflow_id, ordine, nome, tipo, parametri, platform, su_errore)
            VALUES ($id,$wid,$ord,$nome,$tipo,$par,$plat,$err)
            ON CONFLICT(id) DO UPDATE SET
                workflow_id=$wid, ordine=$ord, nome=$nome, tipo=$tipo,
                parametri=$par, platform=$plat, su_errore=$err;
            """;
        cmd.Parameters.AddWithValue("$id",   s.Id);
        cmd.Parameters.AddWithValue("$wid",  s.WorkflowId);
        cmd.Parameters.AddWithValue("$ord",  s.Ordine);
        cmd.Parameters.AddWithValue("$nome", s.Nome);
        cmd.Parameters.AddWithValue("$tipo", s.Tipo);
        cmd.Parameters.AddWithValue("$par",  s.Parametri);
        cmd.Parameters.AddWithValue("$plat", s.Platform);
        cmd.Parameters.AddWithValue("$err",  s.SuErrore);
        cmd.ExecuteNonQuery();
    }

    public static List<WfStepRow> GetWorkflowSteps(int workflowId)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,workflow_id,ordine,nome,tipo,parametri,platform,su_errore FROM workflow_steps WHERE workflow_id=$wid ORDER BY ordine;";
        cmd.Parameters.AddWithValue("$wid", workflowId);
        var list = new List<WfStepRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WfStepRow
            {
                Id         = r.GetInt32(0),
                WorkflowId = r.GetInt32(1),
                Ordine     = r.GetInt32(2),
                Nome       = r.GetString(3),
                Tipo       = r.GetString(4),
                Parametri  = r.GetString(5),
                Platform   = r.GetString(6),
                SuErrore   = r.GetString(7),
            });
        return list;
    }

    public static void DeleteWorkflowSteps(int workflowId)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM workflow_steps WHERE workflow_id=$wid;";
        cmd.Parameters.AddWithValue("$wid", workflowId);
        cmd.ExecuteNonQuery();
    }

    public static void UpsertAssignment(WfAssignRow a)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO workflow_assignments (id, pc_name, workflow_nome, workflow_id, status, progress, assigned_at, last_seen)
            VALUES ($id,$pc,$wn,$wid,$st,$pr,$at,$ls)
            ON CONFLICT(id) DO UPDATE SET
                pc_name=$pc, workflow_nome=$wn, workflow_id=$wid,
                status=$st, progress=$pr, assigned_at=$at, last_seen=$ls;
            """;
        cmd.Parameters.AddWithValue("$id",  a.Id);
        cmd.Parameters.AddWithValue("$pc",  a.PcName);
        cmd.Parameters.AddWithValue("$wn",  a.WorkflowNome);
        cmd.Parameters.AddWithValue("$wid", a.WorkflowId);
        cmd.Parameters.AddWithValue("$st",  a.Status);
        cmd.Parameters.AddWithValue("$pr",  a.Progress);
        cmd.Parameters.AddWithValue("$at",  a.AssignedAt);
        cmd.Parameters.AddWithValue("$ls",  a.LastSeen);
        cmd.ExecuteNonQuery();
    }

    public static List<WfAssignRow> GetAssignments()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,pc_name,workflow_nome,workflow_id,status,progress,assigned_at,last_seen FROM workflow_assignments ORDER BY id DESC;";
        var list = new List<WfAssignRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WfAssignRow
            {
                Id           = r.GetInt32(0),
                PcName       = r.GetString(1),
                WorkflowNome = r.GetString(2),
                WorkflowId   = r.GetInt32(3),
                Status       = r.GetString(4),
                Progress     = r.GetInt32(5),
                AssignedAt   = r.GetString(6),
                LastSeen     = r.GetString(7),
            });
        return list;
    }

    // ════════════════════════════════════════════════════════════════════════
    // CHANGE REQUESTS
    // ════════════════════════════════════════════════════════════════════════

    public static int InsertCr(CrRow c)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO change_requests (pc_name, domain, ou, assigned_user, status, created_at, notes, last_seen)
            VALUES ($pc,$domain,$ou,$user,$status,$created,$notes,$ls);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$pc",      c.PcName);
        cmd.Parameters.AddWithValue("$domain",  c.Domain);
        cmd.Parameters.AddWithValue("$ou",      c.Ou);
        cmd.Parameters.AddWithValue("$user",    c.AssignedUser);
        cmd.Parameters.AddWithValue("$status",  c.Status);
        cmd.Parameters.AddWithValue("$created", c.CreatedAt);
        cmd.Parameters.AddWithValue("$notes",   c.Notes);
        cmd.Parameters.AddWithValue("$ls",      c.LastSeen);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void UpsertCr(CrRow c)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO change_requests (id, pc_name, domain, ou, assigned_user, status, created_at, notes, last_seen)
            VALUES ($id,$pc,$domain,$ou,$user,$status,$created,$notes,$ls)
            ON CONFLICT(id) DO UPDATE SET
                pc_name=$pc, domain=$domain, ou=$ou, assigned_user=$user,
                status=$status, created_at=$created, notes=$notes, last_seen=$ls;
            """;
        cmd.Parameters.AddWithValue("$id",      c.Id);
        cmd.Parameters.AddWithValue("$pc",      c.PcName);
        cmd.Parameters.AddWithValue("$domain",  c.Domain);
        cmd.Parameters.AddWithValue("$ou",      c.Ou);
        cmd.Parameters.AddWithValue("$user",    c.AssignedUser);
        cmd.Parameters.AddWithValue("$status",  c.Status);
        cmd.Parameters.AddWithValue("$created", c.CreatedAt);
        cmd.Parameters.AddWithValue("$notes",   c.Notes);
        cmd.Parameters.AddWithValue("$ls",      c.LastSeen);
        cmd.ExecuteNonQuery();
    }

    public static List<CrRow> GetCrs()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,pc_name,domain,ou,assigned_user,status,created_at,notes,last_seen FROM change_requests ORDER BY id DESC;";
        var list = new List<CrRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CrRow
            {
                Id           = r.GetInt32(0),
                PcName       = r.GetString(1),
                Domain       = r.GetString(2),
                Ou           = r.GetString(3),
                AssignedUser = r.GetString(4),
                Status       = r.GetString(5),
                CreatedAt    = r.GetString(6),
                Notes        = r.GetString(7),
                LastSeen     = r.GetString(8),
            });
        return list;
    }

    public static void UpdateCrStatus(int id, string status)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE change_requests SET status=$status WHERE id=$id;";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$id",     id);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteCr(int id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM change_requests WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ── Note e tag device ─────────────────────────────────────────────────────
    public static void SaveDeviceNote(string ip, string note)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices (ip, mac, vendor, name, icon, device_type, connection_type, status, cert_status, last_seen)
            VALUES ($ip,'—','—','—','❔','—','❔','—','⬜ No','')
            ON CONFLICT(ip) DO UPDATE SET last_seen=last_seen;
            """;
        cmd.Parameters.AddWithValue("$ip", ip);
        cmd.ExecuteNonQuery();

        using var cmd2 = db.CreateCommand();
        cmd2.CommandText = "UPDATE devices SET notes=$notes WHERE ip=$ip;";
        cmd2.Parameters.AddWithValue("$notes", note);
        cmd2.Parameters.AddWithValue("$ip", ip);
        cmd2.ExecuteNonQuery();
    }

    public static void SaveDeviceTag(string ip, string tag)
    {
        using var db = Open();
        using var cmd2 = db.CreateCommand();
        cmd2.CommandText = "UPDATE devices SET notes=$notes WHERE ip=$ip;";
        cmd2.Parameters.AddWithValue("$notes", "[tag:" + tag + "]");
        cmd2.Parameters.AddWithValue("$ip", ip);
        cmd2.ExecuteNonQuery();
    }
}
