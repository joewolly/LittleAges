import type { RoadGrade, RoadTile } from '../api'

export type RoadPoint = { x: number; y: number }
export type RoadSegment = { from: RoadPoint; to: RoadPoint; grade: RoadGrade }
export type RoadStyle = { color: string; edge: string | null; width: number; dashed: boolean; lift: number; thickness: number }

export const ROAD_RANK: Record<RoadGrade, number> = { Track: 1, Trail: 2, Road: 3 }

/** Width is in tiles. Each grade differs in width, color, and finish so the three read apart at a glance. */
export const ROAD_STYLES: Record<RoadGrade, RoadStyle> = {
  Track: { color: '#9c7b4c', edge: null, width: 0.16, dashed: true, lift: 0.1, thickness: 0.02 },
  Trail: { color: '#6b4527', edge: null, width: 0.32, dashed: false, lift: 0.11, thickness: 0.03 },
  Road: { color: '#67625c', edge: '#efe6d2', width: 0.56, dashed: false, lift: 0.12, thickness: 0.07 },
}

const lowerGrade = (first: RoadGrade, second: RoadGrade): RoadGrade => ROAD_RANK[first] <= ROAD_RANK[second] ? first : second

/**
 * Joins neighboring graded tiles into center-to-center segments carrying the lower grade of the two.
 * A diagonal is skipped when an orthogonal corner already links the pair, so bends don't draw triangles.
 * Segments come back ordered from Track to Road, so better grades draw on top.
 */
export function roadSegments(tiles: readonly RoadTile[]): RoadSegment[] {
  const grades = new globalThis.Map(tiles.map(tile => [`${tile.x},${tile.y}`, tile.grade]))
  const at = (x: number, y: number) => grades.get(`${x},${y}`)
  const segments: RoadSegment[] = []
  for (const tile of tiles) {
    const link = (dx: number, dy: number) => {
      const other = at(tile.x + dx, tile.y + dy)
      if (other !== undefined) segments.push({ from: { x: tile.x, y: tile.y }, to: { x: tile.x + dx, y: tile.y + dy }, grade: lowerGrade(tile.grade, other) })
    }
    link(1, 0)
    link(0, 1)
    if (at(tile.x + 1, tile.y) === undefined && at(tile.x, tile.y + 1) === undefined) link(1, 1)
    if (at(tile.x - 1, tile.y) === undefined && at(tile.x, tile.y + 1) === undefined) link(-1, 1)
  }
  return segments.sort((first, second) => ROAD_RANK[first.grade] - ROAD_RANK[second.grade])
}

export function tilesByGrade(tiles: readonly RoadTile[]): Record<RoadGrade, RoadTile[]> {
  return {
    Track: tiles.filter(tile => tile.grade === 'Track'),
    Trail: tiles.filter(tile => tile.grade === 'Trail'),
    Road: tiles.filter(tile => tile.grade === 'Road'),
  }
}
