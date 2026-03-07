#!/usr/bin/env python3
"""
Popola il DB NovaSCM con dati demo realistici.
Uso: python3 seed_demo.py [--db /data/novascm.db]
"""
import sqlite3, json, datetime, argparse, sys

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default="/data/novascm.db")
    args = ap.parse_args()

    conn = sqlite3.connect(args.db)
    conn.row_factory = sqlite3.Row
    now = datetime.datetime.now().isoformat()

    print(f"[seed] DB: {args.db}")

    # ── Pulisci dati demo precedenti ──────────────────────────────────────────
    conn.executescript("""
        DELETE FROM pc_workflow_steps;
        DELETE FROM pc_workflows;
        DELETE FROM workflow_steps;
        DELETE FROM workflows;
        DELETE FROM cr_steps;
        DELETE FROM cr;
        DELETE FROM settings;
    """)

    # ── Workflow 1: Deploy Base Windows 11 ────────────────────────────────────
    conn.execute("""INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at)
        VALUES ('Deploy Base Windows 11',
                'Installazione software standard + aggiornamenti post-deploy',
                1, ?, ?)""", (now, now))
    wf1 = conn.execute("SELECT id FROM workflows WHERE nome='Deploy Base Windows 11'").fetchone()["id"]

    wf1_steps = [
        (1, "Messaggio avvio",         "message",        '{"text":"Deploy avviato — installazione in corso..."}', "all",     "continua"),
        (2, "Installa 7-Zip",          "winget_install", '{"id":"7zip.7zip"}',                                   "windows", "continua"),
        (3, "Installa Firefox",        "winget_install", '{"id":"Mozilla.Firefox"}',                             "windows", "continua"),
        (4, "Installa VLC",            "winget_install", '{"id":"VideoLAN.VLC"}',                                "windows", "continua"),
        (5, "Installa Notepad++",      "winget_install", '{"id":"Notepad++.Notepad++"}',                         "windows", "continua"),
        (6, "Installa Adobe Reader",   "winget_install", '{"id":"Adobe.Acrobat.Reader.64-bit"}',                 "windows", "continua"),
        (7, "Aggiornamenti Windows",   "windows_update", '{"category":"all","exclude_drivers":false,"reboot_after":false}', "windows", "continua"),
        (8, "Messaggio completato",    "message",        '{"text":"Deploy completato. Riavvio in corso..."}',    "all",     "continua"),
        (9, "Riavvio finale",          "reboot",         '{"delay":15}',                                         "windows", "continua"),
    ]
    for o, nome, tipo, par, plat, sue in wf1_steps:
        conn.execute("""INSERT INTO workflow_steps (workflow_id, ordine, nome, tipo, parametri, su_errore, platform)
            VALUES (?,?,?,?,?,?,?)""", (wf1, o, nome, tipo, par, sue, plat))

    # ── Workflow 2: Aggiornamenti Security ────────────────────────────────────
    conn.execute("""INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at)
        VALUES ('Aggiornamenti Windows Security',
                'Solo aggiornamenti di sicurezza critici + riavvio',
                1, ?, ?)""", (now, now))
    wf2 = conn.execute("SELECT id FROM workflows WHERE nome='Aggiornamenti Windows Security'").fetchone()["id"]

    conn.executemany("""INSERT INTO workflow_steps (workflow_id, ordine, nome, tipo, parametri, su_errore, platform)
        VALUES (?,?,?,?,?,?,?)""", [
        (wf2, 1, "Scan aggiornamenti security", "windows_update",
         '{"category":"security","exclude_drivers":true,"reboot_after":false}', "continua", "windows"),
        (wf2, 2, "Riavvio se necessario",        "reboot",
         '{"delay":30}',                                                         "continua", "windows"),
    ])

    # ── Workflow 3: Setup Server Linux ────────────────────────────────────────
    conn.execute("""INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at)
        VALUES ('Setup Server Linux',
                'Configurazione base Debian/Ubuntu: nginx, curl, git',
                1, ?, ?)""", (now, now))
    wf3 = conn.execute("SELECT id FROM workflows WHERE nome='Setup Server Linux'").fetchone()["id"]

    conn.executemany("""INSERT INTO workflow_steps (workflow_id, ordine, nome, tipo, parametri, su_errore, platform)
        VALUES (?,?,?,?,?,?,?)""", [
        (wf3, 1, "Update lista pacchetti", "shell_script",    '{"script":"apt-get update -q"}',                           "stop",    "linux"),
        (wf3, 2, "Installa nginx",         "apt_install",     '{"package":"nginx"}',                                       "continua","linux"),
        (wf3, 3, "Installa curl e git",    "apt_install",     '{"package":"curl git htop"}',                               "continua","linux"),
        (wf3, 4, "Abilita e avvia nginx",  "systemd_service", '{"name":"nginx","action":"enable"}',                        "continua","linux"),
        (wf3, 5, "Completato",             "message",         '{"text":"Server Linux configurato correttamente"}',         "continua","all"),
    ])

    # ── Impostazioni: default workflow = Deploy Base ───────────────────────────
    conn.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('default_workflow_id', ?)", (str(wf1),))

    # ── Change Request demo ───────────────────────────────────────────────────
    crs = [
        ("PC-LAB-001",    "WORKGROUP",            "", "192.168.1.1",  "","","","ChangeMe!", "[]","","Postazione laboratorio A", "open",   now, wf1),
        ("PC-LAB-002",    "WORKGROUP",            "", "192.168.1.1",  "","","","ChangeMe!", "[]","","Postazione laboratorio B", "open",   now, wf1),
        ("PC-LAB-003",    "WORKGROUP",            "", "192.168.1.1",  "","","","ChangeMe!", "[]","","Postazione laboratorio C", "open",   now, wf1),
        ("PC-UFFICIO-01", "corp.example.com",  "OU=Computers,DC=corp,DC=example,DC=com",
                          "192.168.1.10","Administrator","","","ChangeMe!", "[]","claudio","Postazione direzione","open",  now, wf1),
        ("PC-UFFICIO-02", "corp.example.com",  "OU=Computers,DC=corp,DC=example,DC=com",
                          "192.168.1.10","Administrator","","","ChangeMe!", "[]","alessia","Postazione segreteria","open", now, wf1),
        ("SRV-LINUX-01",  "WORKGROUP",            "","","","","","","[]","","Server Ubuntu test",       "open",   now, wf3),
    ]
    for row in crs:
        conn.execute("""INSERT OR IGNORE INTO cr
            (pc_name,domain,ou,dc_ip,join_user,join_pass,odj_blob,admin_pass,
             software,assigned_user,notes,status,created_at,workflow_id)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)""", row)

    # ── Assegnazioni con stati diversi (per visualizzare la progress bar) ─────
    def assign(pc, wf_id, status, started=None, completed=None, last_seen=None):
        conn.execute("""INSERT OR IGNORE INTO pc_workflows
            (pc_name, workflow_id, status, assigned_at, started_at, completed_at, last_seen)
            VALUES (?,?,?,?,?,?,?)""",
            (pc, wf_id, status, now, started, completed, last_seen))
        return conn.execute(
            "SELECT id FROM pc_workflows WHERE pc_name=? AND workflow_id=?", (pc, wf_id)
        ).fetchone()["id"]

    # PC-LAB-001: completed
    pw1 = assign("PC-LAB-001", wf1, "completed",
                 started=now, completed=now, last_seen=now)
    steps_wf1 = conn.execute(
        "SELECT id FROM workflow_steps WHERE workflow_id=? ORDER BY ordine", (wf1,)
    ).fetchall()
    for s in steps_wf1:
        conn.execute("""INSERT OR IGNORE INTO pc_workflow_steps
            (pc_workflow_id, step_id, status, output, timestamp) VALUES (?,?,?,?,?)""",
            (pw1, s["id"], "done", "Completato con successo", now))

    # PC-LAB-002: running al step 4
    pw2 = assign("PC-LAB-002", wf1, "running",
                 started=now, last_seen=now)
    for i, s in enumerate(steps_wf1):
        if i < 3:
            st, out = "done",    "OK"
        elif i == 3:
            st, out = "running", "Installazione in corso..."
        else:
            st, out = "pending", ""
        conn.execute("""INSERT OR IGNORE INTO pc_workflow_steps
            (pc_workflow_id, step_id, status, output, timestamp) VALUES (?,?,?,?,?)""",
            (pw2, s["id"], st, out, now if st != "pending" else None))

    # PC-UFFICIO-01: pending (non ancora iniziato)
    assign("PC-UFFICIO-01", wf1, "pending")

    # SRV-LINUX-01: running al step 2
    pw3 = assign("SRV-LINUX-01", wf3, "running",
                 started=now, last_seen=now)
    steps_wf3 = conn.execute(
        "SELECT id FROM workflow_steps WHERE workflow_id=? ORDER BY ordine", (wf3,)
    ).fetchall()
    for i, s in enumerate(steps_wf3):
        if i == 0:
            st, out = "done",    "apt-get update: 0 upgraded"
        elif i == 1:
            st, out = "running", "Installing nginx..."
        else:
            st, out = "pending", ""
        conn.execute("""INSERT OR IGNORE INTO pc_workflow_steps
            (pc_workflow_id, step_id, status, output, timestamp) VALUES (?,?,?,?,?)""",
            (pw3, s["id"], st, out, now if st != "pending" else None))

    conn.commit()
    conn.close()

    print("[seed] Demo completato:")
    print(f"  - 3 workflow creati (id {wf1},{wf2},{wf3})")
    print(f"  - 6 Change Request")
    print(f"  - 4 assegnazioni (completed/running/running/pending)")
    print(f"  - default_workflow_id = {wf1}")
    print(f"  Apri: http://YOUR-SERVER-IP:9091")

if __name__ == "__main__":
    main()
