import { describe, expect, it } from 'vitest'
import { ROAD_STYLES, roadSegments, tilesByGrade } from './roads'

describe('road network presentation', () => {
  it('joins neighbors with the lower grade and draws better grades last', () => {
    const segments = roadSegments([{ x: 0, y: 0, grade: 'Road' }, { x: 1, y: 0, grade: 'Road' }, { x: 2, y: 0, grade: 'Track' }])
    expect(segments).toEqual([
      { from: { x: 1, y: 0 }, to: { x: 2, y: 0 }, grade: 'Track' },
      { from: { x: 0, y: 0 }, to: { x: 1, y: 0 }, grade: 'Road' },
    ])
  })

  it('links diagonals only where no orthogonal corner already joins the tiles', () => {
    expect(roadSegments([{ x: 0, y: 0, grade: 'Trail' }, { x: 1, y: 1, grade: 'Trail' }])).toEqual([{ from: { x: 0, y: 0 }, to: { x: 1, y: 1 }, grade: 'Trail' }])
    const bend = roadSegments([{ x: 0, y: 0, grade: 'Trail' }, { x: 1, y: 0, grade: 'Trail' }, { x: 1, y: 1, grade: 'Trail' }])
    expect(bend).toHaveLength(2)
    expect(bend.every(segment => segment.from.x === segment.to.x || segment.from.y === segment.to.y)).toBe(true)
    expect(roadSegments([{ x: 1, y: 0, grade: 'Track' }, { x: 0, y: 1, grade: 'Track' }])).toEqual([{ from: { x: 1, y: 0 }, to: { x: 0, y: 1 }, grade: 'Track' }])
  })

  it('keeps each grade visually distinct', () => {
    const styles = Object.values(ROAD_STYLES)
    expect(new Set(styles.map(style => style.color)).size).toBe(3)
    expect(ROAD_STYLES.Track.width).toBeLessThan(ROAD_STYLES.Trail.width)
    expect(ROAD_STYLES.Trail.width).toBeLessThan(ROAD_STYLES.Road.width)
    expect(ROAD_STYLES.Track.dashed).toBe(true)
    expect(ROAD_STYLES.Road.edge).not.toBeNull()
    expect(tilesByGrade([{ x: 0, y: 0, grade: 'Trail' }]).Trail).toHaveLength(1)
  })
})
