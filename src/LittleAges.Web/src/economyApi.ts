export type Goods = { food: number; wood: number; stone: number }
export type HouseholdEconomy = { householdId: string; inventory: Goods; reserved: Goods; inTransitAndEscrow: Goods; wealth: number; standing: string }
export type Occupation = { citizenId: string; specialization: string; assignedMinute: number }
export type Offer = { householdId: string; resource: string; surplus: number; requested: number; updatedMinute: number }
export type Trade = { tradeId: string; marketId: string; householdA: string; householdB: string; resourceA: string; quantityA: number; resourceB: string; quantityB: number; carrierA: string | null; carrierB: string | null; pickedA: boolean; pickedB: boolean; deliveredA: boolean; deliveredB: boolean; status: string; createdMinute: number; closedMinute: number | null }
export type EconomicEvent = { eventId: string; worldMinute: number; kind: string; householdId: string; recipientHouseholdId: string | null; goods: Goods }
export type Recoverable = { cacheId: string; householdId: string | null; location: { x: number; y: number }; resource: string; quantity: number }
export type PublicSupplyTrade = { transactionId: string; worldMinute: number; citizenId: string; householdId: string; resource: string; quantity: number; foodPaid: number }
export type Economy = { enabled: true; version: number; communalPercent: number; commons: Goods; produced: Goods; foodConsumed: number; emergencyFoodConsumed: number; publicWorkPaid: number; reservedPublicFood: number; households: HouseholdEconomy[]; occupations: Occupation[]; offers: Offer[]; trades: Trade[]; events: EconomicEvent[]; recoverable: Recoverable[]; totalHouseholds: number; totalOccupations: number; totalOffers: number; totalTrades: number; totalEvents: number; totalRecoverable: number; publicSupplyTrades: PublicSupplyTrade[]; totalPublicSupplyTrades: number; offset: number; limit: number; wealth: { minimum: number; maximum: number; total: number } } | { enabled: false }
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const count = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0
const id = (v: unknown) => typeof v === 'string' && /^[1-9]\d*$/.test(v) && BigInt(v) <= 9223372036854775807n
const nullableId = (v: unknown) => v === null || id(v)
const goods = (v: unknown) => record(v) && ['food', 'wood', 'stone'].every(k => count(v[k]))
const resource = (v: unknown) => ['Food', 'Wood', 'Stone'].includes(String(v))
const weight = (v: unknown) => v === 'Food' ? 1 : v === 'Wood' ? 2 : 3
export function parseEconomy(value: unknown): Economy {
  if (!record(value) || typeof value.enabled !== 'boolean') throw new Error('Invalid economy information.')
  if (!value.enabled) return { enabled: false }
  if (value.version !== 1 || value.communalPercent !== 20 || !goods(value.commons) || !goods(value.produced) || !record(value.wealth) || !['minimum', 'maximum', 'total'].every(k => count((value.wealth as Record<string, unknown>)[k])) ||
    ['foodConsumed', 'emergencyFoodConsumed', 'publicWorkPaid', 'reservedPublicFood', 'totalHouseholds', 'totalOccupations', 'totalOffers', 'totalTrades', 'totalEvents', 'totalRecoverable', 'totalPublicSupplyTrades', 'offset', 'limit'].some(k => !count(value[k]))) throw new Error('Invalid economy totals.')
  for (const key of ['households', 'occupations', 'offers', 'trades', 'events', 'recoverable', 'publicSupplyTrades']) if (!Array.isArray(value[key]) || value[key].length > 200) throw new Error('Invalid economy page.')
  for (const h of value.households as unknown[]) if (!record(h) || !id(h.householdId) || !goods(h.inventory) || !goods(h.reserved) || !goods(h.inTransitAndEscrow) || !count(h.wealth) || typeof h.standing !== 'string') throw new Error('Invalid household inventory.')
  for (const a of value.occupations as unknown[]) if (!record(a) || !id(a.citizenId) || !count(a.assignedMinute) || !['Farmer', 'Forager', 'Woodcutter', 'Stoneworker', 'Builder', 'Hauler'].includes(String(a.specialization))) throw new Error('Invalid occupation.')
  for (const o of value.offers as unknown[]) if (!record(o) || !id(o.householdId) || !resource(o.resource) || !count(o.surplus) || !count(o.requested) || !count(o.updatedMinute)) throw new Error('Invalid offer.')
  for (const t of value.trades as unknown[]) if (!record(t) || !['tradeId', 'marketId', 'householdA', 'householdB'].every(k => id(t[k])) || !nullableId(t.carrierA) || !nullableId(t.carrierB) || !resource(t.resourceA) || !resource(t.resourceB) || !count(t.quantityA) || !count(t.quantityB) || t.quantityA * weight(t.resourceA) !== t.quantityB * weight(t.resourceB) || !count(t.createdMinute) || !(t.closedMinute === null || count(t.closedMinute)) || !['Reserved', 'Completed', 'Cancelled'].includes(String(t.status)) || !['pickedA', 'pickedB', 'deliveredA', 'deliveredB'].every(k => typeof t[k] === 'boolean') || t.status === 'Completed' && (!t.deliveredA || !t.deliveredB)) throw new Error('Invalid physical trade.')
  for (const e of value.events as unknown[]) if (!record(e) || !id(e.eventId) || !id(e.householdId) || !nullableId(e.recipientHouseholdId) || !count(e.worldMinute) || !goods(e.goods) || !['Inheritance', 'HouseholdMerged', 'FirstTrade'].includes(String(e.kind))) throw new Error('Invalid economic event.')
  for (const g of value.recoverable as unknown[]) if (!record(g) || !id(g.cacheId) || !nullableId(g.householdId) || !record(g.location) || !count(g.location.x) || !count(g.location.y) || !resource(g.resource) || !count(g.quantity)) throw new Error('Invalid interrupted cargo.')
  for (const t of value.publicSupplyTrades as unknown[]) if (!record(t) || !id(t.transactionId) || !id(t.citizenId) || !id(t.householdId) || !count(t.worldMinute) || !['Wood', 'Stone'].includes(String(t.resource)) || !count(t.quantity) || t.foodPaid !== t.quantity * weight(t.resource)) throw new Error('Invalid public supply exchange.')
  return value as Economy
}
