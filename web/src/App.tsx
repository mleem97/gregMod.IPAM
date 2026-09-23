import { useCallback, useEffect, useState } from 'react'

type Status = { ok: boolean; mod: string; scene: string; servers: number; dhcpUnlocked: boolean; overlayVisible: boolean }
type Server = { id: number; name: string; ip: string; customer: string; type: string; power: string }
type Scope = { id: string; name: string; level: string; cidr: string; priority: number }

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, { ...init, headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) } })
  return (await res.json()) as T
}

function usePoll<T>(fn: () => Promise<T>, ms: number, deps: unknown[] = []): T | null {
  const [data, setData] = useState<T | null>(null)
  useEffect(() => {
    let alive = true
    const tick = () => fn().then((d) => alive && setData(d)).catch(() => {})
    tick()
    const t = setInterval(tick, ms)
    return () => { alive = false; clearInterval(t) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
  return data
}

export default function App() {
  const [tab, setTab] = useState<'dash' | 'servers' | 'dhcp' | 'racks' | 'logs'>('dash')
  const status = usePoll<Status>(() => api('/api/status'), 3000)

  return (
    <div className="app">
      <header>
        <div>
          <h1>GREGSTORE <span>IPAM WebUI</span></h1>
          <p className="sub">gregMod.IPAM · http://127.0.0.1 backend</p>
        </div>
        <div className="status-chips">
          <span className={status ? 'chip ok' : 'chip bad'}>{status ? 'verbunden' : 'kein Spiel'}</span>
          {status && <span className="chip">{status.scene || '—'}</span>}
          {status && <span className={status.dhcpUnlocked ? 'chip ok' : 'chip'}>DHCP {status.dhcpUnlocked ? 'an' : 'aus'}</span>}
        </div>
        <nav>
          {(['dash', 'servers', 'dhcp', 'racks', 'logs'] as const).map((t) => (
            <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>
              {{ dash: 'Dashboard', servers: 'Server', dhcp: 'DHCP', racks: 'Racks', logs: 'Logs' }[t]}
            </button>
          ))}
        </nav>
      </header>
      <main>
        {tab === 'dash' && <Dashboard status={status} />}
        {tab === 'servers' && <Servers />}
        {tab === 'dhcp' && <Dhcp />}
        {tab === 'racks' && <Racks />}
        {tab === 'logs' && <Logs />}
      </main>
    </div>
  )
}

function Dashboard({ status }: { status: Status | null }) {
  const [msg, setMsg] = useState('')
  if (!status) return <p className="muted">Spiel nicht erreichbar — Mod geladen und Szene aktiv?</p>
  const toggleOverlay = async () => {
    const r = await api<{ ok: boolean; error?: string }>('/api/overlay', {
      method: 'POST', body: JSON.stringify({ visible: !status.overlayVisible }),
    })
    setMsg(r.ok ? '' : r.error ?? 'Fehler')
  }
  const assignAll = async () => {
    const r = await api<{ ok: boolean; error?: string }>('/api/dhcp/assign-all', { method: 'POST' })
    setMsg(r.ok ? 'DHCP assign-all gestartet' : r.error ?? 'Fehler')
  }
  return (
    <div className="cards">
      <div className="card"><h3>Server</h3><p className="big">{status.servers}</p></div>
      <div className="card"><h3>Szene</h3><p className="big">{status.scene || '—'}</p></div>
      <div className="card"><h3>Overlay</h3><p className="big">{status.overlayVisible ? 'offen' : 'zu'}</p>
        <button onClick={toggleOverlay}>{status.overlayVisible ? 'Schließen' : 'Öffnen'}</button></div>
      <div className="card"><h3>DHCP</h3>
        <button onClick={assignAll} disabled={!status.dhcpUnlocked}>Alle zuweisen (Ctrl+L)</button>
        {msg && <p className="muted">{msg}</p>}</div>
    </div>
  )
}

function Servers() {
  const [servers, setServers] = useState<Server[]>([])
  const [filter, setFilter] = useState('')
  const [msg, setMsg] = useState('')
  const reload = useCallback(async () => {
    try { setServers(await api<Server[]>('/api/servers')) } catch { /* offline */ }
  }, [])
  useEffect(() => { reload(); const t = setInterval(reload, 4000); return () => clearInterval(t) }, [reload])

  const act = async (path: string, payload: object, label: string) => {
    const r = await api<{ ok: boolean; error?: string; ip?: string }>(path, { method: 'POST', body: JSON.stringify(payload) })
    setMsg(r.ok ? `${label} OK${r.ip ? ': ' + r.ip : ''}` : r.error ?? 'Fehler')
    reload()
  }

  const rows = servers.filter((s) =>
    (`${s.name} ${s.ip} ${s.customer}`.toLowerCase().includes(filter.toLowerCase())))
  return (
    <div>
      <div className="toolbar">
        <input placeholder="Filter …" value={filter} onChange={(e) => setFilter(e.target.value)} />
        <button onClick={reload}>Neu laden</button>
        {msg && <span className="muted">{msg}</span>}
      </div>
      <table>
        <thead><tr><th>Name</th><th>IP</th><th>Kunde</th><th>Typ</th><th>Power</th><th>Aktionen</th></tr></thead>
        <tbody>
          {rows.map((s) => (
            <ServerRow key={s.id} s={s} act={act} />
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ServerRow({ s, act }: { s: Server; act: (p: string, b: object, l: string) => void }) {
  const [ip, setIp] = useState(s.ip)
  const [name, setName] = useState(s.name)
  return (
    <tr>
      <td><input value={name} onChange={(e) => setName(e.target.value)} /></td>
      <td><input value={ip} onChange={(e) => setIp(e.target.value)} /></td>
      <td>{s.customer}</td>
      <td>{s.type}</td>
      <td>{s.power}</td>
      <td className="actions">
        <button onClick={() => act('/api/dhcp/assign-one', { id: s.id }, 'DHCP')}>DHCP</button>
        <button onClick={() => act('/api/server/ip', { id: s.id, ip }, 'IP')}>IP</button>
        <button onClick={() => act('/api/server/rename', { id: s.id, name }, 'Rename')}>Rename</button>
        <button onClick={() => act('/api/server/power', { id: s.id }, 'Power')}>Power</button>
      </td>
    </tr>
  )
}

function Dhcp() {
  const [scopes, setScopes] = useState<Scope[]>([])
  const [name, setName] = useState('')
  const [cidr, setCidr] = useState('')
  const [level, setLevel] = useState('Global')
  const [msg, setMsg] = useState('')
  const reload = useCallback(async () => {
    try { setScopes(await api<Scope[]>('/api/scopes')) } catch { /* offline */ }
  }, [])
  useEffect(() => { reload() }, [reload])

  const add = async () => {
    const r = await api<{ ok: boolean; error?: string }>('/api/scopes', {
      method: 'POST', body: JSON.stringify({ name, cidr, level }),
    })
    setMsg(r.ok ? 'Scope angelegt' : r.error ?? 'Fehler')
    setName(''); setCidr('')
    reload()
  }
  const del = async (id: string) => {
    const r = await api<{ ok: boolean }>(`/api/scopes?id=${encodeURIComponent(id)}`, { method: 'DELETE' })
    setMsg(r.ok ? 'Scope gelöscht' : 'Fehler')
    reload()
  }
  return (
    <div>
      <div className="toolbar">
        <input placeholder="Name" value={name} onChange={(e) => setName(e.target.value)} />
        <input placeholder="CIDR z. B. 10.0.0.0/24" value={cidr} onChange={(e) => setCidr(e.target.value)} />
        <select value={level} onChange={(e) => setLevel(e.target.value)}>
          <option>Global</option><option>VLAN</option><option>Switch</option>
        </select>
        <button onClick={add}>Scope anlegen</button>
        {msg && <span className="muted">{msg}</span>}
      </div>
      <table>
        <thead><tr><th>Prio</th><th>Name</th><th>Level</th><th>CIDR</th><th></th></tr></thead>
        <tbody>
          {scopes.map((s) => (
            <tr key={s.id}><td>{s.priority}</td><td>{s.name}</td><td>{s.level}</td><td>{s.cidr}</td>
              <td><button onClick={() => del(s.id)}>Löschen</button></td></tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

type Mount = { id: number; template: string; instantiated: boolean; x: number; y: number; z: number }
type Rack = { id: number; name: string; x: number; z: number }
type Template = { id: string; price: number }

function Racks() {
  const [mounts, setMounts] = useState<Mount[]>([])
  const [racks, setRacks] = useState<Rack[]>([])
  const [templates, setTemplates] = useState<Template[]>([])
  const [rackSel, setRackSel] = useState('')
  const [tplSel, setTplSel] = useState('')
  const [cheat, setCheat] = useState(false)
  const [msg, setMsg] = useState('')
  const reload = useCallback(async () => {
    try {
      const [m, r, t] = await Promise.all([
        api<Mount[]>('/api/racks/mounts'),
        api<Rack[]>('/api/racks/list'),
        api<Template[]>('/api/racks/templates'),
      ])
      setMounts(m); setRacks(r); setTemplates(t)
    } catch { /* offline */ }
  }, [])
  useEffect(() => { reload(); const t = setInterval(reload, 5000); return () => clearInterval(t) }, [reload])

  const install = async (id: number) => {
    const r = await api<{ ok: boolean; error?: string }>('/api/racks/install', {
      method: 'POST', body: JSON.stringify({ id, cheat }),
    })
    setMsg(r.ok ? `Aufbau gestartet (Mount ${id})` : r.error ?? 'Fehler')
    setTimeout(reload, 3000)
  }
  const applyTpl = async () => {
    if (!rackSel || !tplSel) { setMsg('Rack + Template wählen'); return }
    const r = await api<{ ok: boolean; error?: string }>('/api/racks/apply-template', {
      method: 'POST', body: JSON.stringify({ rackId: Number(rackSel), templateId: tplSel }),
    })
    setMsg(r.ok ? 'Template wird angewendet' : r.error ?? 'Fehler')
  }
  const open = mounts.filter((m) => !m.instantiated)
  return (
    <div>
      <div className="toolbar">
        <label><input type="checkbox" checked={cheat} onChange={(e) => setCheat(e.target.checked)} /> Cheat (ohne Kosten)</label>
        <button onClick={reload}>Neu laden</button>
        {msg && <span className="muted">{msg}</span>}
      </div>
      <h3>Mounts ({mounts.length}, offen: {open.length})</h3>
      <table>
        <thead><tr><th>Position</th><th>Template</th><th>Status</th><th></th></tr></thead>
        <tbody>
          {mounts.map((m) => (
            <tr key={m.id}>
              <td>{m.x}, {m.z}</td>
              <td>{m.template || '—'}</td>
              <td>{m.instantiated ? 'aufgebaut' : 'offen'}</td>
              <td>{!m.instantiated && <button onClick={() => install(m.id)}>Aufbauen</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <h3>Template anwenden</h3>
      <div className="toolbar">
        <select value={rackSel} onChange={(e) => setRackSel(e.target.value)}>
          <option value="">Rack wählen …</option>
          {racks.map((r) => <option key={r.id} value={r.id}>{r.name || r.id} ({r.x}, {r.z})</option>)}
        </select>
        <select value={tplSel} onChange={(e) => setTplSel(e.target.value)}>
          <option value="">Template wählen …</option>
          {templates.map((t) => <option key={t.id} value={t.id}>{t.id} ({t.price})</option>)}
        </select>
        <button onClick={applyTpl}>Anwenden</button>
      </div>
    </div>
  )
}

function Logs() {
  const lines = usePoll<string[]>(() => api('/api/logs'), 2000) ?? []
  return <pre className="logs">{lines.join('\n') || '—'}</pre>
}
