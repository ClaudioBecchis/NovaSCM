# NovaSCM — Deploy Floor: Vista PC in fase di Deploy
> **Istruzioni per Claude Code** — aggiungi la sezione "Deploy Floor" a `server/web/index.html`

---

## Obiettivo

Aggiungere una **nuova sezione** `section='floor'` all'interfaccia esistente.
Mostra tutti i PC in fase di deploy come card live con step-track, pipeline e log stream.
Nessuna nuova API necessaria — usa `GET /api/pc-workflows` già presente.

---

## 1 · Cosa aggiungere alla sidebar

Inserire dopo la voce "Deploy" nel nav:

```html
<div class="nav-item" @click="section='floor'" :class="section==='floor' ? 'active' : ''">
  <span class="nav-dot"></span>Deploy Floor
</div>
```

---

## 2 · CSS aggiuntivo (aggiungere al `<style>` esistente)

```css
/* ── Deploy Floor ── */
.floor-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 12px;
}
.floor-grid.view-list {
  grid-template-columns: 1fr;
}

/* PC Card */
.pc-card {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
  cursor: pointer;
  transition: border-color .15s, transform .1s;
}
.pc-card:hover { border-color: var(--border-2); transform: translateY(-1px); }
.pc-card.pc-selected { border-color: var(--accent); box-shadow: 0 0 0 1px rgba(77,159,255,.15); }
.pc-card.pc-running   { border-left: 3px solid var(--running); }
.pc-card.pc-completed { border-left: 3px solid var(--ok); }
.pc-card.pc-failed    { border-left: 3px solid var(--error); }
.pc-card.pc-pending   { border-left: 3px solid var(--warn); }

.card-top {
  padding: 11px 13px 9px;
  display: flex; align-items: flex-start; justify-content: space-between;
}
.card-host {
  font-family: 'DM Mono', monospace;
  font-size: 13px; font-weight: 500; color: var(--text-bright); line-height: 1;
}
.card-wf {
  font-size: 11px; color: var(--muted); margin-top: 3px;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 185px;
}

.card-prog { padding: 0 13px 8px; }
.card-meta {
  display: flex; align-items: center; justify-content: space-between;
  font-family: 'DM Mono', monospace; font-size: 10px; color: var(--muted);
  margin-top: 4px;
}

/* Step track — riga di pallini */
.step-track { padding: 0 13px 9px; display: flex; gap: 3px; }
.step-dot {
  flex: 1; height: 4px; border-radius: 2px; transition: background .3s;
}
.step-dot.done    { background: var(--ok); }
.step-dot.running { background: var(--running); animation: pulse-bar 1.2s ease-in-out infinite; }
.step-dot.error   { background: var(--error); }
.step-dot.skipped { background: var(--skip, #2a3350); }
.step-dot.pending { background: var(--border); }
@keyframes pulse-bar {
  0%,100% { opacity: 1; } 50% { opacity: .35; }
}

/* Step corrente */
.card-cur {
  padding: 7px 13px;
  border-top: 1px solid var(--border);
  background: var(--sidebar);
  font-family: 'DM Mono', monospace; font-size: 10.5px;
  display: flex; align-items: center; gap: 7px;
}
.cur-icon {
  width: 16px; height: 16px; border-radius: 50%;
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0; font-size: 9px;
}
.cur-icon.run  { background: var(--run-dim); border: 1px solid rgba(77,159,255,.3); color: var(--running); animation: spin-glow 2s linear infinite; }
.cur-icon.ok   { background: var(--ok-dim);  border: 1px solid rgba(0,217,126,.3);  color: var(--ok); }
.cur-icon.err  { background: var(--err-dim); border: 1px solid rgba(255,71,87,.3);  color: var(--error); }
.cur-icon.pend { background: var(--card2);   border: 1px solid var(--border);       color: var(--muted); }
@keyframes spin-glow {
  0%,100% { box-shadow: 0 0 0 0 rgba(77,159,255,.3); }
  50%     { box-shadow: 0 0 0 3px rgba(77,159,255,0); }
}
.cur-name { color: var(--text); flex: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.cur-tipo { color: var(--muted); flex-shrink: 0; font-size: 10px; }

/* ── Side Panel ── */
.floor-layout { display: flex; gap: 0; height: 100%; }
.floor-main   { flex: 1; overflow-y: auto; padding: 20px 24px; }
.floor-side {
  width: 360px; min-width: 360px;
  background: var(--sidebar); border-left: 1px solid var(--border);
  display: flex; flex-direction: column; overflow: hidden;
  transition: width .2s ease, min-width .2s ease;
}
.floor-side.closed { width: 0; min-width: 0; }

.fside-head {
  padding: 13px 16px; border-bottom: 1px solid var(--border); flex-shrink: 0;
  display: flex; align-items: flex-start; justify-content: space-between;
}
.fside-host { font-family: 'DM Mono', monospace; font-size: 15px; font-weight: 500; color: var(--text-bright); }
.fside-wf   { font-size: 11px; color: var(--muted); margin-top: 2px; }

.fside-meta {
  padding: 8px 16px; border-bottom: 1px solid var(--border); flex-shrink: 0;
  display: flex; gap: 0;
}
.fmeta-item {
  flex: 1; padding: 0 8px; border-right: 1px solid var(--border);
  font-family: 'DM Mono', monospace; font-size: 9px; color: var(--muted);
  text-transform: uppercase; letter-spacing: .8px;
}
.fmeta-item:first-child { padding-left: 0; }
.fmeta-item:last-child  { border-right: none; }
.fmeta-val { font-size: 11px; color: var(--text); margin-top: 2px; display: block; letter-spacing: 0; text-transform: none; }

.fside-prog { padding: 12px 16px; border-bottom: 1px solid var(--border); flex-shrink: 0; }
.fp-row { display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; }
.fp-lbl { font-family: 'DM Mono', monospace; font-size: 9.5px; color: var(--muted); text-transform: uppercase; letter-spacing: 1px; }
.fp-pct { font-family: 'DM Mono', monospace; font-size: 13px; font-weight: 500; color: var(--running); }

/* Pipeline full nel side */
.fside-pipeline { padding: 12px 16px; border-bottom: 1px solid var(--border); flex-shrink: 0; overflow-x: auto; }
.fp-title { font-family: 'DM Mono', monospace; font-size: 9px; color: var(--muted); text-transform: uppercase; letter-spacing: 1.5px; margin-bottom: 10px; }

/* Log stream */
.fside-log { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.flog-head {
  padding: 8px 16px; border-bottom: 1px solid var(--border); flex-shrink: 0;
  display: flex; align-items: center; justify-content: space-between;
}
.flog-title { font-family: 'DM Mono', monospace; font-size: 9px; color: var(--muted); text-transform: uppercase; letter-spacing: 1.5px; }
.flog-body {
  flex: 1; overflow-y: auto; padding: 10px 14px;
  font-family: 'DM Mono', monospace; font-size: 10.5px; line-height: 1.7;
}
.flog-line { display: flex; gap: 10px; align-items: baseline; }
.flog-ts   { color: var(--very-muted, #272e48); flex-shrink: 0; font-size: 9.5px; }
.flog-ok   { color: var(--ok); }
.flog-err  { color: var(--error); }
.flog-warn { color: var(--warn); }
.flog-run  { color: var(--running); }
.flog-dim  { color: var(--muted); }
.flog-new  { animation: log-in .2s ease; }
@keyframes log-in { from { opacity:0; transform:translateX(-4px); } to { opacity:1; transform:none; } }

/* Toolbar floor */
.floor-toolbar {
  display: flex; align-items: center; gap: 8px;
  margin-bottom: 16px;
}
.floor-filter-chip {
  padding: 3px 9px; border-radius: 3px; cursor: pointer;
  border: 1px solid var(--border); background: transparent;
  font-family: 'DM Mono', monospace; font-size: 9.5px;
  text-transform: uppercase; letter-spacing: .8px; color: var(--muted);
  transition: all .12s;
}
.floor-filter-chip.on { background: var(--run-dim); border-color: rgba(77,159,255,.3); color: var(--running); }
.floor-filter-chip:hover:not(.on) { background: var(--card2); color: var(--text); }
.floor-view-btn {
  padding: 3px 9px; border-radius: 3px; cursor: pointer;
  border: 1px solid var(--border); background: transparent;
  font-family: 'DM Mono', monospace; font-size: 9.5px;
  text-transform: uppercase; letter-spacing: .8px; color: var(--muted);
  transition: all .12s;
}
.floor-view-btn.on { background: var(--card2); color: var(--text); }
```

---

## 3 · HTML della sezione (inserire insieme alle altre sezioni)

```html
<!-- ════════════ DEPLOY FLOOR ════════════ -->
<div class="content" x-show="section==='floor'" style="padding:0;height:100%;overflow:hidden">
  <div class="floor-layout" style="height:100%">

    <!-- Lista PC -->
    <div class="floor-main">

      <!-- Toolbar -->
      <div class="floor-toolbar">
        <button class="floor-view-btn" :class="floorView==='grid'?'on':''"
                @click="floorView='grid'">Grid</button>
        <button class="floor-view-btn" :class="floorView==='list'?'on':''"
                @click="floorView='list'">Lista</button>
        <div style="width:1px;height:20px;background:var(--border);margin:0 4px"></div>
        <span style="font-family:'DM Mono',monospace;font-size:9px;color:var(--muted);text-transform:uppercase;letter-spacing:1px">Filtro:</span>
        <template x-for="f in ['all','running','pending','completed','failed']" :key="f">
          <button class="floor-filter-chip" :class="floorFilter===f?'on':''"
                  @click="floorFilter=f" x-text="f==='all'?'Tutti':f"></button>
        </template>
        <span class="muted" style="margin-left:auto;font-family:'DM Mono',monospace;font-size:10px"
              x-text="floorFiltered().length + ' PC'"></span>
      </div>

      <!-- Grid card -->
      <div :class="'floor-grid' + (floorView==='list'?' view-list':'')">
        <template x-for="a in floorFiltered()" :key="a.id">
          <div class="pc-card"
               :class="`pc-${a.status}` + (floorSel?.id===a.id?' pc-selected':'')"
               @click="floorSelect(a)">

            <!-- Top -->
            <div class="card-top">
              <div>
                <div class="card-host" x-text="a.pc_name"></div>
                <div class="card-wf" x-text="a.workflow_nome"></div>
              </div>
              <span class="badge" :class="floorBadge(a.status)">
                <span class="led" :class="floorLed(a.status)"></span>
                <span x-text="a.status"></span>
              </span>
            </div>

            <!-- Progress -->
            <div class="card-prog">
              <div class="pbar">
                <div class="pbar-fill"
                     :class="a.status==='completed'?'pbar-ok':a.status==='failed'?'pbar-err':'pbar-run'"
                     :style="`width:${progress(a)}%`"></div>
              </div>
              <div class="card-meta">
                <span :style="a.status==='completed'?'color:var(--ok)':a.status==='failed'?'color:var(--error)':'color:var(--running)'"
                      x-text="progress(a)+'%'"></span>
                <span x-text="a.steps ? `step ${(a.steps||[]).filter(s=>s.status==='done'||s.status==='skipped').length}/${a.steps.length}` : '—'"></span>
                <span x-text="ago(a.last_seen)"></span>
              </div>
            </div>

            <!-- Step track -->
            <div class="step-track" x-show="(a.steps||[]).length > 0">
              <template x-for="s in (a.steps||[])" :key="s.step_id">
                <div class="step-dot" :class="s.status"></div>
              </template>
            </div>

            <!-- Step corrente -->
            <div class="card-cur">
              <div class="cur-icon"
                   :class="a.status==='running'?'run':a.status==='completed'?'ok':a.status==='failed'?'err':'pend'">
                <span x-text="a.status==='running'?'▶':a.status==='completed'?'✓':a.status==='failed'?'✕':'○'"></span>
              </div>
              <div class="cur-name"
                   x-text="floorCurStep(a)?.nome || (a.status==='pending'?'In attesa agent':'—')"></div>
              <div class="cur-tipo"
                   x-text="floorCurStep(a)?.tipo || ''"></div>
            </div>

          </div>
        </template>
      </div>

    </div>

    <!-- Side Panel -->
    <div class="floor-side" :class="floorSel ? '' : 'closed'">
      <template x-if="floorSel">
        <div style="display:flex;flex-direction:column;height:100%;overflow:hidden">

          <!-- Head -->
          <div class="fside-head">
            <div>
              <div class="fside-host" x-text="floorSel.pc_name"></div>
              <div class="fside-wf"   x-text="floorSel.workflow_nome"></div>
            </div>
            <div style="display:flex;align-items:center;gap:8px">
              <span class="badge" :class="floorBadge(floorSel.status)">
                <span class="led" :class="floorLed(floorSel.status)"></span>
                <span x-text="floorSel.status"></span>
              </span>
              <button class="btn btn-ghost btn-icon" @click="floorSel=null">✕</button>
            </div>
          </div>

          <!-- Meta -->
          <div class="fside-meta">
            <div class="fmeta-item">Workflow<span class="fmeta-val" x-text="floorSel.workflow_nome"></span></div>
            <div class="fmeta-item">Assegnato<span class="fmeta-val" x-text="ago(floorSel.assigned_at)"></span></div>
            <div class="fmeta-item">Last seen<span class="fmeta-val" x-text="ago(floorSel.last_seen)"></span></div>
          </div>

          <!-- Progress -->
          <div class="fside-prog">
            <div class="fp-row">
              <span class="fp-lbl">Progresso</span>
              <span class="fp-pct"
                    :style="floorSel.status==='completed'?'color:var(--ok)':floorSel.status==='failed'?'color:var(--error)':''"
                    x-text="progress(floorSel)+'%'"></span>
            </div>
            <div class="pbar" style="height:4px">
              <div class="pbar-fill"
                   :class="floorSel.status==='completed'?'pbar-ok':floorSel.status==='failed'?'pbar-err':'pbar-run'"
                   :style="`width:${progress(floorSel)}%`"></div>
            </div>
          </div>

          <!-- Pipeline -->
          <div class="fside-pipeline">
            <div class="fp-title">Pipeline</div>
            <div style="display:flex;align-items:flex-start;overflow-x:auto">
              <template x-for="(s, i) in (floorSel.steps||[])" :key="s.step_id">
                <div style="display:flex;align-items:flex-start">
                  <div class="p-node">
                    <div class="p-circle"
                         :class="s.status==='done'?'done':s.status==='running'?'running':s.status==='error'?'error':s.status==='skipped'?'skipped':'pending'"
                         x-text="s.ordine"></div>
                    <div class="p-label" x-text="s.nome"></div>
                    <div class="p-type"  x-text="s.tipo"></div>
                  </div>
                  <div class="p-line"
                       x-show="i < (floorSel.steps||[]).length - 1"
                       :class="s.status==='done'?'p-line-done':'p-line-pend'"></div>
                </div>
              </template>
            </div>
          </div>

          <!-- Log -->
          <div class="fside-log">
            <div class="flog-head">
              <span class="flog-title">Log Agent</span>
              <span class="muted" style="font-family:'DM Mono',monospace;font-size:9px;cursor:pointer"
                    @click="floorLog=[]">Pulisci</span>
            </div>
            <div class="flog-body" id="floor-log-body">
              <template x-for="(l, i) in floorLog" :key="i">
                <div class="flog-line">
                  <span class="flog-ts" x-text="l.ts"></span>
                  <span :class="'flog-'+l.cls" x-text="l.msg"></span>
                </div>
              </template>
            </div>
          </div>

        </div>
      </template>
    </div>

  </div>
</div>
```

---

## 4 · Logica Alpine da aggiungere in `function app()`

```js
// ── Deploy Floor state ──
floorView:   'grid',
floorFilter: 'all',
floorSel:    null,
floorLog:    [],

// ── Deploy Floor helpers ──
floorFiltered() {
  const src = this.assignments;
  if (this.floorFilter === 'all') return src;
  return src.filter(a => a.status === this.floorFilter);
},

floorBadge(s) {
  return { running:'badge-running', completed:'badge-completed',
           failed:'badge-failed',   pending:'badge-pending' }[s] || 'badge-pending';
},

floorLed(s) {
  return { running:'led-run', completed:'led-ok',
           failed:'led-err',  pending:'led-warn' }[s] || '';
},

floorCurStep(a) {
  const steps = a.steps || [];
  return steps.find(s => s.status === 'running') ||
         steps.find(s => s.status === 'error')   || null;
},

async floorSelect(a) {
  if (this.floorSel?.id === a.id) { this.floorSel = null; return; }
  const detail = await this.api(`/api/pc-workflows/${a.id}`);
  this.floorSel = detail || a;
  this.floorBuildLog(this.floorSel);
  this.$nextTick(() => {
    const el = document.getElementById('floor-log-body');
    if (el) el.scrollTop = el.scrollHeight;
  });
},

floorBuildLog(a) {
  const steps = a.steps || [];
  const logs  = [];
  logs.push({ ts: this.fmtTs(a.assigned_at), cls:'dim', msg:`Agent connesso · ${a.pc_name}` });
  logs.push({ ts: this.fmtTs(a.started_at),  cls:'run', msg:`Avvio workflow: ${a.workflow_nome}` });
  steps.forEach(s => {
    if (s.status === 'done')    logs.push({ ts:'—', cls:'ok',  msg:`[step ${s.ordine}] ${s.tipo} → completato ✓` });
    if (s.status === 'running') logs.push({ ts:'—', cls:'run', msg:`[step ${s.ordine}] ${s.tipo} in corso...` });
    if (s.status === 'error')   logs.push({ ts:'—', cls:'err', msg:`[step ${s.ordine}] ${s.tipo} → ERRORE` });
    if (s.status === 'skipped') logs.push({ ts:'—', cls:'dim', msg:`[step ${s.ordine}] ${s.tipo} → skipped` });
  });
  if (a.status === 'completed') logs.push({ ts:'—', cls:'ok', msg:'Workflow completato con successo ✓' });
  if (a.status === 'failed')    logs.push({ ts:'—', cls:'err',msg:'Workflow interrotto — su_errore=stop' });
  this.floorLog = logs;
},

fmtTs(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleTimeString('it-IT', { hour:'2-digit', minute:'2-digit', second:'2-digit' });
},

// ── Aggiornamento live del panel aperto ──
// Aggiungere dentro loadAll(), dopo il blocco esistente di aggiornamento modal:
// if (this.section === 'floor' && this.floorSel) {
//   const d = await this.api(`/api/pc-workflows/${this.floorSel.id}`);
//   if (d) { this.floorSel = d; this.floorBuildLog(d); }
// }
```

---

## 5 · Badge/LED class mapping (riconciliazione con stile esistente)

I nomi classe usati sopra seguono lo stile `UI_REDESIGN_CLAUDE_CODE.md`.
Se hai già implementato il redesign usa direttamente:

| Stato | Badge class | LED class |
|---|---|---|
| `running` | `b-run` | `led-run` |
| `completed` | `b-ok` | `led-ok` |
| `failed` | `b-err` | `led-err` |
| `pending` | `b-warn` | `led-warn` |

Adatta `floorBadge()` e `floorLed()` ai nomi classe del tuo CSS attuale.

---

## 6 · Connessione a `loadAll()`

Nel metodo `loadAll()` esistente, aggiungere aggiornamento del panel laterale aperto:

```js
// alla fine di loadAll(), dopo il blocco 'modal detail':
if (this.section === 'floor' && this.floorSel) {
  const d = await this.api(`/api/pc-workflows/${this.floorSel.id}`);
  if (d) {
    this.floorSel = d;
    this.floorBuildLog(d);
    this.$nextTick(() => {
      const el = document.getElementById('floor-log-body');
      if (el) el.scrollTop = el.scrollHeight;
    });
  }
}
```

---

## 7 · Dipendenze

Nessuna nuova libreria. Usa:
- **Alpine.js** già presente
- **`GET /api/pc-workflows`** — già usata dalla sezione Deploy
- **`GET /api/pc-workflows/:id`** — già usata dal modal dettaglio
- CSS classes `.pbar`, `.pbar-fill`, `.pbar-run/.pbar-ok/.pbar-err` dal redesign
- CSS classes `.p-node`, `.p-circle`, `.p-label`, `.p-type`, `.p-line` dal redesign
- CSS classes `.badge`, `.led`, `.btn`, `.muted` già presenti

---

## 8 · Checklist

- [ ] `79 passed` su `cd server && NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v`
- [ ] Voce "Deploy Floor" visibile in sidebar
- [ ] Card PC mostrano badge stato + LED animato per running
- [ ] Step track (riga pallini) visibile sotto la progress bar
- [ ] Clic su card apre side panel con pipeline + log
- [ ] Side panel si aggiorna ogni 5s (polling esistente)
- [ ] Filtri Tutti / Running / Pending / Done / Error funzionanti
- [ ] Toggle Grid / Lista funzionante
- [ ] `✕` chiude il side panel

---

## 9 · Commit suggerito

```
feat: aggiungi sezione Deploy Floor (v1.8.0)

- Vista grid/lista di tutti i PC in fase di deploy
- Step track: riga pallini colorati per stato di ogni step
- Side panel con pipeline grafica + log stream live
- Filtri rapidi per stato, toggle grid/lista
- Aggiornamento automatico ogni 5s via polling esistente
```
