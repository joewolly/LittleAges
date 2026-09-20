type Point = { x: number; y: number }
export type LivingPerson = { citizenId: string; goal: string; mood: number; stress: number; injury: number; illness: number; toolCondition: number; clothingCondition: number; knowledge: string[]; deathObserved: boolean; experiences: { kind: string; minute: number; otherCitizenId: string | null }[] }
export type LivingOrder = { id: string; kind: string; location: Point; citizenId: string | null; subjectId: string | null; technique: string | null; phase: string; cargoInTransit?: boolean; suppliesDelivered?: boolean; workDone: number; requiredWork: number; blockedReason: string; ingredients: { resource: string; quantity: number }[]; cargo: { good: string; quantity: number }[] }
export type LivingField = { id: string; location: Point; growth: number; moisture: number; condition: number; yieldRemaining: number; harvests: number }
export type LivingFacility = { id: string; kind: string; location: Point }
export type LivingAnimal = { id: string; predator: boolean; location: Point; energy: number }
export type LivingFact = { id: string; minute: number; kind: string; citizenId: string | null; relatedId: string | null; value: number }
export type LivingWorld = {
  version: number; rulesVersion: string; worldMinute: number; age: string; capabilities: string[]; weather: string; temperature: number; rainfall: number
  completedOrders: number; foodHarvested: number; foodPrepared: number; careGiven: number; goodsSpoiled: number; totalFacts: number
  stock: { good: string; quantity: number }[]; people: LivingPerson[]; orders: LivingOrder[]; fields: LivingField[]; facilities: LivingFacility[]; animals: LivingAnimal[]; facts: LivingFact[]
}

function record(value: unknown): Record<string, unknown> { if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid living-world record.'); return value as Record<string, unknown> }
function text(value: unknown): string { if (typeof value !== 'string') throw new Error('Invalid living-world text.'); return value }
function number(value: unknown, minimum = 0, maximum = Number.MAX_SAFE_INTEGER): number { if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum || value > maximum) throw new Error('Invalid living-world quantity.'); return value }
function bool(value: unknown): boolean { if (typeof value !== 'boolean') throw new Error('Invalid living-world flag.'); return value }
function id(value: unknown): string { const result = text(value); if (!/^[1-9][0-9]*$/.test(result)) throw new Error('Invalid living-world identity.'); return result }
function optionalId(value: unknown): string | null { return value === null ? null : id(value) }
function list<T>(value: unknown, parse: (value: unknown) => T): T[] { if (!Array.isArray(value)) throw new Error('Invalid living-world collection.'); return value.map(parse) }
function point(value: unknown): Point { const p = record(value); return { x: number(p.x), y: number(p.y) } }
const good = (value: unknown) => { const item = record(value); return { good: text(item.good), quantity: number(item.quantity) } }

export function parseLivingWorld(value: unknown): LivingWorld | null {
  if (value === null || value === undefined) return null
  const w = record(value)
  if (w.version !== 1 || w.rulesVersion !== 'v02-rng1-living1') throw new Error('Unsupported living-world version.')
  return {
    version: 1, rulesVersion: w.rulesVersion, worldMinute: number(w.worldMinute), age: text(w.age), capabilities: list(w.capabilities, text), weather: text(w.weather), temperature: number(w.temperature, -40, 60), rainfall: number(w.rainfall, 0, 100),
    completedOrders: number(w.completedOrders), foodHarvested: number(w.foodHarvested), foodPrepared: number(w.foodPrepared), careGiven: number(w.careGiven), goodsSpoiled: number(w.goodsSpoiled), totalFacts: number(w.totalFacts), stock: list(w.stock, good),
    people: list(w.people, value => { const p = record(value); return { citizenId: id(p.citizenId), goal: text(p.goal), mood: number(p.mood, 0, 10000), stress: number(p.stress, 0, 10000), injury: number(p.injury, 0, 10000), illness: number(p.illness, 0, 10000), toolCondition: number(p.toolCondition, 0, 10000), clothingCondition: number(p.clothingCondition, 0, 10000), knowledge: list(p.knowledge, text), deathObserved: bool(p.deathObserved), experiences: list(p.experiences, value => { const e = record(value); return { kind: text(e.kind), minute: number(e.minute), otherCitizenId: optionalId(e.otherCitizenId) } }) } }),
    orders: list(w.orders, value => { const o = record(value); return { id: id(o.id), kind: text(o.kind), location: point(o.location), citizenId: optionalId(o.citizenId), subjectId: optionalId(o.subjectId), technique: o.technique === null ? null : text(o.technique), phase: text(o.phase), cargoInTransit: o.cargoInTransit === undefined ? false : bool(o.cargoInTransit), suppliesDelivered: o.suppliesDelivered === undefined ? false : bool(o.suppliesDelivered), workDone: number(o.workDone), requiredWork: number(o.requiredWork, 1), blockedReason: text(o.blockedReason), ingredients: list(o.ingredients, value => { const i = record(value); return { resource: text(i.resource), quantity: number(i.quantity) } }), cargo: list(o.cargo, good) } }),
    fields: list(w.fields, value => { const f = record(value); return { id: id(f.id), location: point(f.location), growth: number(f.growth, 0, 10000), moisture: number(f.moisture, 0, 10000), condition: number(f.condition, 0, 10000), yieldRemaining: number(f.yieldRemaining), harvests: number(f.harvests) } }),
    facilities: list(w.facilities, value => { const f = record(value); return { id: id(f.id), kind: text(f.kind), location: point(f.location) } }),
    animals: list(w.animals, value => { const a = record(value); return { id: id(a.id), predator: bool(a.predator), location: point(a.location), energy: number(a.energy, 0, 10000) } }),
    facts: list(w.facts, value => { const f = record(value); return { id: id(f.id), minute: number(f.minute), kind: text(f.kind), citizenId: optionalId(f.citizenId), relatedId: optionalId(f.relatedId), value: number(f.value) } }),
  }
}

export async function fetchLivingWorld(): Promise<LivingWorld | null> {
  const response = await fetch('/api/v1/living')
  if (response.status === 404) return null
  if (!response.ok) throw new Error('Living settlement could not be read.')
  const body = await response.text()
  return parseLivingWorld(body ? JSON.parse(body) : null)
}

export function livingLabel(value: string): string { return value.replace(/([a-z])([A-Z])/g, '$1 $2') }

export function livingWorkStage(order: LivingOrder): string {
  if (order.phase === 'Collect') return order.ingredients.length ? 'Collecting supplies' : 'Going to the meeting point'
  if (order.phase === 'Travel') return order.suppliesDelivered || !order.ingredients.length ? 'Going to the work site' : 'Carrying supplies'
  if (order.phase === 'Deliver') return order.cargoInTransit ? 'Bringing goods home' : 'Collecting finished goods'
  return 'Working'
}
