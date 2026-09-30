export const SEASONS = ['spring', 'summer', 'autumn', 'winter'] as const
export type Season = typeof SEASONS[number]

const MINUTES_PER_DAY = 24 * 60
const DAYS_PER_SEASON = 90

/** The calendar season for a world minute: four 90-day seasons in a 360-day year. */
export function seasonAt(worldMinute: number): Season {
  const dayOfYear = Math.floor(Math.max(0, worldMinute) / MINUTES_PER_DAY) % (DAYS_PER_SEASON * 4)
  return SEASONS[Math.floor(dayOfYear / DAYS_PER_SEASON)]
}

export type TerrainPalette = {
  grass: readonly [string, string]
  forest: readonly [string, string]
  wilderness: readonly [string, string]
  rocky: readonly [string, string]
  water: readonly [string, string]
  shore: readonly [string, string]
  ripple: string
  pebble: string
  backdrop: string
}

/** Ground colours per season. Pairs alternate in a checkerboard like a lawn. */
export const TERRAIN_PALETTES: Record<Season, TerrainPalette> = {
  spring: { grass: ['#8fd14e', '#87ca48'], forest: ['#7fc446', '#79bf42'], wilderness: ['#68ae3d', '#63a93a'], rocky: ['#cfc6b0', '#c8bfa9'], water: ['#56c0e8', '#5dc7ee'], shore: ['#efd99a', '#ead28f'], ripple: '#d2f2ff', pebble: '#a39b8e', backdrop: '#6fb13f' },
  summer: { grass: ['#a2d64c', '#9ad046'], forest: ['#8cc443', '#86bf3f'], wilderness: ['#70ad3a', '#6ba837'], rocky: ['#d4c9ad', '#cdc2a6'], water: ['#3fbbee', '#48c2f2'], shore: ['#f5dc98', '#f0d58c'], ripple: '#dcf6ff', pebble: '#a39a89', backdrop: '#7fb83c' },
  autumn: { grass: ['#c2bf52', '#bab84c'], forest: ['#abab47', '#a5a543'], wilderness: ['#8f943f', '#8a8f3c'], rocky: ['#c9bda2', '#c2b69b'], water: ['#4fb0d6', '#56b6db'], shore: ['#e8cf93', '#e2c88a'], ripple: '#c8ebf6', pebble: '#978e7e', backdrop: '#98a043' },
  winter: { grass: ['#f1f5f9', '#e8eef4'], forest: ['#e3eaf0', '#dde5ec'], wilderness: ['#d3dde6', '#cdd8e2'], rocky: ['#e0e2e1', '#d9dbda'], water: ['#cdeaf7', '#c5e4f3'], shore: ['#e9edf1', '#e2e8ee'], ripple: '#ffffff', pebble: '#9ea4a8', backdrop: '#cfd9e2' },
}
