import type { Citizen } from './api'
import { isCitizenPresent, isCitizenResident } from './newcomers'

export const FAMILY_NODE_LIMIT = 100
export type FamilyMode = 'ancestors' | 'descendants'
export type FamilyRecord = Pick<Citizen, 'citizenId' | 'parentAId' | 'parentBId' | 'childrenIds' | 'partnerId'>
export type FamilyIndex = {
  parents: Map<string, string[]>
  children: Map<string, string[]>
  partners: Map<string, string>
  hasCycle: boolean
}
export type FamilyNode = { id: string; generation: number; x: number; y: number }
export type FamilyGraph = { nodes: FamilyNode[]; edges: { parent: string; child: string }[]; partnerships: { first: string; second: string }[]; nodeLimited: boolean; depthLimited: boolean; width: number; height: number }

/** Canonical decimal strings retain precision; numeric conversion is forbidden. */
export function compareFamilyIds(a: string, b: string) { return a.length - b.length || (a < b ? -1 : a > b ? 1 : 0) }
const sorted = (ids: Iterable<string>) => [...new Set(ids)].sort(compareFamilyIds)

/** Only recorded parent/child edges. Missing records remain ID-only placeholders. */
export function buildFamilyIndex(records: FamilyRecord[]): FamilyIndex {
  const parents = new Map<string, Set<string>>()
  const children = new Map<string, Set<string>>()
  const partners = new Map<string, string>()
  const ensure = (id: string) => { if (!parents.has(id)) parents.set(id, new Set()); if (!children.has(id)) children.set(id, new Set()) }
  const edge = (parent: string, child: string) => { ensure(parent); ensure(child); parents.get(child)!.add(parent); children.get(parent)!.add(child) }
  for (const record of records) {
    ensure(record.citizenId)
    for (const parent of [record.parentAId, record.parentBId]) if (parent !== null) edge(parent, record.citizenId)
    for (const child of record.childrenIds) edge(record.citizenId, child)
    if (record.partnerId !== null) partners.set(record.citizenId, record.partnerId)
  }
  const index = { parents: new Map([...parents].map(([id, ids]) => [id, sorted(ids)])), children: new Map([...children].map(([id, ids]) => [id, sorted(ids)])), partners, hasCycle: false }
  // Iterative DFS: even corrupt cyclic archives cannot recurse or fabricate ancestry.
  const colors = new Map<string, number>()
  for (const id of index.parents.keys()) {
    if (colors.has(id)) continue
    const stack: { id: string; next: number }[] = [{ id, next: 0 }]
    colors.set(id, 1)
    while (stack.length) {
      const frame = stack[stack.length - 1]
      const next = index.children.get(frame.id)?.[frame.next++]
      if (next === undefined) { colors.set(frame.id, 2); stack.pop() }
      else if (colors.get(next) === 1) index.hasCycle = true
      else if (!colors.has(next)) { colors.set(next, 1); stack.push({ id: next, next: 0 }) }
    }
  }
  return index
}

/** Stable across movement, needs, names and life updates. Used as the layout dependency. */
export function familyTopologyKey(citizens: FamilyRecord[]) {
  return JSON.stringify(citizens.map(c => [c.citizenId, c.parentAId, c.parentBId, c.childrenIds, c.partnerId]))
}
export function indexFromTopologyKey(key: string): FamilyIndex {
  const tuples = JSON.parse(key) as [string, string | null, string | null, string[], string | null][]
  return buildFamilyIndex(tuples.map(([citizenId, parentAId, parentBId, childrenIds, partnerId]) => ({ citizenId, parentAId, parentBId, childrenIds, partnerId })))
}

export function familyRelatives(index: FamilyIndex, root: string) {
  const parents = index.parents.get(root) ?? []
  return { parents, children: index.children.get(root) ?? [], siblings: sorted(parents.flatMap(parent => index.children.get(parent) ?? [])).filter(id => id !== root), partner: index.partners.get(root) ?? null }
}

/** Complete traversal for exact unique counts, independent of display limits. */
export function descendantIds(index: FamilyIndex, root: string): Set<string> {
  const visited = new Set([root])
  const queue = [root]
  for (let head = 0; head < queue.length; head++) for (const child of index.children.get(queue[head]) ?? []) if (!visited.has(child)) { visited.add(child); queue.push(child) }
  visited.delete(root)
  return visited
}
export function descendantCounts(ids: Set<string>, citizens: Map<string, Citizen>) {
  let total = 0, livingResidents = 0
  for (const id of ids) { const citizen = citizens.get(id); if (!citizen) continue; total++; if (isCitizenPresent(citizen) && isCitizenResident(citizen)) livingResidents++ }
  return { total, livingResidents }
}

/** Directional ancestry plus root's immediate family; partners' relatives never expand. */
export function projectFamily(index: FamilyIndex, root: string, mode: FamilyMode, depth: number): FamilyGraph {
  const generations = new Map<string, number>([[root, 0]])
  const queue = [{ id: root, distance: 0 }]
  const direction = mode === 'ancestors' ? index.parents : index.children
  const sign = mode === 'ancestors' ? -1 : 1
  let nodeLimited = false, depthLimited = false
  const add = (id: string, generation: number) => { if (generations.has(id)) return false; if (generations.size >= FAMILY_NODE_LIMIT) { nodeLimited = true; return false }; generations.set(id, generation); return true }
  for (let head = 0; head < queue.length; head++) {
    const node = queue[head]
    const next = direction.get(node.id) ?? []
    if (node.distance >= Math.min(4, Math.max(1, depth))) { if (next.some(id => !generations.has(id))) depthLimited = true; continue }
    for (const id of next) if (add(id, (node.distance + 1) * sign)) queue.push({ id, distance: node.distance + 1 })
  }
  const relatives = familyRelatives(index, root)
  for (const id of relatives.parents) add(id, -1)
  for (const id of relatives.children) add(id, 1)
  for (const id of relatives.siblings) add(id, 0)
  if (relatives.partner) add(relatives.partner, 0)
  const levels = [...new Set(generations.values())].sort((a, b) => a - b)
  const rows = new Map<number, number>()
  const nodes = [...generations].sort((a, b) => a[1] - b[1] || compareFamilyIds(a[0], b[0])).map(([id, generation]) => {
    const row = rows.get(generation) ?? 0; rows.set(generation, row + 1)
    return { id, generation, x: levels.indexOf(generation) * 260, y: row * 280 }
  })
  const edges: FamilyGraph['edges'] = []
  const partnerships: FamilyGraph['partnerships'] = []
  const seenPartners = new Set<string>()
  for (const { id } of nodes) {
    for (const child of index.children.get(id) ?? []) if (generations.has(child)) edges.push({ parent: id, child })
    const partner = index.partners.get(id)
    if (partner && generations.has(partner)) { const pair = sorted([id, partner]); const key = pair.join(':'); if (!seenPartners.has(key)) { seenPartners.add(key); partnerships.push({ first: id, second: partner }) } }
  }
  return { nodes, edges, partnerships, nodeLimited, depthLimited, width: levels.length * 260, height: Math.max(1, ...rows.values()) * 280 }
}
