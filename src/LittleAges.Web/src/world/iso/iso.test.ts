import { describe, expect, it } from 'vitest'
import type { Citizen, Map as WorldMap, Structure } from '../../api'
import type { LivingWorld } from '../../living'
import { MAX_ZOOM, MIN_ZOOM, clampCamera, homeZoom, panCamera, screenToWorld, visibleTiles, worldToScreen, zoomCameraAt } from './projection'
import { buildStaticScene, cameraFocus, fieldSprite, structureSprite, villagerLook, villagerSprite } from './scene'
import { seasonAt } from './seasons'
import { seasonalKey, spriteKeysFor, spritePlacement } from './sprites'
import { MASONRY_MINUTES, TIMBER_MINUTES, shelterTier } from './tiers'

const DAY = 24 * 60

function structure(type: Structure['type'], status: Structure['status'] = 'Complete', extra: Partial<Structure> = {}): Structure {
  return { structureId: String(Math.random()).slice(2, 8), type, status, location: { x: 4, y: 4 }, startedMinute: 0, completedMinute: status === 'Complete' ? 10 : null, requiredWood: 1, deliveredWood: 1, requiredStone: 1, deliveredStone: 1, requiredWork: 1, completedWork: 1, condition: 10000, capacity: null, storageBonus: null, constructionMultiplierBasisPoints: null, currentOccupantIds: [], contributions: [], ...extra }
}

function citizen(woodcuttingMinutes = 0, stoneworkingMinutes = 0): Citizen {
  return { lifetimeWorkActivity: { foragingMinutes: 0, woodcuttingMinutes, stoneworkingMinutes, constructionMinutes: 0, haulingMinutes: 0 } } as Citizen
}

function living(knowledge: string[]): LivingWorld {
  return { people: [{ citizenId: '1', knowledge, deathObserved: false }], fields: [], facilities: [], animals: [], orders: [] } as unknown as LivingWorld
}

const map: WorldMap = { width: 32, height: 32, terrain: Array(32 * 32).fill(2), elevation: Array(32 * 32).fill(5000), resources: [], startingSite: { x: 16, y: 16 } }

describe('isometric projection', () => {
  const camera = { x: 10, y: 20, zoom: 32 }

  it('places the camera target at the screen centre and round-trips screen points', () => {
    expect(worldToScreen(camera, 800, 600, 10, 20)).toEqual({ x: 400, y: 300 })
    const point = worldToScreen(camera, 800, 600, 13.5, 17.25)
    const back = screenToWorld(camera, 800, 600, point.x, point.y)
    expect(back.x).toBeCloseTo(13.5)
    expect(back.y).toBeCloseTo(17.25)
  })

  it('draws a tile as a diamond twice as wide as it is tall', () => {
    const origin = worldToScreen(camera, 800, 600, 10, 20)
    expect(worldToScreen(camera, 800, 600, 11, 20).x - origin.x).toBe(32)
    expect(worldToScreen(camera, 800, 600, 11, 20).y - origin.y).toBe(16)
    expect(worldToScreen(camera, 800, 600, 10, 20, 1).y).toBe(origin.y - 32)
  })

  it('pans with the pointer and zooms around the cursor', () => {
    const panned = panCamera(camera, 64, 0)
    expect(worldToScreen(panned, 800, 600, 10, 20).x).toBeCloseTo(464)
    const before = screenToWorld(camera, 800, 600, 600, 150)
    const zoomed = zoomCameraAt(camera, 800, 600, 600, 150, 2)
    const after = screenToWorld(zoomed, 800, 600, 600, 150)
    expect(zoomed.zoom).toBe(64)
    expect(after.x).toBeCloseTo(before.x)
    expect(after.y).toBeCloseTo(before.y)
  })

  it('keeps the camera on the map and within zoom limits', () => {
    expect(clampCamera({ x: -5, y: 400, zoom: 1000 }, 160, 160)).toEqual({ x: 0, y: 159, zoom: MAX_ZOOM })
    expect(clampCamera({ x: 5, y: 5, zoom: 0.1 }, 160, 160).zoom).toBe(MIN_ZOOM)
  })

  it('reports visible tiles around the camera and picks a closer home view on phones', () => {
    const bounds = visibleTiles(camera, 800, 600, 2)
    expect(bounds.minX).toBeLessThan(10)
    expect(bounds.maxX).toBeGreaterThan(10)
    expect(bounds.minY).toBeLessThan(20)
    expect(bounds.maxY).toBeGreaterThan(20)
    expect(390 / homeZoom(390)).toBeLessThan(1440 / homeZoom(1440))
  })
})

describe('seasons', () => {
  it('follows the 360-day calendar in 90-day seasons', () => {
    expect(seasonAt(0)).toBe('spring')
    expect(seasonAt(89 * DAY + 1439)).toBe('spring')
    expect(seasonAt(90 * DAY)).toBe('summer')
    expect(seasonAt(180 * DAY)).toBe('autumn')
    expect(seasonAt(270 * DAY)).toBe('winter')
    expect(seasonAt(360 * DAY)).toBe('spring')
  })

  it('has every sprite the renderer can ask for in each season', () => {
    for (const season of ['spring', 'summer', 'autumn', 'winter'] as const) {
      const keys = new Set(spriteKeysFor(season))
      for (const name of ['shelter-1', 'shelter-5', 'stockpile', 'workshop', 'granary', 'marketplace', 'construction', 'farm-harvest', 'field-ripe', 'hearth', 'loom', 'carehouse', 'oak-1', 'oak-3', 'pine', 'berry', 'rocks']) expect(keys.has(`${season}/${name}`)).toBe(true)
      expect(keys.has('people/carry-stone')).toBe(true)
      expect([...keys].some(key => key.startsWith('people/villager-'))).toBe(false)
      expect(keys.has('common/site-selected')).toBe(true)
    }
    expect(spritePlacement('spring/shelter-3')?.anchorY).toBeGreaterThan(0)
  })
})

describe('villagers', () => {
  it('gives each citizen a stable look, with grey-haired looks for elders', () => {
    const adult = { citizenId: '12', lifeStage: 'Adult' } as Citizen
    const elder = { citizenId: '12', lifeStage: 'Elder' } as Citizen
    expect(villagerLook('42', adult)).toBe(villagerLook('42', adult))
    expect(villagerLook('42', adult)).toBeLessThan(8)
    expect(villagerLook('42', elder)).toBeGreaterThanOrEqual(8)
    const ids = Array.from({ length: 60 }, (_, i) => villagerLook('42', { citizenId: String(i + 1), lifeStage: 'Adult' } as Citizen))
    expect(new Set(ids).size).toBe(8)
    for (const frame of [0, 1] as const) {
      expect(spritePlacement(villagerSprite('42', adult, frame))).not.toBeNull()
      expect(spritePlacement(villagerSprite('42', elder, frame))).not.toBeNull()
    }
  })
})

describe('shelter tiers', () => {
  const workshop = [structure('Workshop')]

  it('starts as a hide tent and steps up with techniques, a workshop and lifetime work', () => {
    expect(shelterTier(living([]), [], [])).toBe(1)
    expect(shelterTier(living(['Cultivation']), [], [])).toBe(2)
    expect(shelterTier(living(['Cultivation', 'Toolmaking']), [], [])).toBe(2)
    expect(shelterTier(living(['Cultivation', 'Toolmaking']), workshop, [])).toBe(3)
    expect(shelterTier(living(['Cultivation', 'Toolmaking']), workshop, [citizen(TIMBER_MINUTES)])).toBe(4)
    expect(shelterTier(living(['Cultivation', 'Toolmaking']), workshop, [citizen(TIMBER_MINUTES / 2), citizen(TIMBER_MINUTES / 2, MASONRY_MINUTES)])).toBe(5)
  })

  it('ignores an unfinished workshop and techniques known only by the dead', () => {
    expect(shelterTier(living(['Cultivation', 'Toolmaking']), [structure('Workshop', 'UnderConstruction')], [])).toBe(2)
    const forgotten = { ...living(['Cultivation']), people: [{ citizenId: '1', knowledge: ['Cultivation'], deathObserved: true }] } as unknown as LivingWorld
    expect(shelterTier(forgotten, [], [])).toBe(1)
  })

  it('lets a finished workshop stand in for techniques in worlds without living rules', () => {
    expect(shelterTier(null, [], [])).toBe(1)
    expect(shelterTier(null, workshop, [])).toBe(3)
  })
})

describe('scene', () => {
  it('chooses sprites from canonical structure state', () => {
    expect(structureSprite(structure('Shelter'), 4)).toBe('shelter-4')
    expect(structureSprite(structure('Shelter', 'UnderConstruction'), 4)).toBe('construction')
    expect(structureSprite(structure('Farm', 'Complete', { cropStage: 'Harvest' }), 1)).toBe('farm-harvest')
    expect(structureSprite(structure('Farm'), 1)).toBe('farm-fallow')
    expect(structureSprite(structure('Marketplace'), 1)).toBe('marketplace')
    expect(structureSprite(structure('Storehouse'), 1)).toBe('storehouse')
    for (const season of ['spring', 'summer', 'autumn', 'winter'] as const) expect(spritePlacement(seasonalKey(season, 'storehouse'))).not.toBeNull()
    expect(fieldSprite({ id: '1', location: { x: 0, y: 0 }, growth: 0, moisture: 0, condition: 0, yieldRemaining: 3, harvests: 0 })).toBe('field-ripe')
    expect(fieldSprite({ id: '1', location: { x: 0, y: 0 }, growth: 7000, moisture: 0, condition: 0, yieldRemaining: 0, harvests: 0 })).toBe('field-green')
  })

  it('hides depleted resources and places the same decorative forest every time', () => {
    const forest: WorldMap = { ...map, terrain: map.terrain.map((_, i) => (i % 32 < 8 ? 5 : 2)), resources: [
      { resourceNodeId: '1', resourceType: 'Wood', location: { x: 20, y: 20 }, maximumQuantity: 100, regenerationPotential: 1 },
      { resourceNodeId: '2', resourceType: 'Stone', location: { x: 21, y: 20 }, maximumQuantity: 100, regenerationPotential: 1 },
    ] }
    const settlement = { resources: [{ resourceNodeId: '1', resourceType: 'Wood', currentQuantity: 0 }, { resourceNodeId: '2', resourceType: 'Stone', currentQuantity: 50 }] } as never
    const build = () => [...buildStaticScene({ map: forest, season: 'winter', worldSeed: '42', structures: [], settlement, living: null, tier: 1 }).values()].flat()
    const sprites = build()
    expect(sprites.some(sprite => sprite.x === 20 && sprite.y === 20)).toBe(false)
    expect(sprites.find(sprite => sprite.x === 21 && sprite.y === 20)?.key).toBe('winter/rocks')
    expect(sprites.filter(sprite => sprite.x < 8).length).toBeGreaterThan(200)
    expect(build()).toEqual(sprites)
  })

  it('keeps decorative trees out of a clearing around buildings', () => {
    const wilderness: WorldMap = { ...map, terrain: map.terrain.map(() => 5) }
    const sprites = [...buildStaticScene({ map: wilderness, season: 'spring', worldSeed: '42', structures: [structure('Shelter', 'Complete', { location: { x: 10, y: 10 } })], settlement: null, living: null, tier: 1 }).values()].flat()
    const near = sprites.filter(sprite => Math.abs(sprite.x - 10) < 1.3 && Math.abs(sprite.y - 10) < 1.3)
    expect(near.map(sprite => sprite.key)).toEqual(['spring/shelter-1'])
  })

  it('frames the settlement buildings rather than stockpiles or the map corner', () => {
    expect(cameraFocus(map, [], null)).toEqual({ x: 16, y: 16 })
    const houses = [structure('Shelter', 'Complete', { location: { x: 10, y: 10 } }), structure('Workshop', 'Complete', { location: { x: 14, y: 12 } }), structure('Stockpile', 'Complete', { location: { x: 30, y: 30 } })]
    expect(cameraFocus(map, houses, null)).toEqual({ x: 12, y: 11 })
  })
})
