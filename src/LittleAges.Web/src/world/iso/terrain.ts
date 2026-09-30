import type { Map as WorldMap } from '../../api'
import { stableVisualHash } from '../visuals'
import type { TerrainPalette } from './seasons'

export const CHUNK_TILES = 16
const FRESHWATER = 1
const GRASSLAND = 2
const FOREST = 3
const ROCKY = 4
const WILDERNESS = 5

export type TerrainChunk = { canvas: HTMLCanvasElement; originX: number; originY: number }

export function terrainAt(map: WorldMap, x: number, y: number): number {
  if (x < 0 || y < 0 || x >= map.width || y >= map.height) return 0
  return map.terrain[y * map.width + x]
}

function mix(hex: string, amount: number): string {
  const value = Number.parseInt(hex.slice(1), 16)
  const channel = (shift: number) => {
    const c = (value >> shift) & 255
    const next = amount >= 0 ? c + (255 - c) * amount : c * (1 + amount)
    return Math.max(0, Math.min(255, Math.round(next)))
  }
  return `rgb(${channel(16)},${channel(8)},${channel(0)})`
}

export function tileColor(map: WorldMap, palette: TerrainPalette, x: number, y: number): string {
  const terrain = terrainAt(map, x, y)
  const parity = (x + y) & 1
  const nearWater = terrain !== FRESHWATER && [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => terrainAt(map, x + dx, y + dy) === FRESHWATER)
  const base = terrain === FRESHWATER ? palette.water[parity]
    : nearWater && terrain !== ROCKY ? palette.shore[parity]
    : terrain === FOREST ? palette.forest[parity]
    : terrain === WILDERNESS ? palette.wilderness[parity]
    : terrain === ROCKY ? palette.rocky[parity]
    : palette.grass[parity]
  if (terrain === FRESHWATER) return base
  // Gentle relief: high ground catches a little more sun. Low ground keeps its colour, so the land stays bright.
  return mix(base, Math.max(0, map.elevation[y * map.width + x] / 10000 - 0.4) * 0.12)
}

/** Draws one chunk of flat diamond tiles at `scale` pixels per unit. Returns null outside the map. */
export function drawTerrainChunk(map: WorldMap, palette: TerrainPalette, chunkX: number, chunkY: number, scale: number, worldSeed: string): TerrainChunk | null {
  const x0 = chunkX * CHUNK_TILES
  const y0 = chunkY * CHUNK_TILES
  if (x0 >= map.width || y0 >= map.height || x0 < 0 || y0 < 0 || typeof document === 'undefined') return null
  const x1 = Math.min(map.width, x0 + CHUNK_TILES) - 1
  const y1 = Math.min(map.height, y0 + CHUNK_TILES) - 1
  const originX = x0 - y1 - 1
  const originY = (x0 + y0) / 2 - 0.5
  const canvas = document.createElement('canvas')
  canvas.width = Math.ceil((x1 - y0 + 1 - originX) * scale) + 2
  canvas.height = Math.ceil(((x1 + y1) / 2 + 0.5 - originY) * scale) + 2
  const context = canvas.getContext('2d')
  if (!context) return null
  context.lineJoin = 'round'
  context.lineWidth = 1
  for (let y = y0; y <= y1; y += 1) for (let x = x0; x <= x1; x += 1) {
    const cx = (x - y - originX) * scale
    const cy = ((x + y) / 2 - originY) * scale
    const color = tileColor(map, palette, x, y)
    context.fillStyle = color
    context.strokeStyle = color
    context.beginPath()
    context.moveTo(cx, cy - scale / 2)
    context.lineTo(cx + scale, cy)
    context.lineTo(cx, cy + scale / 2)
    context.lineTo(cx - scale, cy)
    context.closePath()
    context.fill()
    context.stroke()
    const terrain = terrainAt(map, x, y)
    const hash = stableVisualHash(worldSeed, 'ground', x, y)
    if (terrain === FRESHWATER && hash % 3 === 0) {
      const offset = ((hash >>> 4) % 100) / 100 - 0.5
      context.strokeStyle = palette.ripple
      context.lineWidth = Math.max(1, scale * 0.06)
      context.lineCap = 'round'
      context.beginPath()
      context.moveTo(cx - scale * 0.35 + offset * scale * 0.4, cy + offset * scale * 0.2)
      context.lineTo(cx + scale * 0.05 + offset * scale * 0.4, cy + offset * scale * 0.2 + scale * 0.2)
      context.stroke()
      context.lineWidth = 1
    } else if ((terrain === ROCKY && hash % 2 === 0) || (terrain === GRASSLAND && hash % 23 === 0)) {
      context.fillStyle = palette.pebble
      for (let pebble = 0; pebble < (terrain === ROCKY ? 3 : 1); pebble += 1) {
        const px = cx + (((hash >>> (pebble * 5)) % 100) / 100 - 0.5) * scale
        const py = cy + (((hash >>> (pebble * 5 + 3)) % 100) / 100 - 0.5) * scale * 0.4
        context.beginPath()
        context.ellipse(px, py, scale * 0.07, scale * 0.04, 0, 0, Math.PI * 2)
        context.fill()
      }
    }
  }
  return { canvas, originX, originY }
}

export function isForested(terrain: number): boolean { return terrain === FOREST || terrain === WILDERNESS }
export function isWilderness(terrain: number): boolean { return terrain === WILDERNESS }
