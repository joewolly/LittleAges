import { useLayoutEffect, useMemo, useRef } from 'react'
import * as THREE from 'three'
import type { Map, RoadGrade, RoadOverlay } from '../api'
import { ROAD_GRADES } from '../api'
import { ROAD_STYLES, roadSegments, tilesByGrade, type RoadSegment } from './roads'
import { worldToScene } from './visuals'

const xAxis = new THREE.Vector3(1, 0, 0)

function GradeSegments({ map, grade, segments }: { map: Map; grade: RoadGrade; segments: RoadSegment[] }) {
  const ref = useRef<THREE.InstancedMesh>(null)
  const edge = useRef<THREE.InstancedMesh>(null)
  const style = ROAD_STYLES[grade]
  useLayoutEffect(() => {
    const dummy = new THREE.Object3D()
    const from = new THREE.Vector3()
    const to = new THREE.Vector3()
    const direction = new THREE.Vector3()
    segments.forEach((segment, index) => {
      const start = worldToScene(map, segment.from.x, segment.from.y, style.lift)
      const end = worldToScene(map, segment.to.x, segment.to.y, style.lift)
      from.set(start.x, start.y, start.z)
      to.set(end.x, end.y, end.z)
      direction.subVectors(to, from)
      const length = direction.length()
      dummy.position.addVectors(from, to).multiplyScalar(0.5)
      dummy.quaternion.setFromUnitVectors(xAxis, direction.normalize())
      dummy.scale.set(length, style.thickness, style.width)
      dummy.updateMatrix()
      ref.current?.setMatrixAt(index, dummy.matrix)
      if (edge.current) {
        dummy.position.y -= style.thickness * 0.3
        dummy.scale.set(length, style.thickness * 0.6, style.width + 0.14)
        dummy.updateMatrix()
        edge.current.setMatrixAt(index, dummy.matrix)
      }
    })
    for (const mesh of [ref.current, edge.current]) {
      if (!mesh) continue
      mesh.instanceMatrix.needsUpdate = true
      mesh.computeBoundingSphere()
    }
  }, [map, segments, style])
  if (segments.length === 0) return null
  return <>
    {style.edge && <instancedMesh ref={edge} args={[undefined, undefined, segments.length]} receiveShadow>
      <boxGeometry args={[1, 1, 1]} />
      <meshStandardMaterial color={style.edge} roughness={1} />
    </instancedMesh>}
    <instancedMesh ref={ref} args={[undefined, undefined, segments.length]} receiveShadow>
      <boxGeometry args={[1, 1, 1]} />
      <meshStandardMaterial color={style.color} roughness={style.dashed ? 1 : 0.92} transparent={style.dashed} opacity={style.dashed ? 0.75 : 1} />
    </instancedMesh>
  </>
}

function GradeJoints({ map, grade, tiles }: { map: Map; grade: RoadGrade; tiles: Array<{ x: number; y: number }> }) {
  const ref = useRef<THREE.InstancedMesh>(null)
  const style = ROAD_STYLES[grade]
  useLayoutEffect(() => {
    const mesh = ref.current
    if (!mesh) return
    const dummy = new THREE.Object3D()
    tiles.forEach((tile, index) => {
      const point = worldToScene(map, tile.x, tile.y, style.lift)
      dummy.position.set(point.x, point.y, point.z)
      dummy.scale.set(style.width, style.thickness, style.width)
      dummy.updateMatrix()
      mesh.setMatrixAt(index, dummy.matrix)
    })
    mesh.instanceMatrix.needsUpdate = true
    mesh.computeBoundingSphere()
  }, [map, style, tiles])
  if (tiles.length === 0) return null
  return <instancedMesh ref={ref} args={[undefined, undefined, tiles.length]} receiveShadow>
    <cylinderGeometry args={[0.5, 0.5, 1, 10]} />
    <meshStandardMaterial color={style.color} roughness={1} transparent={style.dashed} opacity={style.dashed ? 0.75 : 1} />
  </instancedMesh>
}

/** Read-only M15 road overlay laid on the terrain: thin pale tracks, brown trails, and raised stone roads. */
export function RoadScene({ map, roads }: { map: Map; roads: RoadOverlay | null }) {
  const tiles = roads?.tiles
  const layers = useMemo(() => {
    if (!tiles || tiles.length === 0) return null
    const segments = roadSegments(tiles)
    const joints = tilesByGrade(tiles)
    return ROAD_GRADES.map(grade => ({ grade, segments: segments.filter(segment => segment.grade === grade), joints: joints[grade] }))
  }, [tiles])
  if (layers === null) return null
  return <group name="road-network">{layers.map(layer => <group key={layer.grade}>
    <GradeSegments map={map} grade={layer.grade} segments={layer.segments} />
    <GradeJoints map={map} grade={layer.grade} tiles={layer.joints} />
  </group>)}</group>
}
