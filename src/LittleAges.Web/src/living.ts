type Point = { x: number; y: number }
export const UNIFIED_RULES_VERSION = 'm13-rng1-unified1'
const UNIFIED_LIVING_SUCCESSOR_RULES_VERSIONS = new Set(['m14-rng1-migration1', 'm15-rng1-roads1', 'm16-rng1-planned1', 'm16-rng1-festivals1'])
const m12OwnedGoods = new Set(['Food', 'Wood', 'Stone'])
export type LivingPerson = { citizenId: string; goal: string; mood: number; stress: number; injury: number; illness: number; toolCondition: number; clothingCondition: number; knowledge: string[]; deathObserved: boolean; experiences: { kind: string; minute: number; otherCitizenId: string | null }[] }
export type LivingOrder = { id: string; kind: string; location: Point; citizenId: string | null; subjectId: string | null; technique: string | null; phase: string; cargoInTransit?: boolean; suppliesDelivered?: boolean; workDone: number; requiredWork: number; blockedReason: string; ingredients: { resource: string; quantity: number }[]; cargo: { good: string; quantity: number }[] }
export type LivingField = { id: string; location: Point; growth: number; moisture: number; condition: number; yieldRemaining: number; harvests: number }
export type LivingFacility = { id: string; kind: string; location: Point }
export type LivingAnimal = { id: string; predator: boolean; location: Point; energy: number }
export type LivingFact = { id: string; minute: number; kind: string; citizenId: string | null; relatedId: string | null; value: number }
export type Festival = { settlementId: string; year: number; location: Point; startMinute: number; endMinute: number; started: boolean; finished: boolean; mode: 'Feast' | 'Gathering'; initialFood: number; reservedFood: number; consumedFood: number; attendance: { citizenId: string; minutes: number; benefitsGranted: boolean; portionConsumed: boolean }[] }
export type LivingWorld = {
  version: number; rulesVersion: string; worldMinute: number; age: string; capabilities: string[]; weather: string; temperature: number; rainfall: number
  completedOrders: number; foodHarvested: number; foodPrepared: number; careGiven: number; goodsSpoiled: number; totalFacts: number
  festivals?: Festival[]
  festivalVisit?: { citizenId: string; settlementId: string; startMinute: number; partyId: string | null } | null
  stock: { good: string; quantity: number }[]; people: LivingPerson[]; orders: LivingOrder[]; fields: LivingField[]; facilities: LivingFacility[]; animals: LivingAnimal[]; facts: LivingFact[]
}

export function isUnifiedLivingRules(rulesVersion: string): boolean { return rulesVersion === UNIFIED_RULES_VERSION || UNIFIED_LIVING_SUCCESSOR_RULES_VERSIONS.has(rulesVersion) }
export function isM12OwnedGood(good: string): boolean { return m12OwnedGoods.has(good) }

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
  const rulesVersion = text(w.rulesVersion)
  if (w.version !== 1 || (rulesVersion !== 'v02-rng1-living1' && rulesVersion !== 'v02-rng1-living2' && !isUnifiedLivingRules(rulesVersion))) throw new Error('Unsupported living-world version.')
  return {
    festivalVisit: w.festivalVisit == null ? null : (() => { const v = record(w.festivalVisit); return { citizenId: id(v.citizenId), settlementId: id(v.settlementId), startMinute: number(v.startMinute), partyId: optionalId(v.partyId) } })(),
    festivals: w.festivals == null ? [] : list(w.festivals, parseFestival),
    version: 1, rulesVersion, worldMinute: number(w.worldMinute), age: text(w.age), capabilities: list(w.capabilities, text), weather: text(w.weather), temperature: number(w.temperature, -40, 60), rainfall: number(w.rainfall, 0, 100),
    completedOrders: number(w.completedOrders), foodHarvested: number(w.foodHarvested), foodPrepared: number(w.foodPrepared), careGiven: number(w.careGiven), goodsSpoiled: number(w.goodsSpoiled), totalFacts: number(w.totalFacts), stock: list(w.stock, good),
    people: list(w.people, value => { const p = record(value); return { citizenId: id(p.citizenId), goal: text(p.goal), mood: number(p.mood, 0, 10000), stress: number(p.stress, 0, 10000), injury: number(p.injury, 0, 10000), illness: number(p.illness, 0, 10000), toolCondition: number(p.toolCondition, 0, 10000), clothingCondition: number(p.clothingCondition, 0, 10000), knowledge: list(p.knowledge, text), deathObserved: bool(p.deathObserved), experiences: list(p.experiences, value => { const e = record(value); return { kind: text(e.kind), minute: number(e.minute), otherCitizenId: optionalId(e.otherCitizenId) } }) } }),
    orders: list(w.orders, value => { const o = record(value); return { id: id(o.id), kind: text(o.kind), location: point(o.location), citizenId: optionalId(o.citizenId), subjectId: optionalId(o.subjectId), technique: o.technique === null ? null : text(o.technique), phase: text(o.phase), cargoInTransit: o.cargoInTransit === undefined ? false : bool(o.cargoInTransit), suppliesDelivered: o.suppliesDelivered === undefined ? false : bool(o.suppliesDelivered), workDone: number(o.workDone), requiredWork: number(o.requiredWork, 1), blockedReason: text(o.blockedReason), ingredients: list(o.ingredients, value => { const i = record(value); return { resource: text(i.resource), quantity: number(i.quantity) } }), cargo: list(o.cargo, good) } }),
    fields: list(w.fields, value => { const f = record(value); return { id: id(f.id), location: point(f.location), growth: number(f.growth, 0, 10000), moisture: number(f.moisture, 0, 10000), condition: number(f.condition, 0, 10000), yieldRemaining: number(f.yieldRemaining), harvests: number(f.harvests) } }),
    facilities: list(w.facilities, value => { const f = record(value); return { id: id(f.id), kind: text(f.kind), location: point(f.location) } }),
    animals: list(w.animals, value => { const a = record(value); return { id: id(a.id), predator: bool(a.predator), location: point(a.location), energy: number(a.energy, 0, 10000) } }),
    facts: list(w.facts, value => { const f = record(value); return { id: id(f.id), minute: number(f.minute), kind: text(f.kind), citizenId: optionalId(f.citizenId), relatedId: optionalId(f.relatedId), value: number(f.value) } }),
  }
}

function parseFestival(value: unknown): Festival {
  const f = record(value)
  const mode = text(f.mode)
  if (mode !== 'Feast' && mode !== 'Gathering') throw new Error('Invalid festival mode.')
  const settlementId = id(f.settlementId)
  if (settlementId !== '1' && settlementId !== '2') throw new Error('Invalid festival site.')
  const result: Festival = { settlementId, year: number(f.year), location: point(f.location), startMinute: number(f.startMinute), endMinute: number(f.endMinute), started: bool(f.started), finished: bool(f.finished), mode, initialFood: number(f.initialFood), reservedFood: number(f.reservedFood), consumedFood: number(f.consumedFood), attendance: list(f.attendance, value => { const a = record(value); return { citizenId: id(a.citizenId), minutes: number(a.minutes, 1, 360), benefitsGranted: bool(a.benefitsGranted), portionConsumed: bool(a.portionConsumed) } }) }
  if (result.endMinute - result.startMinute !== 360 || result.finished && result.reservedFood !== 0 || result.mode === 'Gathering' && result.initialFood !== 0) throw new Error('Invalid festival state.')
  return result
}

export function festivalStatus(festival: Festival): string {
  if (festival.finished) return festival.started ? 'Harvest festival remembered' : 'No gathering this year'
  return festival.started ? festival.mode === 'Feast' ? 'Harvest feast underway' : 'Harvest gathering underway' : 'Next harvest festival'
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
  if (order.kind === 'AttendFestival') return order.phase === 'Work' ? 'Celebrating' : 'Going to the festival'
  if (order.phase === 'Collect') return order.ingredients.length ? 'Collecting supplies' : 'Going to the meeting point'
  if (order.phase === 'Travel') return order.suppliesDelivered || !order.ingredients.length ? 'Going to the work site' : 'Carrying supplies'
  if (order.phase === 'Deliver') return order.cargoInTransit ? 'Bringing goods home' : 'Collecting finished goods'
  return 'Working'
}

export function festivalVisitorActivity(world: LivingWorld | null | undefined, citizen: { citizenId: string; currentAction: string; location: Point }): string | null {
  const visit = world?.festivalVisit
  if (!visit?.partyId || visit.citizenId !== citizen.citizenId || citizen.currentAction !== 'Idle') return null
  const festival = world?.festivals?.find(f => f.settlementId === visit.settlementId && f.startMinute === visit.startMinute && f.started && !f.finished)
  return festival && citizen.location.x === festival.location.x && citizen.location.y === festival.location.y ? 'Celebrating with relatives' : null
}
