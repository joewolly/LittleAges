import type { Citizen, Map as WorldMap, Settlement, SettlementSite, Structure } from '../../api'
import type { LivingOrder, LivingWorld } from '../../living'
import { detailVariant, stableVisualHash } from '../visuals'
import type { Season } from './seasons'
import { seasonalKey } from './sprites'
import { CHUNK_TILES, isForested, isWilderness, terrainAt } from './terrain'
import type { ShelterTier } from './tiers'

/** One sprite placed on the map. `x`/`y` are world tile coordinates of its ground anchor. */
export type SceneSprite = { key: string; x: number; y: number; scale: number; depth: number; ground?: boolean }

const CROP_SPRITES: Record<string, string> = { Fallow: 'farm-fallow', Planted: 'farm-planted', Growing: 'farm-growing', Harvest: 'farm-harvest', Dormant: 'farm-dormant' }
const FACILITY_SPRITES: Record<string, string> = { Hearth: 'hearth', Loom: 'loom', CareHouse: 'carehouse' }

export function structureSprite(structure: Structure, tier: ShelterTier): string {
  if (structure.status !== 'Complete') return 'construction'
  switch (structure.type) {
    case 'Shelter': return `shelter-${tier}`
    case 'Farm': return CROP_SPRITES[structure.cropStage ?? 'Fallow'] ?? 'farm-fallow'
    case 'Marketplace': return 'marketplace'
    default: return structure.type.toLowerCase()
  }
}

export function fieldSprite(field: LivingWorld['fields'][number]): string {
  if (field.yieldRemaining > 0) return 'field-ripe'
  if (field.growth >= 6000) return 'field-green'
  if (field.growth > 0) return 'field-sprout'
  return 'field-bare'
}

/** Ground-level sprites draw before anything standing on the same tile. */
function depth(x: number, y: number, ground = false): number { return x + y + (ground ? -0.5 : 0) }

/**
 * Static scenery for a season, bucketed by terrain chunk so the renderer only
 * sorts what is on screen. Decorative trees come from the world seed and tile
 * coordinates, never from randomness, so every observer sees the same forest.
 */
export function buildStaticScene(input: {
  map: WorldMap
  season: Season
  worldSeed: string | null
  structures: readonly Structure[]
  settlement: Settlement | null
  living: LivingWorld | null | undefined
  tier: ShelterTier
}): Map<string, SceneSprite[]> {
  const { map, season, worldSeed, structures, settlement, living, tier } = input
  const seed = worldSeed ?? 'world'
  const buckets = new Map<string, SceneSprite[]>()
  const add = (sprite: SceneSprite) => {
    const key = `${Math.floor(sprite.x / CHUNK_TILES)}:${Math.floor(sprite.y / CHUNK_TILES)}`
    const list = buckets.get(key)
    if (list) list.push(sprite)
    else buckets.set(key, [sprite])
  }
  const occupied = new Set<string>()
  // Decorative trees keep a one-tile clearing around anything people built.
  const clear = (x: number, y: number) => { for (let dy = -1; dy <= 1; dy += 1) for (let dx = -1; dx <= 1; dx += 1) occupied.add(`${x + dx},${y + dy}`) }
  const quantities = new Map((settlement?.resources ?? []).map(resource => [resource.resourceNodeId, resource.currentQuantity]))
  for (const structure of structures) {
    clear(structure.location.x, structure.location.y)
    const ground = structure.type === 'Farm' && structure.status === 'Complete'
    add({ key: seasonalKey(season, structureSprite(structure, tier)), x: structure.location.x, y: structure.location.y, scale: 1, depth: depth(structure.location.x, structure.location.y, ground), ground })
  }
  for (const field of living?.fields ?? []) {
    clear(field.location.x, field.location.y)
    add({ key: seasonalKey(season, fieldSprite(field)), x: field.location.x, y: field.location.y, scale: 1, depth: depth(field.location.x, field.location.y, true), ground: true })
  }
  for (const facility of living?.facilities ?? []) {
    clear(facility.location.x, facility.location.y)
    const name = FACILITY_SPRITES[facility.kind]
    if (name) add({ key: seasonalKey(season, name), x: facility.location.x, y: facility.location.y, scale: 1, depth: depth(facility.location.x, facility.location.y) })
  }
  for (const resource of map.resources) {
    const { x, y } = resource.location
    occupied.add(`${x},${y}`)
    const remaining = quantities.get(resource.resourceNodeId) ?? resource.maximumQuantity
    if (remaining <= 0) continue
    const variant = detailVariant(worldSeed, `resource-${resource.resourceType}`, resource.resourceNodeId)
    const fullness = Math.max(0.2, Math.min(1, remaining / Math.max(1, resource.maximumQuantity)))
    const scale = (0.78 + ((variant >>> 8) & 15) / 60) * Math.sqrt(fullness)
    const name = resource.resourceType === 'Food' ? 'berry' : resource.resourceType === 'Stone' ? 'rocks' : variant % 3 === 0 ? `oak-${1 + (variant >>> 4) % 3}` : 'pine'
    add({ key: seasonalKey(season, name), x, y, scale, depth: depth(x, y) })
  }
  for (let y = 0; y < map.height; y += 1) for (let x = 0; x < map.width; x += 1) {
    const terrain = terrainAt(map, x, y)
    if (!isForested(terrain) || occupied.has(`${x},${y}`)) continue
    const hash = stableVisualHash(seed, 'decor-tree', x, y)
    if (!isWilderness(terrain) && hash % 4 !== 0) continue
    const jitterX = ((hash >>> 3) % 100) / 100 * 0.5 - 0.25
    const jitterY = ((hash >>> 10) % 100) / 100 * 0.5 - 0.25
    const name = isWilderness(terrain) || (hash >>> 17) % 3 !== 0 ? 'pine' : `oak-${1 + (hash >>> 24) % 3}`
    add({ key: seasonalKey(season, name), x: x + jitterX, y: y + jitterY, scale: 0.75 + ((hash >>> 20) % 40) / 100, depth: depth(x + jitterX, y + jitterY) })
  }
  return buckets
}

/** Where the default view looks: the settlement's buildings, not wandering gatherers. */
export function cameraFocus(map: WorldMap, structures: readonly Structure[], focusedSite: SettlementSite | null): { x: number; y: number } {
  if (focusedSite) return { ...focusedSite.site }
  const prominent = structures.filter(structure => structure.type !== 'Stockpile')
  const focus = prominent.length > 0 ? prominent : structures
  if (focus.length === 0) return { ...map.startingSite }
  return { x: focus.reduce((sum, s) => sum + s.location.x, 0) / focus.length, y: focus.reduce((sum, s) => sum + s.location.y, 0) / focus.length }
}

export function siteSprites(sites: readonly SettlementSite[], selectedId: string | null): SceneSprite[] {
  if (sites.length < 2) return []
  return sites.map(site => ({ key: site.settlementId === selectedId ? 'common/site-selected' : 'common/site', x: site.site.x + 0.35, y: site.site.y + 0.35, scale: 1, depth: depth(site.site.x + 0.35, site.site.y + 0.35) }))
}

export function animalSprites(living: LivingWorld | null | undefined): SceneSprite[] {
  return (living?.animals ?? []).map(animal => ({ key: animal.predator ? 'common/predator' : 'common/animal', x: animal.location.x, y: animal.location.y, scale: 1, depth: depth(animal.location.x, animal.location.y) }))
}

export function sortByDepth(sprites: SceneSprite[]): SceneSprite[] {
  return sprites.sort((first, second) => first.depth - second.depth || first.y - second.y)
}

const FOOD_GOODS = new Set(['Food', 'Meal', 'Grain', 'PreservedFood'])
const WOOD_GOODS = new Set(['Wood', 'Fuel'])

/** Which villager sprite variant shows what a citizen is carrying right now. */
export function carriedSprite(citizen: Citizen, order: LivingOrder | undefined): string {
  const carried = citizen.carriedResource ?? (order?.cargoInTransit ? order.cargo[0]?.good : order?.phase === 'Travel' && !order.suppliesDelivered ? order.ingredients[0]?.resource : null)
  if (!carried) return 'empty'
  if (FOOD_GOODS.has(carried)) return 'food'
  if (WOOD_GOODS.has(carried)) return 'wood'
  return carried === 'Stone' ? 'stone' : 'empty'
}
