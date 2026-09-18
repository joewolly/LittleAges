import { Component, Suspense, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { AdaptiveDpr, Clone, MapControls, OrthographicCamera, PerformanceMonitor, useAnimations, useGLTF } from '@react-three/drei'
import { Canvas, useFrame, useThree } from '@react-three/fiber'
import type { MapControls as MapControlsImpl } from 'three-stdlib'
import * as THREE from 'three'
import type { Citizen, Map, Settlement, Structure } from '../api'
import { LegacyMap } from './LegacyMap'
import { citizenPaletteIndex, detailVariant, scenePointAlongMovementPlan, stableVisualHash, worldToScene } from './visuals'

type DetailTier = 'full' | 'reduced'
const DIORAMA_ASSET_BYTES = 65_152
type CameraNudge = { x: number; z: number; zoom: number; sequence: number }
type PerfStats = { fps: number; calls: number; triangles: number }

const terrainPalette = ['#78aebe', '#d8c991', '#7da06f', '#9d8d76', '#496f51']
const villagerPalette = ['#b95f4b', '#536f88', '#d09a48', '#6d8150', '#876390', '#3f7c78', '#9a704d', '#b77774']

function supportsWebGL(): boolean {
  if (typeof document === 'undefined') return false
  try {
    const canvas = document.createElement('canvas')
    return Boolean(canvas.getContext('webgl2') || canvas.getContext('webgl'))
  } catch {
    return false
  }
}

class SceneBoundary extends Component<{ children: ReactNode; onError: () => void }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  componentDidCatch() { this.props.onError() }
  render() { return this.state.failed ? null : this.props.children }
}

function Terrain({ map, worldSeed }: { map: Map; worldSeed: string | null }) {
  const geometry = useMemo(() => {
    const positions = new Float32Array((map.width + 1) * (map.height + 1) * 3)
    const colors = new Float32Array(positions.length)
    const indices = new Uint32Array(map.width * map.height * 6)
    const color = new THREE.Color()
    let vertex = 0
    for (let y = 0; y <= map.height; y += 1) for (let x = 0; x <= map.width; x += 1) {
      const sampleX = Math.max(0, Math.min(map.width - 1, x === map.width ? x - 1 : x))
      const sampleY = Math.max(0, Math.min(map.height - 1, y === map.height ? y - 1 : y))
      const point = worldToScene(map, sampleX, sampleY)
      positions[vertex * 3] = x - map.width / 2
      positions[vertex * 3 + 1] = point.y
      positions[vertex * 3 + 2] = y - map.height / 2
      color.set(terrainPalette[map.terrain[sampleY * map.width + sampleX] - 1] ?? '#7da06f')
      const tint = ((stableVisualHash(worldSeed ?? 'world', 'terrain-tint', sampleX, sampleY) & 255) / 255 - 0.5) * 0.08
      color.offsetHSL(0, tint * 0.35, tint)
      colors[vertex * 3] = color.r
      colors[vertex * 3 + 1] = color.g
      colors[vertex * 3 + 2] = color.b
      vertex += 1
    }
    let cursor = 0
    for (let y = 0; y < map.height; y += 1) for (let x = 0; x < map.width; x += 1) {
      const topLeft = y * (map.width + 1) + x
      const topRight = topLeft + 1
      const bottomLeft = topLeft + map.width + 1
      const bottomRight = bottomLeft + 1
      indices.set([topLeft, bottomLeft, topRight, topRight, bottomLeft, bottomRight], cursor)
      cursor += 6
    }
    const result = new THREE.BufferGeometry()
    result.setAttribute('position', new THREE.BufferAttribute(positions, 3))
    result.setAttribute('color', new THREE.BufferAttribute(colors, 3))
    result.setIndex(new THREE.BufferAttribute(indices, 1))
    result.computeVertexNormals()
    return result
  }, [map, worldSeed])
  useEffect(() => () => geometry.dispose(), [geometry])
  return <mesh geometry={geometry} receiveShadow><meshStandardMaterial vertexColors roughness={0.92} metalness={0} flatShading /></mesh>
}

function GroundDetails({ map, worldSeed, detailTier }: { map: Map; worldSeed: string | null; detailTier: DetailTier }) {
  const details = useMemo(() => {
    const values: Array<{ x: number; y: number; rotation: number; scale: number }> = []
    for (let y = 1; y < map.height - 1; y += 1) for (let x = 1; x < map.width - 1; x += 1) {
      const terrain = map.terrain[y * map.width + x]
      if (terrain === 1 || terrain === 4) continue
      const hash = stableVisualHash(worldSeed ?? 'world', 'ground-detail', x, y)
      if (hash % (detailTier === 'full' ? 43 : 89) !== 0) continue
      values.push({ x, y, rotation: (hash % 360) * Math.PI / 180, scale: 0.45 + ((hash >>> 8) & 15) / 35 })
    }
    return values
  }, [detailTier, map, worldSeed])
  const ref = useRef<THREE.InstancedMesh>(null)
  useLayoutEffect(() => {
    if (!ref.current) return
    const matrix = new THREE.Matrix4()
    const quaternion = new THREE.Quaternion()
    const position = new THREE.Vector3()
    const scale = new THREE.Vector3()
    details.forEach((detail, index) => {
      const point = worldToScene(map, detail.x, detail.y, 0.12)
      position.set(point.x, point.y, point.z)
      quaternion.setFromAxisAngle(new THREE.Vector3(0, 1, 0), detail.rotation)
      scale.set(detail.scale, detail.scale, detail.scale)
      matrix.compose(position, quaternion, scale)
      ref.current!.setMatrixAt(index, matrix)
    })
    ref.current.instanceMatrix.needsUpdate = true
  }, [details, map])
  return <instancedMesh ref={ref} args={[undefined, undefined, details.length]}>
    <coneGeometry args={[0.16, 0.34, 5]} />
    <meshStandardMaterial color="#d6bd68" roughness={1} flatShading />
  </instancedMesh>
}

function Water({ map }: { map: Map }) {
  const geometry = useMemo(() => {
    const positions: number[] = []
    for (let y = 0; y < map.height; y += 1) for (let x = 0; x < map.width; x += 1) {
      if (map.terrain[y * map.width + x] !== 1) continue
      const point = worldToScene(map, x, y, 0.035)
      positions.push(point.x - 0.5, point.y, point.z - 0.5, point.x - 0.5, point.y, point.z + 0.5, point.x + 0.5, point.y, point.z - 0.5)
      positions.push(point.x + 0.5, point.y, point.z - 0.5, point.x - 0.5, point.y, point.z + 0.5, point.x + 0.5, point.y, point.z + 0.5)
    }
    const result = new THREE.BufferGeometry()
    result.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3))
    result.computeVertexNormals()
    return result
  }, [map])
  useEffect(() => () => geometry.dispose(), [geometry])
  return <mesh geometry={geometry}><meshStandardMaterial color="#6da6b6" transparent opacity={0.78} roughness={0.3} /></mesh>
}

function ResourceInstances({ map, settlement, worldSeed, detailTier }: { map: Map; settlement: Settlement | null; worldSeed: string | null; detailTier: DetailTier }) {
  const quantities = useMemo(() => new globalThis.Map((settlement?.resources ?? []).map(resource => [resource.resourceNodeId, resource.currentQuantity])), [settlement])
  const resources = useMemo(() => map.resources.filter((_, index) => detailTier === 'full' || index % 2 === 0), [detailTier, map.resources])
  return <>{(['Food', 'Wood', 'Stone'] as const).map(type => <ResourceTypeInstances key={type} type={type} map={map} resources={resources.filter(resource => resource.resourceType === type && (quantities.get(resource.resourceNodeId) ?? resource.maximumQuantity) > 0)} quantities={quantities} worldSeed={worldSeed} />)}</>
}

function ResourceTypeInstances({ type, map, resources, quantities, worldSeed }: { type: 'Food' | 'Wood' | 'Stone'; map: Map; resources: Map['resources']; quantities: globalThis.Map<string, number>; worldSeed: string | null }) {
  const ref = useRef<THREE.InstancedMesh>(null)
  useLayoutEffect(() => {
    const mesh = ref.current
    if (!mesh) return
    const matrix = new THREE.Matrix4()
    const rotation = new THREE.Quaternion()
    const scale = new THREE.Vector3()
    const position = new THREE.Vector3()
    resources.forEach((resource, index) => {
      const point = worldToScene(map, resource.location.x, resource.location.y, type === 'Wood' ? 0.65 : 0.28)
      const variant = detailVariant(worldSeed, `resource-${type}`, resource.resourceNodeId)
      const remaining = Math.max(0.2, Math.min(1, (quantities.get(resource.resourceNodeId) ?? resource.maximumQuantity) / resource.maximumQuantity))
      position.set(point.x + ((variant & 15) / 15 - 0.5) * 0.28, point.y, point.z + (((variant >>> 4) & 15) / 15 - 0.5) * 0.28)
      rotation.setFromAxisAngle(new THREE.Vector3(0, 1, 0), (variant % 360) * Math.PI / 180)
      const size = (0.7 + ((variant >>> 8) & 15) / 50) * Math.sqrt(remaining)
      scale.set(size, size, size)
      matrix.compose(position, rotation, scale)
      mesh.setMatrixAt(index, matrix)
    })
    mesh.instanceMatrix.needsUpdate = true
  }, [map, quantities, resources, type, worldSeed])
  const color = type === 'Food' ? '#a9554a' : type === 'Wood' ? '#3f6846' : '#867b70'
  return <instancedMesh ref={ref} args={[undefined, undefined, resources.length]} castShadow={type === 'Wood'}>
    {type === 'Food' ? <icosahedronGeometry args={[0.34, 0]} /> : type === 'Wood' ? <coneGeometry args={[0.42, 1.3, 6]} /> : <dodecahedronGeometry args={[0.36, 0]} />}
    <meshStandardMaterial color={color} roughness={0.92} />
  </instancedMesh>
}

function StructureModel({ map, structure, detailTier }: { map: Map; structure: Structure; detailTier: DetailTier }) {
  const point = worldToScene(map, structure.location.x, structure.location.y, 0.05)
  const incomplete = structure.status === 'UnderConstruction'
  const asset = useGLTF(`/assets/diorama/${structure.type.toLowerCase()}.glb`)
  return <group position={[point.x, point.y, point.z]} rotation={[0, Math.PI / 4, 0]}>
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.015, 0]} receiveShadow><circleGeometry args={[1.05, 18]} /><meshBasicMaterial color="#34251b" transparent opacity={detailTier === 'full' ? 0.2 : 0.12} depthWrite={false} /></mesh>
    <Clone object={asset.scene} deep="materialsOnly" castShadow receiveShadow />
    {incomplete && <group>{[-0.62, 0.62].map(x => <mesh key={x} position={[x, 0.55, 0]}><boxGeometry args={[0.07, 1.1, 1.35]} /><meshStandardMaterial color="#d0a16b" /></mesh>)}</group>}
  </group>
}

function citizenAnimation(citizen: Citizen): string {
  if (citizen.currentAction === 'Rest') return 'Rest'
  if (citizen.currentAction === 'Socialize') return 'Socialize'
  if (citizen.currentAction === 'Build') return 'Build'
  if (citizen.carriedResource !== null || citizen.currentAction === 'HaulConstruction') return 'Carry'
  if (citizen.currentAction === 'GatherFood' || citizen.currentAction === 'GatherWood' || citizen.currentAction === 'GatherStone') return citizen.actionPhase === 'Perform' ? 'Gather' : 'Walk'
  return citizen.actionPhase !== 'Perform' && citizen.actionPhase !== 'None' ? 'Walk' : 'Idle'
}

function CitizenModel({ citizen, map, worldSeed, operationalSpeed, paused, reducedMotion, selected, onSelect }: { citizen: Citizen; map: Map; worldSeed: string | null; operationalSpeed: number | null; paused: boolean; reducedMotion: boolean; selected: boolean; onSelect: () => void }) {
  const group = useRef<THREE.Group>(null)
  const body = useRef<THREE.Group>(null)
  const target = useRef(new THREE.Vector3())
  const previousFrame = useRef(new THREE.Vector3())
  const planAnchor = useRef({ key: '', receivedAt: 0 })
  const asset = useGLTF('/assets/diorama/villager.glb')
  const { actions } = useAnimations(asset.animations, group)
  const point = worldToScene(map, citizen.location.x, citizen.location.y, 0.05)
  const palette = villagerPalette[citizenPaletteIndex(worldSeed, citizen.citizenId)]
  const stageScale = citizen.lifeStage === 'YoungChild' ? 0.58 : citizen.lifeStage === 'Child' ? 0.7 : citizen.lifeStage === 'Adolescent' ? 0.86 : citizen.lifeStage === 'Elder' ? 0.94 : 1
  const planKey = citizen.movementPlan === null ? 'none' : `${citizen.movementPlan.actionSequence}:${citizen.movementPlan.observedMinute}:${citizen.movementPlan.waypoints.length}`
  useEffect(() => {
    const next = new THREE.Vector3(point.x, point.y, point.z)
    if (planAnchor.current.key !== planKey) planAnchor.current = { key: planKey, receivedAt: performance.now() }
    const current = group.current
    if (current && (reducedMotion || operationalSpeed === null || operationalSpeed > 10 || citizen.movementPlan === null)) current.position.copy(next)
    target.current.copy(next)
  }, [citizen.movementPlan, operationalSpeed, planKey, point.x, point.y, point.z, reducedMotion])
  useEffect(() => {
    for (const action of Object.values(actions)) action?.stop()
    if (reducedMotion) return
    const prefix = `${citizenAnimation(citizen)}_`
    const active = Object.entries(actions).filter(([name]) => name.startsWith(prefix)).map(([, action]) => action).filter(action => action !== null)
    for (const action of active) action?.reset().fadeIn(0.12).play()
    return () => { for (const action of active) action?.fadeOut(0.12) }
  }, [actions, citizen, reducedMotion])
  useFrame(({ clock }, delta) => {
    const current = group.current
    if (!current) return
    const plan = citizen.movementPlan
    const routeMotion = !paused && !reducedMotion && plan !== null && operationalSpeed !== null && operationalSpeed > 0 && operationalSpeed <= 10
    if (routeMotion) {
      const visualMinute = plan.observedMinute + Math.max(0, performance.now() - planAnchor.current.receivedAt) / 1000 * operationalSpeed
      const planned = scenePointAlongMovementPlan(map, plan, visualMinute, 0.05)
      target.current.set(planned.x, planned.y, planned.z)
      current.position.lerp(target.current, 1 - Math.exp(-delta * 14))
      const dx = current.position.x - previousFrame.current.x
      const dz = current.position.z - previousFrame.current.z
      if (dx * dx + dz * dz > 0.00001) current.rotation.y = Math.atan2(dx, dz)
    } else current.position.lerp(target.current, 1 - Math.exp(-delta * (operationalSpeed !== null && operationalSpeed > 10 ? 18 : 9)))
    previousFrame.current.copy(current.position)
    const moving = routeMotion
    if (body.current) body.current.position.y = moving ? Math.abs(Math.sin(clock.elapsedTime * 8 + Number(citizen.citizenId) % 7)) * 0.06 : Math.sin(clock.elapsedTime * 1.7 + Number(citizen.citizenId) % 11) * 0.015
  })
  return <group ref={group} position={[point.x, point.y, point.z]} scale={stageScale} onClick={event => { event.stopPropagation(); onSelect() }}>
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.015, 0]}><circleGeometry args={[0.3, 16]} /><meshBasicMaterial color="#2c2018" transparent opacity={0.2} depthWrite={false} /></mesh>
    {selected && <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.04, 0]}><ringGeometry args={[0.36, 0.48, 24]} /><meshBasicMaterial color="#f3d287" side={THREE.DoubleSide} /></mesh>}
    <group ref={body}>
      <Clone object={asset.scene} deep="materialsOnly" castShadow inject={(object) => object.name === 'VillagerTunic' ? <meshStandardMaterial color={palette} roughness={0.9} /> : null} />
      {citizen.carriedResource !== null && <mesh position={[0, 0.6, 0.24]}><boxGeometry args={[0.32, 0.24, 0.22]} /><meshStandardMaterial color={citizen.carriedResource === 'Food' ? '#a9554a' : citizen.carriedResource === 'Wood' ? '#765137' : '#817971'} /></mesh>}
      {(citizen.currentAction === 'Build' || citizen.currentAction === 'HaulConstruction') && <mesh position={[0.3, 0.58, 0]} rotation={[0, 0, -0.65]}><boxGeometry args={[0.38, 0.05, 0.06]} /><meshStandardMaterial color="#6e5038" /></mesh>}
    </group>
  </group>
}

function cameraFocus(map: Map, citizens: Citizen[], structures: Structure[]): THREE.Vector3 {
  const coordinates = [...citizens.filter(citizen => citizen.isAlive).map(citizen => citizen.location), ...structures.map(structure => structure.location)]
  if (coordinates.length === 0) coordinates.push(map.startingSite)
  const x = coordinates.reduce((total, coordinate) => total + coordinate.x, 0) / coordinates.length
  const y = coordinates.reduce((total, coordinate) => total + coordinate.y, 0) / coordinates.length
  const point = worldToScene(map, x, y)
  return new THREE.Vector3(point.x, point.y, point.z)
}

function CameraRig({ focus, rotation, resetToken, nudge, follow, controlsEnabled }: { focus: THREE.Vector3; rotation: number; resetToken: number; nudge: CameraNudge; follow: THREE.Vector3 | null; controlsEnabled: boolean }) {
  const camera = useRef<THREE.OrthographicCamera>(null)
  const controls = useRef<MapControlsImpl>(null)
  const applyHome = () => {
    const activeCamera = camera.current
    const activeControls = controls.current
    if (!activeCamera || !activeControls) return
    const angle = rotation * Math.PI / 2 + Math.PI / 4
    activeCamera.position.set(focus.x + Math.cos(angle) * 28, focus.y + 26, focus.z + Math.sin(angle) * 28)
    activeCamera.zoom = 32
    activeCamera.updateProjectionMatrix()
    activeControls.target.copy(focus)
    activeControls.update()
  }
  useEffect(applyHome, [focus, resetToken, rotation])
  useEffect(() => {
    const activeCamera = camera.current
    const activeControls = controls.current
    if (!activeCamera || !activeControls) return
    activeControls.target.x += nudge.x
    activeControls.target.z += nudge.z
    activeCamera.zoom = THREE.MathUtils.clamp(activeCamera.zoom + nudge.zoom, 3, 42)
    activeCamera.updateProjectionMatrix()
    activeControls.update()
  }, [nudge])
  useFrame(() => {
    if (!follow || !controls.current) return
    controls.current.target.lerp(follow, 0.08)
    controls.current.update()
  })
  return <>
    <OrthographicCamera ref={camera} makeDefault near={0.1} far={400} position={[24, 26, 24]} zoom={32} />
    <MapControls ref={controls} enabled={controlsEnabled} enableRotate={false} screenSpacePanning minZoom={8} maxZoom={64} maxPolarAngle={Math.PI / 2.15} />
  </>
}

function SceneStats({ onStats }: { onStats: (stats: PerfStats) => void }) {
  const { gl } = useThree()
  const sample = useRef({ frames: 0, elapsed: 0 })
  useFrame((_state, delta) => {
    sample.current.frames += 1
    sample.current.elapsed += delta
    if (sample.current.elapsed < 1) return
    onStats({ fps: Math.round(sample.current.frames / sample.current.elapsed), calls: gl.info.render.calls, triangles: gl.info.render.triangles })
    sample.current = { frames: 0, elapsed: 0 }
  })
  return null
}

function DioramaScene({ map, citizens, structures, settlement, worldSeed, operationalSpeed, paused, reducedMotion, controlsEnabled, selectedCitizenId, onSelectCitizen, rotation, resetToken, nudge, followCitizenId, detailTier, onDetailTier, onStats }: { map: Map; citizens: Citizen[]; structures: Structure[]; settlement: Settlement | null; worldSeed: string | null; operationalSpeed: number | null; paused: boolean; reducedMotion: boolean; controlsEnabled: boolean; selectedCitizenId: string | null; onSelectCitizen: (id: string) => void; rotation: number; resetToken: number; nudge: CameraNudge; followCitizenId: string | null; detailTier: DetailTier; onDetailTier: (tier: DetailTier) => void; onStats: (stats: PerfStats) => void }) {
  const focus = useMemo(() => cameraFocus(map, citizens, structures), [citizens, map, structures])
  const followed = citizens.find(citizen => citizen.citizenId === followCitizenId && citizen.isAlive)
  const follow = followed ? (() => { const point = worldToScene(map, followed.location.x, followed.location.y); return new THREE.Vector3(point.x, point.y, point.z) })() : null
  return <>
    <color attach="background" args={['#c9b792']} />
    <fog attach="fog" args={['#c9b792', 70, 210]} />
    <ambientLight intensity={0.78} color="#fff0d2" />
    <directionalLight castShadow={detailTier === 'full'} position={[35, 58, 22]} intensity={2.65} color="#ffddb0" shadow-mapSize-width={1024} shadow-mapSize-height={1024} shadow-bias={-0.0004} />
    <hemisphereLight args={['#cde7e2', '#76503a', 1.25]} />
    <group>
      <mesh position={[0, -0.46, 0]} receiveShadow><boxGeometry args={[map.width + 2, 0.9, map.height + 2]} /><meshStandardMaterial color="#5b4634" roughness={1} /></mesh>
      <Terrain map={map} worldSeed={worldSeed} />
      <Water map={map} />
      <GroundDetails map={map} worldSeed={worldSeed} detailTier={detailTier} />
      <ResourceInstances map={map} settlement={settlement} worldSeed={worldSeed} detailTier={detailTier} />
      <Suspense fallback={null}>
        {structures.map(structure => <StructureModel key={structure.structureId} map={map} structure={structure} detailTier={detailTier} />)}
        {citizens.filter(citizen => citizen.isAlive).map(citizen => <CitizenModel key={citizen.citizenId} citizen={citizen} map={map} worldSeed={worldSeed} operationalSpeed={operationalSpeed} paused={paused} reducedMotion={reducedMotion} selected={citizen.citizenId === selectedCitizenId} onSelect={() => onSelectCitizen(citizen.citizenId)} />)}
      </Suspense>
    </group>
    <CameraRig focus={focus} rotation={rotation} resetToken={resetToken} nudge={nudge} follow={follow} controlsEnabled={controlsEnabled} />
    <AdaptiveDpr pixelated />
    <PerformanceMonitor flipflops={2} onDecline={() => onDetailTier('reduced')} onIncline={() => onDetailTier('full')} />
    {import.meta.env.DEV && <SceneStats onStats={onStats} />}
  </>
}

export type WorldViewportProps = {
  map: Map
  citizens: Citizen[]
  structures: Structure[]
  settlement: Settlement | null
  worldSeed: string | null
  operationalSpeed: number | null
  paused: boolean
  controlsEnabled?: boolean
  selectedCitizenId: string | null
  onSelectCitizen: (citizenId: string) => void
  onOpenSelected?: (citizenId: string) => void
}

export function WorldViewport(props: WorldViewportProps) {
  const [fallback, setFallback] = useState(() => !supportsWebGL())
  const [rotation, setRotation] = useState(0)
  const [resetToken, setResetToken] = useState(0)
  const [followCitizenId, setFollowCitizenId] = useState<string | null>(null)
  const [detailTier, setDetailTier] = useState<DetailTier>('full')
  const [stats, setStats] = useState<PerfStats>({ fps: 0, calls: 0, triangles: 0 })
  const [nudge, setNudge] = useState<CameraNudge>({ x: 0, z: 0, zoom: 0, sequence: 0 })
  const reducedMotion = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
  const selected = props.citizens.find(citizen => citizen.citizenId === props.selectedCitizenId) ?? null
  const effectiveFollowCitizenId = followCitizenId !== null && props.citizens.some(citizen => citizen.citizenId === followCitizenId && citizen.isAlive) ? followCitizenId : null
  const issueNudge = (x: number, z: number, zoom = 0) => setNudge(previous => ({ x, z, zoom, sequence: previous.sequence + 1 }))
  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'ArrowLeft') issueNudge(-2, 0)
    else if (event.key === 'ArrowRight') issueNudge(2, 0)
    else if (event.key === 'ArrowUp') issueNudge(0, -2)
    else if (event.key === 'ArrowDown') issueNudge(0, 2)
    else if (event.key === '+' || event.key === '=') issueNudge(0, 0, 2)
    else if (event.key === '-') issueNudge(0, 0, -2)
    else if (event.key.toLowerCase() === 'r') setRotation(value => (value + 1) % 4)
    else return
    event.preventDefault()
  }

  return <section className="world-viewport" aria-labelledby="world-heading" tabIndex={0} onKeyDown={onKeyDown}>
    <div className="world-title"><span>Living diorama</span><h2 id="world-heading">The settlement grounds</h2><p>Drag to pan · scroll to zoom · select a villager to follow their day</p></div>
    <div className="world-toolbar" aria-label="World camera controls">
      <button type="button" onClick={() => setRotation(value => (value + 3) % 4)} aria-label="Rotate world left">↶</button>
      <button type="button" onClick={() => setRotation(value => (value + 1) % 4)} aria-label="Rotate world right">↷</button>
      <button type="button" onClick={() => { setResetToken(value => value + 1); setFollowCitizenId(null) }}>Reset view</button>
      <button type="button" disabled={selected === null || !selected.isAlive} onClick={() => setFollowCitizenId(current => current === selected?.citizenId ? null : selected?.citizenId ?? null)}>{effectiveFollowCitizenId === selected?.citizenId ? 'Unfollow' : 'Follow selected'}</button>
      <button type="button" onClick={() => setFallback(value => !value)}>{fallback ? 'Try 3D' : '2D fallback'}</button>
    </div>
    <div className="world-stage">
      {fallback ? <LegacyMap map={props.map} citizens={props.citizens} structures={props.structures} /> : <SceneBoundary onError={() => setFallback(true)}>
        <Canvas dpr={[1, 1.5]} shadows frameloop={reducedMotion ? 'demand' : 'always'} gl={{ antialias: detailTier === 'full', powerPreference: 'high-performance' }} onCreated={({ gl }) => gl.domElement.addEventListener('webglcontextlost', event => { event.preventDefault(); setFallback(true) }, { once: true })}>
          <DioramaScene {...props} controlsEnabled={props.controlsEnabled !== false} reducedMotion={reducedMotion} rotation={rotation} resetToken={resetToken} nudge={nudge} followCitizenId={effectiveFollowCitizenId} detailTier={detailTier} onDetailTier={setDetailTier} onStats={setStats} />
        </Canvas>
      </SceneBoundary>}
    </div>
    {selected && <div className="world-selection" role="status"><strong>{selected.name}</strong><span>{selected.lifeStage} · {selected.occupation}</span><span>{selected.currentAction} · {selected.actionPhase}</span>{props.onOpenSelected && <button type="button" onClick={() => props.onOpenSelected?.(selected.citizenId)}>Open record</button>}</div>}
    {import.meta.env.DEV && !fallback && <output className="world-perf" aria-label="3D performance">{stats.fps} FPS · {stats.calls} calls · {stats.triangles.toLocaleString()} tris · {props.citizens.filter(citizen => citizen.isAlive).length} villagers · {(DIORAMA_ASSET_BYTES / 1024).toFixed(1)} KB assets · {detailTier}</output>}
    <span className="world-accessibility-note">Starting site</span><span className="world-accessibility-note">Keyboard: arrow keys pan, +/− zoom, R rotates. All citizen details remain available in Observer records.</span>
  </section>
}
