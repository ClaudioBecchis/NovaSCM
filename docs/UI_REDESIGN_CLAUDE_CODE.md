# NovaSCM — Redesign UI Enterprise
> **Istruzioni per Claude Code** — implementa il nuovo `server/web/index.html`

---

## Obiettivo

Sostituire `server/web/index.html` con una nuova interfaccia enterprise.  
Il file rimane un **singolo HTML autocontenuto** (Alpine.js CDN + tutto inline).  
**Tutta la logica JavaScript `function app()` va copiata identica** — si ridisegna solo HTML e CSS.

---

## Font da aggiungere in `<head>`

```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&family=DM+Mono:wght@400;500&display=swap" rel="stylesheet">
```

- **UI generale:** `'IBM Plex Sans', sans-serif`
- **Hostname, comandi, badge tipo, timestamp:** `'DM Mono', monospace`

---

## CSS Variables (`:root`)

```css
:root {
  --bg:          #080b12;
  --surface:     #0c0f1a;
  --surface-2:   #111525;
  --surface-3:   #161b2e;
  --border:      #1c2238;
  --border-2:    #253060;
  --text:        #b8c4e0;
  --text-bright: #dde6f8;
  --muted:       #4a5474;
  --very-muted:  #272e48;
  --ok:          #00d97e;
  --ok-dim:      rgba(0,217,126,.1);
  --run:         #4d9fff;
  --run-dim:     rgba(77,159,255,.1);
  --warn:        #f5a623;
  --warn-dim:    rgba(245,166,35,.1);
  --err:         #ff4757;
  --err-dim:     rgba(255,71,87,.1);
  --accent:      #4d9fff;
  --accent-glow: rgba(77,159,255,.2);
  --r:           3px;
  --r2:          6px;
  --sidebar-w:   196px;
}
```

---

## Layout

```
┌──────────────┬──────────────────────────────────────┐
│ SIDEBAR      │ TOPBAR (48px)                         │
│ 196px        │  sinistra: sezione attiva             │
│              │  destra: LED live + ora + btn azione  │
│  NOVA SCM    ├──────────────────────────────────────┤
│  ─────────   │                                       │
│  ● Dashboard │   CONTENT (scroll verticale)          │
│  ○ Deploy    │                                       │
│  ○ Workflow  │                                       │
│  ○ Change Req│                                       │
│  ○ Settings  │                                       │
│              │                                       │
│  ─────────   │                                       │
│  v1.7.8 ●ok  │                                       │
└──────────────┴──────────────────────────────────────┘
```

### Sidebar
- Sfondo `--bg`, bordo destro `--border`
- Logo: `NOVA` in `DM Mono 500` colore `--accent` + `SCM` in `IBM Plex Sans --muted`
- Sotto logo: `Deploy Console` in `DM Mono 9px uppercase --very-muted`
- Voci nav: cerchio `●` (attivo) / `○` (inattivo) — **niente emoji**
- Footer: versione + dot `●` verde

### Topbar
- Sfondo `--surface`, `border-bottom: 1px solid var(--border)`
- Effetto scanline sottile con `::after` e `repeating-linear-gradient`
- Destra: dot pulsante live (animazione `pulse-led`) + ora in `DM Mono`

---

## Componenti CSS

### LED animato
```css
.led { width:7px; height:7px; border-radius:50%; display:inline-block; flex-shrink:0; }
.led-ok  { background:var(--ok);  box-shadow:0 0 5px var(--ok); }
.led-run { background:var(--run); box-shadow:0 0 5px var(--run);
           animation: pulse-led 1.4s ease-in-out infinite; }
.led-warn{ background:var(--warn);box-shadow:0 0 4px var(--warn); }
.led-err { background:var(--err); box-shadow:0 0 5px var(--err); }
.led-skip{ background:var(--very-muted); }

@keyframes pulse-led {
  0%,100% { opacity:1; transform:scale(1); }
  50%     { opacity:.5; transform:scale(1.4); }
}
```

### Badge stato
```css
.badge {
  display:inline-flex; align-items:center; gap:5px;
  padding:2px 7px; border-radius:var(--r);
  font-family:'DM Mono',monospace; font-size:10px; font-weight:500;
  letter-spacing:.3px; text-transform:uppercase;
}
.b-ok   { background:var(--ok-dim);  color:var(--ok);  border:1px solid rgba(0,217,126,.2); }
.b-run  { background:var(--run-dim); color:var(--run); border:1px solid rgba(77,159,255,.2); }
.b-warn { background:var(--warn-dim);color:var(--warn);border:1px solid rgba(245,166,35,.2); }
.b-err  { background:var(--err-dim); color:var(--err); border:1px solid rgba(255,71,87,.2); }
.b-skip { background:rgba(42,51,80,.3);color:var(--muted);border:1px solid var(--border); }
.b-win  { background:rgba(0,120,215,.12); color:#4da6ff; border:1px solid rgba(0,120,215,.2); }
.b-lin  { background:rgba(255,165,0,.1);  color:#ffb347; border:1px solid rgba(255,165,0,.2); }
.b-all  { background:rgba(74,84,116,.2);  color:var(--muted); border:1px solid var(--border); }
```

**Mapping stato Alpine → classe:**

| Stato | Classe badge | Classe LED |
|---|---|---|
| `completed` / `done` | `b-ok` | `led-ok` |
| `running` / `in_progress` | `b-run` | `led-run` |
| `pending` / `open` | `b-warn` | `led-warn` |
| `failed` / `error` | `b-err` | `led-err` |
| `skipped` | `b-skip` | `led-skip` |

### Progress bar
```css
.pbar      { height:3px; background:var(--border); border-radius:2px; overflow:hidden; }
.pbar-fill { height:100%; border-radius:2px; transition:width .5s ease; }
.pbar-run  { background:linear-gradient(90deg,var(--run),rgba(77,159,255,.5));
             background-size:200%;
             animation:shimmer 1.8s linear infinite; }
.pbar-ok   { background:var(--ok); }
.pbar-err  { background:var(--err); }

@keyframes shimmer {
  0%   { background-position:200% 0; }
  100% { background-position:-200% 0; }
}
```

### Tabelle
- `<th>`: `DM Mono 9px uppercase letter-spacing:1.2px color:var(--muted)`
- Righe pari: `background:rgba(255,255,255,.013)`
- Hover: `background:var(--surface-3)`
- Hostname: `DM Mono 12px font-weight:500 color:var(--text-bright)`
- Timestamp: `DM Mono 11px color:var(--muted)`

### Bottoni
```css
.btn { font-family:'IBM Plex Sans'; font-size:12px; font-weight:500;
       padding:6px 13px; border-radius:var(--r); border:none; cursor:pointer;
       display:inline-flex; align-items:center; gap:5px; transition:all .12s; }
.btn-primary { background:var(--accent); color:#fff; }
.btn-primary:hover { background:#5aaeff; box-shadow:0 0 14px var(--accent-glow); }
.btn-ghost   { background:transparent; color:var(--muted); border:1px solid var(--border); }
.btn-ghost:hover { background:var(--surface-3); color:var(--text); }
.btn-danger  { background:var(--err-dim); color:var(--err); border:1px solid rgba(255,71,87,.15); }
.btn-sm   { padding:3px 9px; font-size:11px; }
.btn-icon { padding:4px 7px; }
```

### Modale
- Backdrop: `rgba(0,0,0,.72)` + `backdrop-filter:blur(4px)`
- Header: `border-left:3px solid var(--accent)` sul lato sinistro

### Toast (sostituisce tutti gli `alert()`)

Aggiungere in cima al `<body>`:
```html
<div class="toast" x-show="toast.show" x-transition
     :class="`toast-${toast.type}`">
  <span class="led" :class="`led-${toast.type==='ok'?'ok':toast.type==='err'?'err':'warn'}`"></span>
  <span x-text="toast.msg"></span>
</div>
```

```css
.toast {
  position:fixed; bottom:22px; right:22px; z-index:200;
  display:flex; align-items:center; gap:10px;
  background:var(--surface-2); border:1px solid var(--border-2);
  padding:10px 16px; border-radius:var(--r2);
  font-size:12.5px; box-shadow:0 6px 28px rgba(0,0,0,.45);
  max-width:340px;
}
.toast-ok  { border-left:3px solid var(--ok); }
.toast-err { border-left:3px solid var(--err); }
.toast-warn{ border-left:3px solid var(--warn); }
```

Aggiungere in `function app()`:
```js
toast: { show:false, msg:'', type:'ok' },
notify(msg, type='ok') {
  this.toast = { show:true, msg, type };
  setTimeout(() => { this.toast.show = false; }, 3000);
},
```

Sostituire tutti gli `alert(...)` con `this.notify(...)`.

---

## Sezione: Dashboard

**4 stat cards** in griglia:
- `DEPLOY ATTIVI` — numero con `.led.led-run` a sinistra
- `COMPLETATI OGGI` — numero `--ok`
- `CR APERTE` — numero `--accent`
- `ERRORI 24H` — numero `--err`

Ogni card: `padding:14px 16px`, titolo `DM Mono 9.5px uppercase --muted`, numero `28px 700`.

**Due pannelli** affiancati:
- Sinistra: "Esecuzioni in corso" — per ogni `running`: hostname `DM Mono`, progress bar shimmer, `progress(a)%` + `ago(a.last_seen)`
- Destra: "Ultime CR" — lista 5 elementi con hostname, dominio, LED+badge stato

---

## Sezione: Deploy (`section='assignments'`)

Topbar action: `+ ASSEGNA`

Tabella: `HOST` | `WORKFLOW` | `STATO` | `PROGRESSO` | `ASSEGNATO` | `LAST SEEN` | ` `

Clic riga → apre modale dettaglio. Azioni: `◎` dettaglio + `✕` elimina.

### Modale dettaglio — Pipeline grafica

```
 ●────●────●────◉────○────○
 1    2    3    4    5    6
done done done run  pend pend
```

```html
<div class="pipeline">
  <template x-for="(s, i) in activeAssign?.steps || []" :key="s.step_id">
    <div style="display:flex;align-items:flex-start">
      <div class="p-node">
        <div class="p-circle"
             :class="s.status==='done'    ? 'p-done'
                   : s.status==='running' ? 'p-running'
                   : s.status==='error'   ? 'p-err'
                   : s.status==='skipped' ? 'p-skip' : 'p-pend'"
             x-text="s.ordine"></div>
        <div class="p-label" x-text="s.nome"></div>
        <div class="p-type"  x-text="s.tipo"></div>
      </div>
      <div class="p-line"
           x-show="i < (activeAssign?.steps||[]).length - 1"
           :class="s.status==='done' ? 'p-line-done' : 'p-line-pend'"></div>
    </div>
  </template>
</div>
```

```css
.pipeline   { display:flex; align-items:flex-start; padding:14px 0; overflow-x:auto; }
.p-node     { display:flex; flex-direction:column; align-items:center; gap:4px; flex-shrink:0; }
.p-circle   { width:28px; height:28px; border-radius:50%; display:flex;
              align-items:center; justify-content:center;
              font-family:'DM Mono',monospace; font-size:11px; border:2px solid; }
.p-done     { background:rgba(0,217,126,.15); border-color:var(--ok);   color:var(--ok); }
.p-running  { background:rgba(77,159,255,.15);border-color:var(--run);  color:var(--run);
              animation:pulse-circle 1.4s ease-in-out infinite; }
.p-pend     { background:var(--surface-3);   border-color:var(--border);color:var(--muted); }
.p-err      { background:rgba(255,71,87,.15);border-color:var(--err);   color:var(--err); }
.p-skip     { background:rgba(42,51,80,.3);  border-color:var(--very-muted);color:var(--muted); }
.p-label    { font-family:'DM Mono',monospace; font-size:9px; color:var(--muted);
              text-transform:uppercase; max-width:60px; text-align:center;
              overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.p-type     { font-size:8px; color:var(--very-muted); }
.p-line     { width:36px; height:2px; align-self:center; margin-bottom:22px; flex-shrink:0; }
.p-line-done{ background:var(--ok); }
.p-line-pend{ background:var(--border); }

@keyframes pulse-circle {
  0%,100% { box-shadow:0 0 0 0 rgba(77,159,255,.4); }
  50%     { box-shadow:0 0 0 4px rgba(77,159,255,0); }
}
```

Sotto la pipeline: card con dettaglio dello step in `running` (nome, tipo, output log se presente).

---

## Sezione: Workflow (`section='workflows'`)

Lista espandibile — ogni workflow è una riga card:
```
◆ Nome Workflow    v1.2 · 9 step · 3 attive    [✏ Step] [▶ Assegna] [✕]
```
Click nome → espande lista step inline.

Step nella lista espansa: `[ordine] [tag tipo] Nome step [badge platform] [✕]`

---

## Sezione: Change Request (`section='cr'`)

Topbar action: `+ NUOVA CR`

Tabella: `#` | `HOST` | `DOMINIO` | `STATO` | `WORKFLOW` | `SOFTWARE` | `CREATA` | ` `

- `#`: `DM Mono --muted 11px`
- `SOFTWARE`: `<span class="tag">N pkg</span>`
- Azioni: `▶` start (solo se `open`), `✓` complete, `✕` elimina

---

## Sezione: Impostazioni (`section='settings'`)

Tre card separate `max-width:480px`:

**Card 1 — Workflow Default:** `<select>` + Salva

**Card 2 — Installer Agent (NUOVO):**
```html
<a class="installer-link" href="/api/download/agent-install.ps1">
  ↓  agent-install.ps1  <span class="badge b-win">Windows</span>
</a>
<a class="installer-link" href="/api/download/agent-install.sh">
  ↓  agent-install.sh   <span class="badge b-lin">Linux</span>
</a>
```
Stile: `DM Mono`, background `--surface-3`, border `--border`, hover → `--accent`.

**Card 3 — Server:** versione API (da `GET /api/version`), health LED, path DB, stato rate limit.

---

## Animazioni globali

```css
/* Fade-in cambio sezione */
.content { animation: section-in .18s ease; }
@keyframes section-in {
  from { opacity:0; transform:translateY(4px); }
  to   { opacity:1; transform:none; }
}

/* Scanline decorativa topbar */
.topbar::after {
  content:''; position:absolute; inset:0; pointer-events:none;
  background:repeating-linear-gradient(
    0deg, transparent, transparent 3px,
    rgba(255,255,255,.008) 3px, rgba(255,255,255,.008) 4px
  );
}
```

---

## Cosa NON modificare

- `function app()` — copiare identica da `server/web/index.html` attuale
- Tutti gli endpoint API usati (`/api/workflows`, `/api/pc-workflows`, `/api/cr`, ecc.)
- `setInterval(() => this.loadAll(), 5000)` — polling ogni 5s invariato
- Tutte le variabili Alpine: `crForm`, `stepForm`, `assignForm`, `activeWf`, `activeAssign`
- Helper functions: `defaultParams(tipo)`, `progress(a)`, `ago(iso)`, `statusColor(s)`

---

## Checklist prima del commit

- [ ] `79 passed` su `cd server && NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v`
- [ ] Font DM Mono visibile su hostname e badge tipo (Network tab → Google Fonts loaded)
- [ ] LED pulsante animato sui deploy `running`
- [ ] Pipeline grafica nel modale dettaglio deploy
- [ ] Toast al posto di tutti gli `alert()`
- [ ] Card installer in Settings con link `.ps1` e `.sh`
- [ ] Nessuna emoji nel markup — solo `◆ ▶ ✕ ✓ ○ ●`

---

## Commit suggerito

```
feat: redesign UI enterprise "Deploy War Room" (v1.7.9)

- Font IBM Plex Sans (UI) + DM Mono (hostname/codice/timestamp)
- Pipeline grafica step-by-step nel modale dettaglio deploy
- LED animati pulse + glow per stato running
- Toast notifications al posto di alert()
- Progress bar 3px con shimmer animato
- Card installer in Settings (PS1 + SH download)
- Tabelle alta densità, header DM Mono uppercase
- Sidebar con cerchi ●/○ al posto di emoji
- Scanline decorativa su topbar
- Palette #080b12 industriale
```
