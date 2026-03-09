# NovaSCM — Specifica Redesign UI Enterprise
## Per Claude Code — `server/web/index.html`

> **Obiettivo:** Sostituire l'attuale `server/web/index.html` con una nuova interfaccia
> enterprise-grade. Il file deve rimanere un singolo `index.html` autocontenuto
> (Alpine.js CDN, tutto inline). Nessuna dipendenza nuova tranne i font Google e
> Alpine.js già usato.  
> **Tutta la logica API (`app()`, metodi `api()`, `loadAll()`, ecc.) va mantenuta
> identica** — si ridisegna solo il markup e i CSS.

---

## 1 · Direzione estetica: "Deploy War Room"

L'interfaccia non è una SaaS dashboard generica. È una **sala controllo operativa**
per IT ops — tono industriale, alta densità informativa, feedback visivo in tempo reale.

Ispirazione: pannelli SCADA, console CI/CD di datacenter, terminali IPMI.

**Design pillars:**
- **Griglia stretta, alta densità** — ogni pixel porta informazione
- **Tipografia mista** — `DM Mono` per hostname/comandi/output, `IBM Plex Sans` per UI
- **Colore come segnale** — nessun colore decorativo; ogni colore = uno stato operativo
- **Animazioni funzionali** — solo pulse su "running", scanline su header attivi
- **Nessun emoji** nel markup — sostituire con SVG inline minimalisti o caratteri Unicode
  geometrici (▶ ■ ◆ ● ○ ✕ — ammessi)

---

## 2 · Design System

### 2.1 Font (Google Fonts CDN)
```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=DM+Mono:wght@400;500&display=swap" rel="stylesheet">
```
- UI text: `'IBM Plex Sans', sans-serif`
- Hostname, comandi, output, badge tipo: `'DM Mono', monospace`

### 2.2 Palette CSS (`:root`)
```css
:root {
  /* Surface */
  --bg:          #0a0c14;   /* sfondo principale — quasi nero */
  --surface:     #0f1220;   /* card/panel base */
  --surface-2:   #141728;   /* card elevata */
  --surface-3:   #1a1f33;   /* hover */
  --border:      #1e2540;   /* bordi standard */
  --border-2:    #263058;   /* bordi attivi */

  /* Testo */
  --text:        #c8d0e8;   /* corpo */
  --text-bright: #e8ecf8;   /* titoli */
  --muted:       #5a6480;   /* secondario */
  --very-muted:  #333a55;   /* placeholder, divider label */

  /* Stato operativo */
  --ok:          #00d97e;   /* completed / success */
  --ok-dim:      rgba(0,217,126,.12);
  --running:     #4d9fff;   /* running / active */
  --running-dim: rgba(77,159,255,.12);
  --warn:        #ffaa00;   /* pending / attenzione */
  --warn-dim:    rgba(255,170,0,.12);
  --error:       #ff4757;   /* failed / error */
  --error-dim:   rgba(255,71,87,.12);
  --skip:        #3d4a70;   /* skipped */

  /* Accento navigazione */
  --accent:      #4d9fff;
  --accent-glow: rgba(77,159,255,.25);

  /* Dimensioni */
  --radius:      4px;       /* bordi quadrati — look industriale */
  --radius-lg:   6px;
  --sidebar-w:   200px;
  --topbar-h:    48px;
}
```

### 2.3 Stato → colore mapping
| Stato | Colore | Classe badge |
|---|---|---|
| `completed` / `done` | `--ok` | `.s-ok` |
| `running` | `--running` | `.s-running` |
| `pending` / `open` | `--warn` | `.s-warn` |
| `failed` / `error` | `--error` | `.s-err` |
| `skipped` | `--skip` | `.s-skip` |
| `in_progress` | `--running` | `.s-running` |

---

## 3 · Layout Generale

```
┌─────────────────────────────────────────────────────────────┐
│ SIDEBAR (200px)  │  TOPBAR (48px) — breadcrumb + actions    │
│                  │─────────────────────────────────────────  │
│  [logo]          │                                           │
│  ─────           │   CONTENT AREA (scroll verticale)        │
│  ● Dashboard     │                                           │
│  ○ Deploy        │                                           │
│  ○ Workflow      │                                           │
│  ○ Change Req    │                                           │
│  ○ Impostazioni  │                                           │
│                  │                                           │
│  ─────           │                                           │
│  [status dot]    │                                           │
│  v1.7.8          │                                           │
└─────────────────────────────────────────────────────────────┘
```

**Sidebar:** sfondo `--bg`, bordo destro `--border`. Nessuna icona emoji — usare
piccoli cerchi `●` / `○` come indicatori di sezione attiva. Logo: `NOVA` in
`DM Mono 700` colore `--accent` + `SCM` in `IBM Plex Sans 400` `--muted`.

**Topbar:** sfondo `--surface`, border-bottom `--border`. Sinistra: breadcrumb
semplice (`Dashboard`, `Deploy`, ecc.). Destra: dot pulsante live status + ora ultimo
aggiornamento + eventuale pulsante azione primario della sezione.

---

## 4 · Componenti condivisi

### 4.1 Status LED
Piccolo cerchio con box-shadow colorato. Usare ovunque al posto dei badge di stato.
```css
.led { width:8px; height:8px; border-radius:50%; flex-shrink:0; }
.led-ok      { background:var(--ok);      box-shadow:0 0 6px var(--ok); }
.led-running { background:var(--running); box-shadow:0 0 6px var(--running);
               animation: pulse-led 1.4s ease-in-out infinite; }
.led-warn    { background:var(--warn);    box-shadow:0 0 5px var(--warn); }
.led-err     { background:var(--error);   box-shadow:0 0 5px var(--error); }
.led-skip    { background:var(--skip); }

@keyframes pulse-led {
  0%,100% { opacity:1; transform:scale(1); }
  50%     { opacity:.6; transform:scale(1.3); }
}
```

### 4.2 Badge stato testuale
```css
.badge { display:inline-flex; align-items:center; gap:5px;
         padding:2px 7px; border-radius:var(--radius);
         font-family:'DM Mono',monospace; font-size:10.5px; font-weight:500;
         letter-spacing:.3px; text-transform:uppercase; }
.s-ok   { background:var(--ok-dim);      color:var(--ok);      border:1px solid rgba(0,217,126,.25); }
.s-running { background:var(--running-dim); color:var(--running); border:1px solid rgba(77,159,255,.25); }
.s-warn { background:var(--warn-dim);    color:var(--warn);    border:1px solid rgba(255,170,0,.25); }
.s-err  { background:var(--error-dim);   color:var(--error);   border:1px solid rgba(255,71,87,.25); }
.s-skip { background:rgba(61,74,112,.2); color:var(--skip);    border:1px solid var(--border); }
```

### 4.3 Progress bar con stato
```css
.pbar { height:3px; background:var(--border); border-radius:2px; overflow:hidden; }
.pbar-fill { height:100%; border-radius:2px; transition:width .4s ease; }
.pbar-running  { background:var(--running); }
.pbar-ok       { background:var(--ok); }
.pbar-err      { background:var(--error); }
```

### 4.4 Tabella dati
Header `font-family:DM Mono, 10px, uppercase, --very-muted`. Righe zebra: righe
pari `background:rgba(255,255,255,.015)`. Hover riga: `background:var(--surface-3)`.
Nessun border-bottom pesante — solo `1px solid var(--border)` sottile.

### 4.5 Bottoni
```css
.btn         { font-family:'IBM Plex Sans'; font-size:12.5px; font-weight:500;
               padding:6px 14px; border-radius:var(--radius); border:none;
               cursor:pointer; display:inline-flex; align-items:center; gap:6px;
               transition:background .12s, box-shadow .12s; }
.btn-primary { background:var(--running); color:#fff; }
.btn-primary:hover { background:#5aaeff; box-shadow:0 0 12px var(--accent-glow); }
.btn-ghost   { background:transparent; color:var(--muted);
               border:1px solid var(--border); }
.btn-ghost:hover { background:var(--surface-3); color:var(--text); }
.btn-danger  { background:var(--error-dim); color:var(--error);
               border:1px solid rgba(255,71,87,.2); }
.btn-sm      { padding:4px 10px; font-size:11.5px; }
```

### 4.6 Modale
Stessa struttura attuale ma con:
- backdrop `rgba(0,0,0,.75)` + `backdrop-filter:blur(3px)`
- modal width `520px`, max `95vw`
- header con linea accent sinistra `border-left:3px solid var(--accent)` sulla sezione titolo
- Nessun emoji nel titolo modale

### 4.7 Toast notification (nuovo — sostituisce `alert()`)
**Aggiungere questo componente** in cima al body, gestito da Alpine:
```html
<div class="toast-container" x-show="toast.show" x-transition:enter="toast-in"
     x-transition:leave="toast-out" :class="`toast-${toast.type}`">
  <span class="led" :class="`led-${toast.type==='ok'?'ok':toast.type==='err'?'err':'warn'}`"></span>
  <span x-text="toast.msg"></span>
</div>
```
```css
.toast-container { position:fixed; bottom:20px; right:20px; z-index:200;
  display:flex; align-items:center; gap:10px;
  background:var(--surface-2); border:1px solid var(--border-2);
  padding:10px 16px; border-radius:var(--radius-lg);
  font-size:13px; box-shadow:0 4px 24px rgba(0,0,0,.4);
  max-width:360px; }
```
Nel `app()` Alpine aggiungere:
```js
toast: { show:false, msg:'', type:'ok' },
notify(msg, type='ok') {
  this.toast = { show:true, msg, type };
  setTimeout(() => { this.toast.show = false; }, 3000);
},
```
Sostituire tutti gli `alert(...)` con `this.notify(...)`.

---

## 5 · Sezioni

### 5.1 Dashboard

Layout: **3 stat cards** in cima (non 4), poi due pannelli affiancati.

**Stat cards** (griglia 3 colonne):
- `DEPLOY ATTIVI` — numero con LED running animato a sinistra del numero
- `COMPLETATI OGGI` — numero verde
- `CR APERTE` — numero blu

Ogni card: titolo in `DM Mono 10px uppercase --very-muted`, numero `32px IBM Plex Sans 600`,
nessun padding eccessivo — card compatta `padding:14px 16px`.

**Pannello sinistro — "Esecuzioni in corso":**  
Per ogni deploy in running: mostrare hostname in `DM Mono 600`, workflow nome, barra
progress `3px`, percentuale + "last seen Xs fa". Se nessuno: messaggio centrato
`NESSUN DEPLOY ATTIVO` in `DM Mono --very-muted 11px uppercase`.

**Pannello destro — "Ultime Change Request":**  
Lista compatta (max 6). Ogni riga: hostname `DM Mono`, dominio `--muted`, LED stato,
data creazione `--very-muted`. Bordo bottom `--border`.

---

### 5.2 Deploy (sezione `assignments`, rinominata visivamente)

**Topbar azione:** pulsante `+ ASSEGNA` (primary).

**Tabella assegnazioni** con colonne:
`HOST` | `WORKFLOW` | `STATO` | `PROGRESSO` | `ASSEGNATO` | `LAST SEEN` | `·`

- `HOST`: `DM Mono 500 --text-bright`
- `WORKFLOW`: `IBM Plex Sans --muted`
- `STATO`: badge con LED + testo
- `PROGRESSO`: progress bar `3px` + percentuale `DM Mono 11px`
- `ASSEGNATO` / `LAST SEEN`: timestamp `DM Mono 11px --muted`
- `·`: icona dettaglio (cerchio con punto) + icona elimina (✕)

**Dettaglio deploy (modale)** — redesign completo:

```
┌─────────────────────────────────────────────────────────────┐
│ ◆ HOSTNAME-PC01                     [badge running] [✕]    │
│ ─────────────────────────────────────────────────────────── │
│ Workflow: Nome Workflow  ·  Avviato: 3m fa  ·  last seen: 8s│
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  72%    │
│                                                             │
│ PIPELINE                                                    │
│  ●─────────●─────────●──────────●──────────○               │
│  1         2         3          4           5              │
│  done      done      running    pending     pending        │
│                                                             │
│ ┌─ Step 3 — Installa Chrome ──────────────────────────────┐ │
│ │ tipo: winget_install    ●  running                      │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

**Pipeline grafica:** `<div class="pipeline">` con cerchi connessi da linea.
Ogni nodo: cerchio `20px` colorato per stato, numero dentro, tooltip con nome step
al hover. Nodo `running` con animazione pulse. Linea di connessione `2px solid --border`
che diventa `--ok` per i tratti già completati.

CSS pipeline:
```css
.pipeline { display:flex; align-items:center; margin:16px 0; overflow-x:auto; }
.pipeline-node { display:flex; flex-direction:column; align-items:center; gap:4px;
                 flex-shrink:0; }
.pipeline-circle { width:28px; height:28px; border-radius:50%; display:flex;
                   align-items:center; justify-content:center;
                   font-family:'DM Mono'; font-size:11px; font-weight:500;
                   border:2px solid; }
.pipeline-line { flex:1; height:2px; min-width:24px; }
.pipeline-label { font-family:'DM Mono'; font-size:9px; text-transform:uppercase;
                  color:var(--muted); max-width:60px; text-align:center;
                  overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
```

Sotto la pipeline: lista step verticale con output log (se disponibile).

---

### 5.3 Workflow

**Layout a lista** (non grid di card) — ogni workflow è una riga espandibile.

Riga collassata:
```
◆ Nome Workflow                v2.4  ·  8 step  ·  2 assegnazioni attive   [Step] [▶ Assegna] [✕]
```

Riga espansa (click su nome):
- Lista step in formato pipeline orizzontale (stessa grafica del dettaglio deploy
  ma con cerchi colorati per platform: blu=windows, arancio=linux, grigio=all)
- Ogni step: ordine, nome, tipo (monospace), platform badge

**Editor step (modale):**
- Titolo: `STEP — NomeWorkflow`
- Lista step esistenti con drag-handle `⠿` (solo visivo per ordine futuro)
- Ogni step: `[ordine] [tipo badge] Nome step [platform] [✕]`
- Form aggiungi step: layout 2 colonne per campi brevi, 1 colonna per textarea JSON
- Il campo parametri JSON con font `DM Mono` e altezza `auto` che cresce

---

### 5.4 Change Request (sezione `cr`)

**Topbar:** `+ NUOVA CR`

**Tabella** con colonne:
`#` | `HOST` | `DOMINIO` | `STATO` | `WORKFLOW` | `SOFTWARE` | `CREATA` | `·`

- Colonna `#`: `DM Mono --muted 11px`
- Colonna `SOFTWARE`: badge compatto `N pkg`
- Azioni inline: `▶ start`, `✓ done`, `✕`

**Modale nuova CR:**
- Grid 2 colonne per la maggior parte dei campi
- Campo `NOME PC` in `DM Mono` uppercase automatico
- Sezione "Dominio AD" collassabile (se non si fa join al dominio)

---

### 5.5 Impostazioni

Layout card singola `max-width:480px`:
- `WORKFLOW DEFAULT` — select + salva
- Sezione `INSTALLER` — due link cliccabili per scaricare i PS1/SH dinamici:
  ```
  ↓ agent-install.ps1    ↓ agent-install.sh
  ```
  Piccoli `<a href="/api/download/agent-install.ps1">` styled come `btn-ghost btn-sm`
  con font `DM Mono`
- Sezione `SERVER` — mostra versione API (`/api/version`), stato health

---

## 6 · Micro-interazioni e animazioni

### 6.1 Scanline header (solo decorativa)
Sul `topbar` e sulle intestazioni di sezione attive:
```css
.topbar::after {
  content:'';
  position:absolute; inset:0; pointer-events:none;
  background:repeating-linear-gradient(
    0deg, transparent, transparent 2px,
    rgba(255,255,255,.012) 2px, rgba(255,255,255,.012) 4px
  );
}
```

### 6.2 Fade-in contenuto
Ogni sezione `.content` al cambio:
```css
.content { animation: section-in .18s ease; }
@keyframes section-in { from { opacity:0; transform:translateY(4px); } to { opacity:1; transform:none; } }
```

### 6.3 Tabelle — riga nuova
Quando `loadAll()` aggiunge una riga non presente prima, la riga appare con
`background:rgba(77,159,255,.06)` per 1.5s poi torna normale.
*(Opzionale: implementare se non complica troppo la logica Alpine)*

### 6.4 Live indicator (topbar destra)
```html
<span class="live-dot" :class="liveOk ? 'live-ok' : 'live-err'"></span>
<span class="live-label" x-text="lastUpdate"></span>
```
```css
.live-dot { width:6px; height:6px; border-radius:50%; display:inline-block; }
.live-ok  { background:var(--ok); animation:pulse-led 2s ease-in-out infinite; }
.live-err { background:var(--error); }
.live-label { font-family:'DM Mono'; font-size:11px; color:var(--muted); }
```

---

## 7 · Cosa NON cambiare

- **Tutta la logica JavaScript** nel `function app()` — portarla identica
- **Gli endpoint API** usati (`/api/workflows`, `/api/pc-workflows`, ecc.)
- **Il polling** `setInterval(() => this.loadAll(), 5000)`
- **Alpine.js** — mantenere `x-model`, `x-show`, `x-for`, `@click` ecc.
- **La struttura modale** per nuova CR, step editor, assign — ridisegnare l'HTML
  ma mantenere le variabili Alpine (`crForm`, `stepForm`, `assignForm`, ecc.)
- **`defaultParams(tipo)`** e tutti gli helper (`progress()`, `ago()`, `statusColor()`)

---

## 8 · Struttura file finale

```
server/web/index.html   ← unico file, tutto inline
```

Ordine nel file:
1. `<head>` — meta, font Google, `<style>` con tutto il CSS
2. `<body x-data="app()">` — HTML struttura
3. `<script>` — `function app() { return { ... } }` con tutta la logica
4. `<style>[x-cloak]{display:none!important}</style>` alla fine

---

## 9 · Checklist prima del commit

- [ ] `79 passed` su `pytest tests/ -v` (nessun test server toccato)  
- [ ] `GET /` risponde 200 con il nuovo HTML  
- [ ] Dashboard mostra stat cards e sezione esecuzioni in corso  
- [ ] Dettaglio deploy mostra pipeline grafica con cerchi  
- [ ] Nessun `alert()` — tutti sostituiti con `this.notify()`  
- [ ] Font `DM Mono` visibile su hostname e badge tipo  
- [ ] LED pulsante sui deploy `running`  
- [ ] Mobile: sidebar collassa sopra `768px` (display:none, toggle burger)  

---

## 10 · Commit suggerito

```
feat: redesign UI enterprise "Deploy War Room" (v1.7.9)

- Palette scura industriale, font IBM Plex Sans + DM Mono
- Pipeline grafica step-by-step nel dettaglio deploy
- LED animati per stato running
- Toast notifications al posto di alert()
- Tabelle ad alta densità, progress bar 3px
- Sezione installer in Settings con link download
- Sidebar con indicatori ● / ○ al posto di emoji
- Scanline decorativa su topbar
```
