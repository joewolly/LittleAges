import { useMemo, useState } from 'react'
import type { Citizen } from './api'
import { descendantCounts, descendantIds, familyRelatives, familyTopologyKey, indexFromTopologyKey, projectFamily, type FamilyMode } from './family'
import { citizenObservationLabel, isCitizenPresent } from './newcomers'

export type FamilyViewState = { root: string | null; mode: FamilyMode; depth: number }
type Props = { citizens: Citizen[]; state: FamilyViewState; onChange: (state: FamilyViewState) => void; onBiography: (id: string) => void; onHistory: (id: string) => void; onFollow: (id: string) => void }
const minute = (value: number | null | undefined) => value == null ? 'Unknown' : `Minute ${value.toLocaleString('en-US')}`
const familyOrigin = (c: Citizen) => c.newcomer ? 'External origin · parents before arrival unknown' : c.founderOrdinal !== null ? 'Founder' : 'Origin not recorded'

/** Bounded relation pages keep very large sibling/child groups accessible. */
function Relatives({ title, ids, roster, onRoot }: { title: string; ids: string[]; roster: Map<string, Citizen>; onRoot: (id: string) => void }) {
  const [page, setPage] = useState(0)
  const current = Math.min(page, Math.max(0, Math.ceil(ids.length / 12) - 1))
  return <div className="family-relatives"><h4>{title} · {ids.length}</h4>{ids.length === 0 ? <p>{title === 'Parents' ? 'Unknown parents' : 'None recorded'}</p> : <><ul>{ids.slice(current * 12, current * 12 + 12).map(id => <li key={id}><button type="button" className="family-link" onClick={() => onRoot(id)}>{roster.get(id)?.name ?? 'Missing record'} · #{id}</button></li>)}</ul>{ids.length > 12 && <div className="family-actions"><button type="button" className="observer-button" disabled={current === 0} onClick={() => setPage(current - 1)}>Previous {title.toLowerCase()}</button><span>Page {current + 1} of {Math.ceil(ids.length / 12)}</span><button type="button" className="observer-button" disabled={(current + 1) * 12 >= ids.length} onClick={() => setPage(current + 1)}>Next {title.toLowerCase()}</button></div>}</>}</div>
}

export function FamilyRecords({ citizens, state, onChange, onBiography, onHistory, onFollow }: Props) {
  const [search, setSearch] = useState('')
  const topologyKey = familyTopologyKey(citizens)
  const index = useMemo(() => indexFromTopologyKey(topologyKey), [topologyKey])
  const roster = useMemo(() => new Map(citizens.map(c => [c.citizenId, c])), [citizens])
  const root = state.root ?? citizens[0]?.citizenId ?? null
  const graph = useMemo(() => root === null ? null : projectFamily(index, root, state.mode, state.depth), [index, root, state.mode, state.depth])
  const descendants = useMemo(() => root === null ? new Set<string>() : descendantIds(index, root), [index, root])
  const counts = descendantCounts(descendants, roster)
  const relatives = useMemo(() => root === null ? null : familyRelatives(index, root), [index, root])
  const positions = useMemo(() => new Map(graph?.nodes.map(node => [node.id, node]) ?? []), [graph])
  const query = search.trim().toLocaleLowerCase()
  const matches: Citizen[] = []
  let matchCount = 0
  if (query) for (const c of citizens) if (c.citizenId.includes(query) || c.name.toLocaleLowerCase().includes(query)) { matchCount++; if (matches.length < 20) matches.push(c) }
  const rootCitizen = root ? roster.get(root) : undefined
  const recenter = (id: string) => { onChange({ ...state, root: id }); queueMicrotask(() => document.getElementById('family-root-heading')?.focus()) }
  return <section className="family-surface" aria-labelledby="family-heading">
    <div className="section-heading"><span className="section-kicker">Recorded kinship</span><h2 id="family-heading">Family & lineage</h2><p className="section-description">Parentage comes from recorded IDs. Partnership is a separate snapshot fact. No ancestry is inferred.</p></div>
    <div className="family-controls"><label>Search family roots by name or ID<input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Name or decimal citizen ID" /></label><label>Direction<select aria-label="Direction" value={state.mode} onChange={event => onChange({ ...state, mode: event.target.value as FamilyMode })}><option value="ancestors">Ancestors</option><option value="descendants">Descendants</option></select></label><label>Generations<select aria-label="Generations" value={state.depth} onChange={event => onChange({ ...state, depth: Number(event.target.value) })}>{[1, 2, 3, 4].map(depth => <option key={depth} value={depth}>{depth}</option>)}</select></label></div>
    {query && <div className="family-search"><p role="status">{matchCount} matching records{matchCount > 20 ? ' · first 20 shown; narrow the search by name or ID' : ''}</p><ul>{matches.map(c => <li key={c.citizenId}><button type="button" className="family-link" onClick={() => recenter(c.citizenId)}>{c.name} · #{c.citizenId}</button></li>)}</ul></div>}
    {root === null ? <p>No citizen records are available.</p> : <>
      <h3 id="family-root-heading" tabIndex={-1}>{rootCitizen?.name ?? 'Missing record'} · #{root}</h3>
      <p className="family-counts">{counts.total} unique recorded descendants · {counts.livingResidents} living resident descendants <small>Complete roster counts, independent of graph limits.</small></p>
      <div className="family-actions"><button type="button" className="observer-button" disabled={!rootCitizen} onClick={() => onBiography(root)}>Biography</button><button type="button" className="observer-button" disabled={!rootCitizen} onClick={() => onHistory(root)}>Family history</button><button type="button" className="observer-button" disabled={!rootCitizen || !isCitizenPresent(rootCitizen)} onClick={() => onFollow(root)}>Follow on map</button></div>
      {rootCitizen && <p>{citizenObservationLabel(rootCitizen)} · {familyOrigin(rootCitizen)}</p>}

      <p className="section-note">{graph?.nodes.length} people shown · {state.depth} {state.mode} generations plus immediate root family · maximum 100 people and 4 generations. Solid lines show parent → child; dashed lines show recorded partnership. Partners' relatives are not expanded.</p>
      {(graph?.depthLimited || graph?.nodeLimited) && <p role="status" className="notice">{graph.nodeLimited ? '100-person limit reached. ' : ''}{graph.depthLimited ? 'More generations exist beyond the selected depth. ' : ''}Recenter on any person or use the paged relatives and root search to reach hidden branches.</p>}
      {index.hasCycle && <p className="notice">The roster contains cyclic parent links. Recorded edges are retained; traversal visits each identity once. Generation placement cannot resolve contradictory links.</p>}
      <div className="family-graph-scroll" tabIndex={0} aria-label="Family graph; scroll horizontally for generations">
        <div className="family-graph" style={{ width: graph?.width, height: graph?.height }}>
          <svg className="family-edges" width={graph?.width} height={graph?.height} aria-hidden="true"><defs><marker id="family-parent-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z" /></marker></defs>{graph?.edges.map(edge => { const a = positions.get(edge.parent)!, b = positions.get(edge.child)!; return <path markerEnd="url(#family-parent-arrow)" key={`${edge.parent}:${edge.child}`} d={a.x === b.x ? `M ${a.x + 120} ${a.y + (a.y < b.y ? 260 : 0)} L ${b.x + 120} ${b.y + (a.y < b.y ? 0 : 260)}` : `M ${a.x + (a.x < b.x ? 240 : 0)} ${a.y + 130} L ${b.x + (a.x < b.x ? 0 : 240)} ${b.y + 130}`} /> })}{graph?.partnerships.map(edge => { const a = positions.get(edge.first)!, b = positions.get(edge.second)!; return <path className="family-partner-edge" key={`${edge.first}:${edge.second}`} d={`M ${a.x + 120} ${a.y + 130} L ${b.x + 120} ${b.y + 130}`} /> })}</svg>
          <ol className="family-nodes" aria-label="People in family graph">{graph?.nodes.map(node => {
            const c = roster.get(node.id)
            const parents = index.parents.get(node.id) ?? []
            return <li key={node.id} className={`family-node ${node.id === root ? 'is-root' : ''}`} style={{ left: node.x, top: node.y }} data-person-id={node.id}>
              <span className="section-kicker">{node.generation === 0 ? 'Root / immediate family' : `${Math.abs(node.generation)} ${node.generation < 0 ? 'above' : 'below'} root`}</span>
              <button type="button" className="family-node-name" aria-label={`Recenter on ${c?.name ?? 'missing record'} · ${node.id}`} onClick={() => recenter(node.id)}>{c?.name ?? 'Missing record'}</button><span className="family-id">#{node.id}</span>
              {c ? <><p>{citizenObservationLabel(c)}</p><p>Birth: {c.newcomer ? 'Unknown external birth date' : minute(c.birthMinute)}<br />Death: {c.deathMinute == null ? c.isAlive ? 'Not recorded' : 'Unknown' : `${minute(c.deathMinute)} · ${c.deathCause ?? 'Unknown cause'}`}</p><small>{familyOrigin(c)}</small></> : <p>Linked ID only. Name, life and dates unknown.</p>}
              <p className="family-node-links">Parents: {parents.length === 0 ? 'Unknown' : parents.slice(0, 2).map(id => <button type="button" key={id} className="family-link" onClick={() => recenter(id)}>{roster.get(id)?.name ?? 'Missing record'} · #{id}</button>)}{parents.length > 2 && <span>{parents.length - 2} more recorded links; recenter for all.</span>}</p>
              {index.partners.has(node.id) && <p className="family-node-links">Recorded partner: <button type="button" className="family-link" onClick={() => recenter(index.partners.get(node.id)!)}>{roster.get(index.partners.get(node.id)!)?.name ?? 'Missing record'} · #{index.partners.get(node.id)}</button></p>}
            </li>
          })}</ol>
        </div>
      </div>
      {relatives && <div className="family-immediate"><Relatives key={`${root}-parents`} title="Parents" ids={relatives.parents} roster={roster} onRoot={recenter} /><Relatives key={`${root}-children`} title="Children" ids={relatives.children} roster={roster} onRoot={recenter} /><Relatives key={`${root}-siblings`} title="Siblings sharing a known parent" ids={relatives.siblings} roster={roster} onRoot={recenter} /><Relatives key={`${root}-partner`} title="Recorded partner" ids={relatives.partner ? [relatives.partner] : []} roster={roster} onRoot={recenter} /></div>}
    </>}
  </section>
}
