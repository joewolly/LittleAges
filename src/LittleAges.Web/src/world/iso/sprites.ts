import spriteManifest from '../sprite-manifest.json'
import type { Season } from './seasons'

/** Size and tile-centre anchor of a sprite, in projection units (half a tile's width). */
export type SpritePlacement = { width: number; height: number; anchorX: number; anchorY: number }

const placements = spriteManifest.sprites as Record<string, SpritePlacement>
const SPRITE_ROOT = '/assets/sprites'
const MAX_RASTERS = 320
const SPRITE_POP = 'saturate(1.18) brightness(1.03) contrast(1.04)'

export function spritePlacement(key: string): SpritePlacement | null {
  return placements[key] ?? null
}

export function seasonalKey(season: Season, name: string): string {
  return `${season}/${name}`
}

/** Sprites to preload for a season: its ground art and the shared markers. Villagers load on first use. */
export function spriteKeysFor(season: Season): string[] {
  return Object.keys(placements).filter(key => key.startsWith(`${season}/`) || key.startsWith('common/') || key.startsWith('people/carry-'))
}

/** Rasterized sprites are bucketed by half-octaves of zoom so zooming reuses bitmaps without blurring. */
export function zoomBucket(pixelsPerUnit: number): number {
  return Math.pow(2, Math.round(Math.log2(Math.max(1, pixelsPerUnit)) * 2) / 2)
}

type Raster = HTMLCanvasElement

/**
 * Loads SVG sprites as images and keeps crisp raster copies per zoom bucket.
 * Missing or failed images are simply skipped by the renderer.
 */
export class SpriteKit {
  private images = new Map<string, HTMLImageElement>()
  private ready = new Set<string>()
  private rasters = new Map<string, Raster>()
  private listeners = new Set<() => void>()

  onChange(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  load(keys: readonly string[]) {
    for (const key of keys) {
      if (this.images.has(key) || typeof Image === 'undefined') continue
      const image = new Image()
      image.decoding = 'async'
      image.onload = () => { this.ready.add(key); for (const listener of this.listeners) listener() }
      image.src = `${SPRITE_ROOT}/${key}.svg`
      this.images.set(key, image)
    }
  }

  isReady(key: string): boolean { return this.ready.has(key) }

  /** A bitmap of `key` drawn at `bucket` device pixels per unit, or null until the SVG has loaded. */
  raster(key: string, bucket: number): Raster | null {
    if (!this.ready.has(key)) {
      if (placements[key]) this.load([key])
      return null
    }
    const cacheKey = `${key}@${bucket}`
    const cached = this.rasters.get(cacheKey)
    if (cached) return cached
    const placement = placements[key]
    const image = this.images.get(key)
    if (!placement || !image || typeof document === 'undefined') return null
    const canvas = document.createElement('canvas')
    canvas.width = Math.max(1, Math.ceil(placement.width * bucket))
    canvas.height = Math.max(1, Math.ceil(placement.height * bucket))
    const context = canvas.getContext('2d')
    if (!context) return null
    // A little extra colour and light, baked in once per zoom level so it costs nothing per frame.
    context.filter = SPRITE_POP
    context.drawImage(image, 0, 0, canvas.width, canvas.height)
    // Keep a bounded set of bitmaps; zooming to a new level gradually replaces the old ones.
    if (this.rasters.size >= MAX_RASTERS) this.rasters.delete(this.rasters.keys().next().value as string)
    this.rasters.set(cacheKey, canvas)
    return canvas
  }
}
