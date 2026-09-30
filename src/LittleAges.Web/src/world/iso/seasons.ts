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
  spring: { grass: ['#8acb4c', '#82c345'], forest: ['#6fb13f', '#69ab3b'], wilderness: ['#4f8f36', '#4a8932'], rocky: ['#b9b09a', '#b1a893'], water: ['#56b8de', '#5cc0e4'], shore: ['#e6cf92', '#e1c887'], ripple: '#bfe9f7', pebble: '#8f887c', backdrop: '#5e9a36' },
  summer: { grass: ['#9ccf4a', '#95c843'], forest: ['#7cb33c', '#76ad38'], wilderness: ['#588f33', '#538a30'], rocky: ['#c2b79c', '#bab095'], water: ['#3fb0e0', '#48b8e6'], shore: ['#efd592', '#e9cd86'], ripple: '#c9f0ff', pebble: '#958d7e', backdrop: '#6c9e34' },
  autumn: { grass: ['#b5b54e', '#acad48'], forest: ['#9b9f42', '#95993e'], wilderness: ['#77803a', '#727b37'], rocky: ['#b3a78e', '#aca088'], water: ['#4d9fbf', '#539fc4'], shore: ['#dcc48c', '#d6bc82'], ripple: '#b4dcea', pebble: '#877f71', backdrop: '#7f8a3a' },
  winter: { grass: ['#f1f5f9', '#e8eef4'], forest: ['#e3eaf0', '#dde5ec'], wilderness: ['#d3dde6', '#cdd8e2'], rocky: ['#d8d9d8', '#d0d2d1'], water: ['#c9e6f2', '#c1e0ee'], shore: ['#e9edf1', '#e2e8ee'], ripple: '#ffffff', pebble: '#9ea4a8', backdrop: '#cfd9e2' },
}
