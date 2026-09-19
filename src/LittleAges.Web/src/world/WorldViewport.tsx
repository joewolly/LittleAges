import { Component, Suspense, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { AdaptiveDpr, Clone, MapControls, OrthographicCamera, PerformanceMonitor, useAnimations, useGLTF } from '@react-three/drei'
import { Canvas, useFrame, useThree } from '@react-three/fiber'
import type { MapControls as MapControlsImpl } from 'three-stdlib'
import * as THREE from 'three'
import { clone as cloneSkeleton } from 'three/addons/utils/SkeletonUtils.js'
import type { Citizen, Map, Settlement, Structure } from '../api'
import { LegacyMap } from './LegacyMap'
import { GroundContacts } from './GroundContacts'
import { citizenAnimation, setAnimationPlayback } from './artMotion'
import { worldGeometry } from './assetGeometry'
import { createResourceLod } from './resourceLod'
import { createGroundTexture, upgradeGroundTexture } from './terrainArt'
import { PresentationClock, doorway, doorwayPlan, restingHome } from './presentation'
import artManifest from './art-manifest.json'
import { citizenPaletteIndex, detailVariant, scenePointAlongMovementPlan, stableVisualHash, worldToScene } from './visuals'

type DetailTier = 'full' | 'reduced'
const DIORAMA_ASSET_BYTES = artManifest.bytes
type CameraNudge = { x: number; z: number; zoom: number; sequence: number }
type PerfStats = { fps: number; calls: number; triangles: number; p95: number; readyMs: number }
const diagnosticsEnabled = import.meta.env.DEV || new URLSearchParams(window.location.search).has('diagnostics')

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
  const texture = useMemo(() => createGroundTexture(map, worldSeed), [map, worldSeed])
  const invalidate = useThree(state => state.invalidate)
  useEffect(() => {
    if (typeof Worker === 'undefined') return () => texture.dispose()
    const worker = new Worker(new URL('./terrain.worker.ts', import.meta.url), { type: 'module' })
    worker.onmessage = (event: MessageEvent<Uint8Array<ArrayBuffer>>) => {
      upgradeGroundTexture(texture, map, event.data)
      invalidate()
      worker.terminate()
    }
    worker.onerror = () => worker.terminate() // Keep the quick texture if worker loading fails.
    worker.postMessage([{ width: map.width, height: map.height, terrain: map.terrain }, worldSeed])
    return () => { worker.terminate(); texture.dispose() }
  }, [map, worldSeed, texture, invalidate])
  const geometry = useMemo(() => {
    const positions = new Float32Array((map.width + 1) * (map.height + 1) * 3)
    const uvs = new Float32Array((map.width + 1) * (map.height + 1) * 2)
    const indices = new Uint32Array(map.width * map.height * 6)
    let vertex = 0
    for (let y = 0; y <= map.height; y += 1) for (let x = 0; x <= map.width; x += 1) {
      const sampleX = Math.max(0, Math.min(map.width - 1, x === map.width ? x - 1 : x))
      const sampleY = Math.max(0, Math.min(map.height - 1, y === map.height ? y - 1 : y))
      const point = worldToScene(map, sampleX, sampleY)
      positions[vertex * 3] = x - map.width / 2
      positions[vertex * 3 + 1] = point.y
      positions[vertex * 3 + 2] = y - map.height / 2
      uvs[vertex * 2] = x / map.width
      uvs[vertex * 2 + 1] = y / map.height
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
    result.setAttribute('uv', new THREE.BufferAttribute(uvs, 2))
    result.setIndex(new THREE.BufferAttribute(indices, 1))
    result.computeVertexNormals()
    return result
  }, [map])
  useEffect(() => () => geometry.dispose(), [geometry])
  return <mesh geometry={geometry} receiveShadow><meshStandardMaterial map={texture} roughness={0.96} metalness={0} /></mesh>
}

function GroundDetails({ map, worldSeed, detailTier }: { map: Map; worldSeed: string | null; detailTier: DetailTier }) {
  const details = useMemo(() => {
    const values: Array<{ x: number; y: number; rotation: number; scale: number }> = []
    for (let y = 1; y < map.height - 1; y += 1) for (let x = 1; x < map.width - 1; x += 1) {
      const terrain = map.terrain[y * map.width + x]
      if (terrain !== 3 && terrain !== 5) continue
      const hash = stableVisualHash(worldSeed ?? 'world', 'ground-detail', x, y)
      if (hash % (detailTier === 'full' ? 9 : 29) !== 0) continue
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
    <meshStandardMaterial color="#70a83d" roughness={1} />
  </instancedMesh>
}

function Water({ map }: { map: Map }) {
  const geometry = useMemo(() => {
    const positions: number[] = [], shore: number[] = []
    const isWater = (x: number, y: number) => x >= 0 && y >= 0 && x < map.width && y < map.height && map.terrain[y * map.width + x] === 1
    const corner = (x: number, y: number) => {
      // Match the terrain vertices exactly; independent flat tile heights made
      // the old overlay intersect the ground and expose a diamond grid.
      const point = worldToScene(map, Math.min(x, map.width - 1), Math.min(y, map.height - 1), 0.025)
      positions.push(x - map.width / 2, point.y, y - map.height / 2)
      shore.push([isWater(x, y), isWater(x - 1, y), isWater(x, y - 1), isWater(x - 1, y - 1)].filter(Boolean).length < 4 ? 1 : 0)
    }
    for (let y = 0; y < map.height; y++) for (let x = 0; x < map.width; x++) {
      if (!isWater(x, y)) continue
      corner(x, y); corner(x, y + 1); corner(x + 1, y)
      corner(x + 1, y); corner(x, y + 1); corner(x + 1, y + 1)
    }
    const result = new THREE.BufferGeometry()
    result.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3))
    result.setAttribute('shore', new THREE.Float32BufferAttribute(shore, 1))
    return result
  }, [map])
  useEffect(() => () => geometry.dispose(), [geometry])
  return <mesh geometry={geometry}><shaderMaterial transparent depthWrite={false} vertexShader={`
    attribute float shore;
    varying vec2 waterPoint;
    varying float bank;
    void main() { waterPoint = position.xz; bank = shore; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }
  `} fragmentShader={`
    varying vec2 waterPoint;
    varying float bank;
    void main() {
      float streak = smoothstep(0.985, 1.0, sin(waterPoint.y * 12.0 + sin(waterPoint.x * 1.8))) * smoothstep(0.2, 0.8, sin(waterPoint.x * 3.1));
      vec3 deep = vec3(0.035, 0.39, 0.46);
      vec3 shallow = vec3(0.24, 0.65, 0.61);
      vec3 color = mix(deep, shallow, bank * 0.7) + streak * 0.1;
      gl_FragColor = vec4(color, 0.88);
      #include <tonemapping_fragment>
      #include <colorspace_fragment>
    }
  `} /></mesh>
}

function ResourceInstances({ map, settlement, worldSeed, detailTier }: { map: Map; settlement: Settlement | null; worldSeed: string | null; detailTier: DetailTier }) {
  const resourceKey = JSON.stringify((settlement?.resources ?? []).map(resource => [resource.resourceNodeId, resource.currentQuantity]))
  const quantities = useMemo(() => new globalThis.Map<string, number>(JSON.parse(resourceKey)), [resourceKey])
  const chunks = useMemo(() => {
    const groups = new globalThis.Map<string, Map['resources']>()
    for (const resource of map.resources) {
      if ((quantities.get(resource.resourceNodeId) ?? resource.maximumQuantity) <= 0) continue
      const key = `${resource.resourceType}-${Math.floor(resource.location.x / 12)}-${Math.floor(resource.location.y / 12)}`
      const group = groups.get(key) ?? []
      group.push(resource)
      groups.set(key, group)
    }
    return [...groups.entries()]
  }, [map.resources, quantities])
  return <>{chunks.map(([key, resources]) => <ResourceTypeInstances key={key} type={resources[0].resourceType} map={map} resources={resources} quantities={quantities} worldSeed={worldSeed} detailTier={detailTier} />)}</>
}

function ResourceTypeInstances({ type, map, resources, quantities, worldSeed, detailTier }: { detailTier: DetailTier; type: 'Food' | 'Wood' | 'Stone'; map: Map; resources: Map['resources']; quantities: globalThis.Map<string, number>; worldSeed: string | null }) {
  const ref = useRef<THREE.InstancedMesh>(null)
  const distant = useRef<THREE.InstancedMesh>(null)
  const viewDirection = useRef(new THREE.Vector3())
  const viewCenter = useRef(new THREE.Vector3())
  const center = useMemo(() => { const r = resources[0]; return new THREE.Vector3(r.location.x - (map.width - 1) / 2, 0, r.location.y - (map.height - 1) / 2) }, [map.width, map.height, resources])
  const asset = useGLTF(`/assets/diorama/${type.toLowerCase()}.glb`)
  const prepared = useMemo(() => {
    asset.scene.updateMatrixWorld(true)
    let source: THREE.Mesh | undefined
    asset.scene.traverse(object => { if (object instanceof THREE.Mesh) source = object })
    if (!source) throw new Error(`Missing ${type} resource mesh`)
    return { geometry: worldGeometry(source), material: source.material }
  }, [asset.scene, type])
  useEffect(() => () => prepared.geometry.dispose(), [prepared])
  useLayoutEffect(() => {
    const mesh = ref.current
    if (!mesh) return
    const dummy = new THREE.Object3D()
    resources.forEach((resource, index) => {
      const point = worldToScene(map, resource.location.x, resource.location.y, 0.02)
      const variant = detailVariant(worldSeed, `resource-${type}`, resource.resourceNodeId)
      const remaining = Math.max(0.2, Math.min(1, (quantities.get(resource.resourceNodeId) ?? resource.maximumQuantity) / Math.max(1, resource.maximumQuantity)))
      dummy.position.set(point.x, point.y, point.z)
      dummy.rotation.y = (variant % 360) * Math.PI / 180
      dummy.scale.setScalar((0.72 + ((variant >>> 8) & 15) / 45) * Math.sqrt(remaining))
      dummy.updateMatrix()
      mesh.setMatrixAt(index, dummy.matrix)
      distant.current?.setMatrixAt(index, dummy.matrix)
    })
    mesh.instanceMatrix.needsUpdate = true
    mesh.computeBoundingSphere()
    if (distant.current) { distant.current.instanceMatrix.needsUpdate = true; distant.current.computeBoundingSphere() }
  }, [map, quantities, resources, type, worldSeed])
  useFrame(({ camera }) => {
    if (!ref.current || !distant.current) return
    camera.getWorldDirection(viewDirection.current)
    viewCenter.current.copy(camera.position).addScaledVector(viewDirection.current, -camera.position.y / Math.min(-0.001, viewDirection.current.y))
    const near = camera.zoom >= 30 && viewCenter.current.distanceTo(center) < (detailTier === 'full' ? 10 : 9)
    ref.current.visible = near
    distant.current.visible = !near
  })
  const coarse = useMemo(() => createResourceLod(type), [type])
  useEffect(() => () => coarse.dispose(), [coarse])
  return <><instancedMesh ref={ref} args={[prepared.geometry, prepared.material, resources.length]} castShadow receiveShadow />
    <instancedMesh ref={distant} args={[coarse, undefined, resources.length]}><meshStandardMaterial vertexColors roughness={1} /></instancedMesh></>
}

function StructureModel({ map, structure, detailTier }: { map: Map; structure: Structure; detailTier: DetailTier }) {
  const point = worldToScene(map, structure.location.x, structure.location.y, 0.05)
  const incomplete = structure.status === 'UnderConstruction'
  const asset = useGLTF(`/assets/diorama/${structure.type.toLowerCase()}.glb`)
  return <group position={[point.x, point.y, point.z]} scale={0.9}>
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.015, 0]} receiveShadow><circleGeometry args={[1.05, 18]} /><meshBasicMaterial color="#34251b" transparent opacity={detailTier === 'full' ? 0.2 : 0.12} depthWrite={false} /></mesh>
    <Clone object={asset.scene} deep="materialsOnly" castShadow receiveShadow />
    {incomplete && <group>
      {[-1.13, 1.13].flatMap(x => [-0.95, 0.95].map(z => <mesh key={`${x}:${z}`} position={[x, 0.9, z]} castShadow><boxGeometry args={[0.1, 1.8, 0.1]} /><meshStandardMaterial color="#ab773e" /></mesh>))}
      {[-0.95, 0.95].map(z => <mesh key={z} position={[0, 1.15, z]} castShadow><boxGeometry args={[2.4, 0.1, 0.35]} /><meshStandardMaterial color="#c39756" /></mesh>)}
    </group>}
  </group>
}

function CitizenModel({ citizen, map, structures, presentationClock, worldSeed, operationalSpeed, paused, reducedMotion, selected, onSelect }: { structures: Structure[]; presentationClock: PresentationClock; citizen: Citizen; map: Map; worldSeed: string | null; operationalSpeed: number | null; paused: boolean; reducedMotion: boolean; selected: boolean; onSelect: () => void }) {
  const group = useRef<THREE.Group>(null)
  const body = useRef<THREE.Group>(null)
  const target = useRef(new THREE.Vector3())
  const previousFrame = useRef(new THREE.Vector3())
  const initialized = useRef(false)
  const indoorHome = useRef<Structure | null>(null)
  const home = restingHome(citizen, structures)
  const displayPlan = useMemo(() => doorwayPlan(citizen, structures), [citizen, structures])
  const asset = useGLTF('/assets/diorama/villager.glb')
  const { actions, mixer } = useAnimations(asset.animations, group)
  const point = worldToScene(map, citizen.location.x, citizen.location.y, 0.05)
  const palette = villagerPalette[citizenPaletteIndex(worldSeed, citizen.citizenId)]
  const painted = useMemo(() => {
    const scene = cloneSkeleton(asset.scene)
    scene.traverse(object => {
      if (!(object instanceof THREE.Mesh)) return
      object.castShadow = true
      const material = (object.material as THREE.MeshStandardMaterial).clone()
      material.onBeforeCompile = shader => {
        shader.uniforms.clothingColor = { value: new THREE.Color(palette) }
        shader.fragmentShader = 'uniform vec3 clothingColor;\n' + shader.fragmentShader
        shader.fragmentShader = shader.fragmentShader.replace('#include <map_fragment>', `#include <map_fragment>
          float clothMask = step(diffuseColor.r * 1.8, diffuseColor.b) * step(diffuseColor.r * 1.7, diffuseColor.g);
          diffuseColor.rgb = mix(diffuseColor.rgb, clothingColor * (0.55 + diffuseColor.g), clothMask);`)
      }
      material.customProgramCacheKey = () => 'little-ages-clothing-v1'
      object.material = material
    })
    return scene
  }, [asset.scene, palette])
  useEffect(() => () => painted.traverse(object => {
    if (object instanceof THREE.Mesh) (object.material as THREE.Material).dispose()
    if (object instanceof THREE.SkinnedMesh) object.skeleton.dispose()
  }), [painted])
  const stageScale = citizen.lifeStage === 'YoungChild' || citizen.lifeStage === 'Young Child' ? 0.58 : citizen.lifeStage === 'Child' ? 0.7 : citizen.lifeStage === 'Adolescent' ? 0.86 : citizen.lifeStage === 'Elder' ? 0.94 : 1
  const animationClip = citizenAnimation(citizen)
  useLayoutEffect(() => {
    const current = group.current
    if (!current) return
    target.current.set(point.x, point.y, point.z)
    if (!initialized.current || reducedMotion) {
      const planned = displayPlan && !reducedMotion ? scenePointAlongMovementPlan(map, displayPlan, presentationClock.at(performance.now()), 0.05) : { x: point.x, y: point.y, z: point.z }
      current.position.set(planned.x, planned.y, planned.z)
      previousFrame.current.copy(current.position)
      current.visible = home === null
      indoorHome.current = home
      initialized.current = true
    }
    if (indoorHome.current && !home) {
      const exit = doorway(indoorHome.current)
      const exitPoint = worldToScene(map, exit.x, exit.y, 0.05)
      current.position.set(exitPoint.x, exitPoint.y, exitPoint.z)
      previousFrame.current.copy(current.position)
      current.visible = true
      indoorHome.current = null
    }
  }, [displayPlan, home, map, presentationClock, point.x, point.y, point.z, reducedMotion])
  useEffect(() => {
    for (const action of Object.values(actions)) action?.stop()
    if (reducedMotion || (operationalSpeed ?? 0) > 10) return
    const prefix = `${animationClip}_`
    const active = Object.entries(actions).filter(([name]) => name.startsWith(prefix)).map(([, action]) => action).filter(action => action !== null)
    for (const action of active) action?.reset().fadeIn(0.12).play()
    return () => { for (const action of active) action?.fadeOut(0.12) }
  }, [actions, animationClip, operationalSpeed, reducedMotion])
  useEffect(() => { for (const action of Object.values(actions)) if (action) action.paused = paused }, [actions, paused, animationClip, operationalSpeed])
  useEffect(() => { setAnimationPlayback(mixer, paused, reducedMotion, operationalSpeed) }, [mixer, paused, reducedMotion, operationalSpeed])
  useFrame(({ clock }, delta) => {
    const current = group.current
    if (!current) return
    if (paused || reducedMotion) { previousFrame.current.copy(current.position); return }
    const plan = displayPlan
    const visualMinute = presentationClock.at(performance.now())
    const routeMotion = plan !== null && operationalSpeed !== null && operationalSpeed > 0 && operationalSpeed <= 10
    if (routeMotion) {
      const planned = scenePointAlongMovementPlan(map, plan, visualMinute, 0.05)
      target.current.set(planned.x, planned.y, planned.z)
    }
    if (home) {
      const entrance = doorway(home)
      const entryPoint = worldToScene(map, entrance.x, entrance.y, 0.05)
      target.current.set(entryPoint.x, entryPoint.y, entryPoint.z)
      if (current.position.distanceToSquared(target.current) < 0.01) {
        current.visible = false
        indoorHome.current = home
      }
    }
    const step = Math.min(delta, 0.1)
    current.position.lerp(target.current, 1 - Math.exp(-step * (routeMotion ? 18 : 12)))
    const dx = current.position.x - previousFrame.current.x
    const dz = current.position.z - previousFrame.current.z
    if (dx * dx + dz * dz > 0.000001) current.rotation.y = Math.atan2(dx, dz)
    previousFrame.current.copy(current.position)
    const moving = routeMotion && visualMinute < (plan?.waypoints.at(-1)?.arriveMinute ?? 0)
    if (body.current) body.current.position.y = animationClip === 'Rest' ? -0.12 : moving ? Math.abs(Math.sin(clock.elapsedTime * 8 + Number(citizen.citizenId) % 7)) * 0.06 : Math.sin(clock.elapsedTime * 1.7 + Number(citizen.citizenId) % 11) * 0.015
  })
  return <group ref={group} scale={stageScale} onClick={event => { event.stopPropagation(); onSelect() }}>
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.015, 0]}><circleGeometry args={[0.3, 16]} /><meshBasicMaterial color="#2c2018" transparent opacity={0.2} depthWrite={false} /></mesh>
    {selected && <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.04, 0]}><ringGeometry args={[0.36, 0.48, 24]} /><meshBasicMaterial color="#f3d287" side={THREE.DoubleSide} /></mesh>}
    <group ref={body}>
      <primitive object={painted} />
      {citizen.carriedResource !== null && <mesh position={[0, 0.6, 0.24]}><boxGeometry args={[0.32, 0.24, 0.22]} /><meshStandardMaterial color={citizen.carriedResource === 'Food' ? '#a9554a' : citizen.carriedResource === 'Wood' ? '#765137' : '#817971'} /></mesh>}
      {(citizen.currentAction === 'Build' || citizen.currentAction === 'HaulConstruction') && <mesh position={[0.3, 0.58, 0]} rotation={[0, 0, -0.65]}><boxGeometry args={[0.38, 0.05, 0.06]} /><meshStandardMaterial color="#6e5038" /></mesh>}
    </group>
  </group>
}

function cameraFocus(map: Map, structures: Structure[]): THREE.Vector3 {
  // Gathering citizens can roam across the map. Keep the default composition
  // anchored on the settlement and let selection/follow mode chase individuals.
  const coordinates = structures.length > 0 ? structures.map(structure => structure.location) : [map.startingSite]
  const x = coordinates.reduce((total, coordinate) => total + coordinate.x, 0) / coordinates.length
  const y = coordinates.reduce((total, coordinate) => total + coordinate.y, 0) / coordinates.length
  const point = worldToScene(map, x, y)
  return new THREE.Vector3(point.x, point.y, point.z)
}

function CameraRig({ focus, rotation, resetToken, nudge, follow, controlsEnabled }: { focus: THREE.Vector3; rotation: number; resetToken: number; nudge: CameraNudge; follow: (() => { x: number; y: number; z: number }) | null; controlsEnabled: boolean }) {
  const camera = useRef<THREE.OrthographicCamera>(null)
  const controls = useRef<MapControlsImpl>(null)
  const appliedHome = useRef('')
  const followTarget = useRef(new THREE.Vector3())
  const followDelta = useRef(new THREE.Vector3())
  const { size } = useThree()
  const homeZoom = Math.max(30, Math.min(76, size.width / 18))
  const focusX = focus.x
  const focusY = focus.y
  const focusZ = focus.z
  const homeIdentity = `${focusX}:${focusY}:${focusZ}:${resetToken}:${rotation}:${homeZoom}`
  useFrame(() => {
    const activeCamera = camera.current
    const activeControls = controls.current
    // MapControls is recreated when makeDefault installs the orthographic camera.
    // Initialize only once it controls that camera, not the temporary Canvas camera.
    if (!activeCamera || !activeControls || activeControls.object !== activeCamera || appliedHome.current === homeIdentity) return
    const angle = rotation * Math.PI / 2 + Math.PI / 4
    activeCamera.position.set(focusX + Math.cos(angle) * 28, focusY + 26, focusZ + Math.sin(angle) * 28)
    activeCamera.zoom = homeZoom
    activeCamera.updateProjectionMatrix()
    activeControls.target.set(focusX, focusY, focusZ)
    activeControls.update()
    appliedHome.current = homeIdentity
  }, -1)
  useEffect(() => {
    const activeCamera = camera.current
    const activeControls = controls.current
    if (!activeCamera || !activeControls) return
    activeControls.target.x += nudge.x
    activeControls.target.z += nudge.z
    activeCamera.position.x += nudge.x
    activeCamera.position.z += nudge.z
    activeCamera.zoom = THREE.MathUtils.clamp(activeCamera.zoom + nudge.zoom, 8, 180)
    activeCamera.updateProjectionMatrix()
    activeControls.update()
  }, [nudge])
  useFrame((_state, frameDelta) => {
    if (!follow || !controls.current) return
    const point = follow()
    const target = followTarget.current.set(point.x, point.y, point.z)
    const blend = 1 - Math.exp(-Math.min(frameDelta, 0.1) * 5)
    const delta = followDelta.current.copy(target).sub(controls.current.target).multiplyScalar(blend)
    controls.current.object.position.add(delta)
    controls.current.target.lerp(target, blend)
    controls.current.update()
  })
  return <>
    <OrthographicCamera ref={camera} makeDefault near={0.1} far={400} position={[24, 26, 24]} zoom={40} />
    <MapControls ref={controls} enabled={controlsEnabled} enableRotate={false} screenSpacePanning minZoom={8} maxZoom={180} maxPolarAngle={Math.PI / 2.15} />
  </>
}

function SettlementSun({ enabled }: { enabled: boolean }) {
  const light = useRef<THREE.DirectionalLight>(null)
  const target = useMemo(() => new THREE.Object3D(), [])
  const ground = useMemo(() => new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), [])
  const ray = useMemo(() => new THREE.Raycaster(), [])
  const scratch = useRef(new THREE.Vector3())
  useFrame(({ camera }) => {
    if (!light.current) return
    const point = scratch.current
    ray.setFromCamera(new THREE.Vector2(0, 0), camera)
    if (!ray.ray.intersectPlane(ground, point)) return
    // Move shadow coverage with pan/follow; quantize to avoid subpixel shimmer.
    point.x = Math.round(point.x * 32) / 32
    point.z = Math.round(point.z * 32) / 32
    target.position.copy(point)
    light.current.position.copy(point).add(new THREE.Vector3(-12, 22, 8))
    target.updateMatrixWorld()
  })
  return <><primitive object={target} /><directionalLight ref={light} target={target} castShadow={enabled} intensity={2.3} color="#fff0d7" shadow-mapSize={[2048, 2048]} shadow-camera-left={-18} shadow-camera-right={18} shadow-camera-top={18} shadow-camera-bottom={-18} shadow-camera-near={1} shadow-camera-far={65} shadow-normalBias={0.035} shadow-bias={-0.00015} shadow-radius={3} /></>
}

function SceneStats({ onStats }: { onStats: (stats: PerfStats) => void }) {
  const { gl } = useThree()
  const sample = useRef({ frames: 0, elapsed: 0, durations: [] as number[], readyMs: 0 })
  useFrame((_state, delta) => {
    if (sample.current.readyMs === 0) sample.current.readyMs = performance.now()
    sample.current.durations.push(delta * 1000)
    sample.current.frames += 1
    sample.current.elapsed += delta
    if (sample.current.elapsed < 1) return
    onStats({ fps: Math.round(sample.current.frames / sample.current.elapsed), calls: gl.info.render.calls, triangles: gl.info.render.triangles, p95: sample.current.durations.sort((a, b) => a - b)[Math.floor(sample.current.durations.length * 0.95)] ?? 0, readyMs: sample.current.readyMs })
    sample.current = { frames: 0, elapsed: 0, durations: [], readyMs: sample.current.readyMs }
  })
  return null
}

function ContextLossGuard({ onLoss }: { onLoss: () => void }) {
  const { gl } = useThree()
  useEffect(() => {
    const lost = (event: Event) => { event.preventDefault(); onLoss() }
    gl.domElement.addEventListener('webglcontextlost', lost)
    // R3F deliberately loses its context when unmounting. That event must not
    // switch a newly mounted replacement canvas straight back to 2D.
    return () => gl.domElement.removeEventListener('webglcontextlost', lost)
  }, [gl, onLoss])
  return null
}

function DioramaScene({ map, citizens, structures, worldMinute, settlement, worldSeed, operationalSpeed, paused, reducedMotion, controlsEnabled, selectedCitizenId, onSelectCitizen, rotation, resetToken, nudge, followCitizenId, detailTier, onDetailTier, onStats }: { map: Map; citizens: Citizen[]; structures: Structure[]; settlement: Settlement | null; worldSeed: string | null; operationalSpeed: number | null; paused: boolean; reducedMotion: boolean; controlsEnabled: boolean; selectedCitizenId: string | null; onSelectCitizen: (id: string) => void; rotation: number; resetToken: number; nudge: CameraNudge; followCitizenId: string | null; detailTier: DetailTier; onDetailTier: (tier: DetailTier) => void; onStats: (stats: PerfStats) => void; worldMinute?: number }) {
  const [presentationClock] = useState(() => new PresentationClock())
  const observedMinute = worldMinute ?? Math.max(0, ...citizens.map(c => c.movementPlan?.observedMinute ?? 0))
  useLayoutEffect(() => { presentationClock.observe(observedMinute, operationalSpeed, paused, performance.now()) }, [presentationClock, observedMinute, operationalSpeed, paused])
  const focus = useMemo(() => cameraFocus(map, structures), [map, structures])
  const followed = citizens.find(citizen => citizen.citizenId === followCitizenId && citizen.isAlive)
  const followedPlan = followed ? doorwayPlan(followed, structures) : null
  const follow = followed ? () => followedPlan && !reducedMotion && (operationalSpeed ?? 0) <= 10
    ? scenePointAlongMovementPlan(map, followedPlan, presentationClock.at(performance.now()))
    : worldToScene(map, followed.location.x, followed.location.y) : null
  return <>
    <color attach="background" args={['#c9b792']} />
    <fog attach="fog" args={['#c9b792', 70, 210]} />
    <ambientLight intensity={0.5} color="#fff7e5" />
    <SettlementSun enabled={detailTier === 'full'} />
    <hemisphereLight args={['#bce1ff', '#809644', 1.1]} />
    <group>
      <mesh position={[0, -0.46, 0]} receiveShadow><boxGeometry args={[map.width + 2, 0.9, map.height + 2]} /><meshStandardMaterial color="#5b4634" roughness={1} /></mesh>
      <Terrain map={map} worldSeed={worldSeed} />
      <Water map={map} />
      <GroundContacts map={map} structures={structures} settlement={settlement} />
      <GroundDetails map={map} worldSeed={worldSeed} detailTier={detailTier} />
      <Suspense fallback={null}>
        <ResourceInstances map={map} settlement={settlement} worldSeed={worldSeed} detailTier={detailTier} />
        {structures.map(structure => <StructureModel key={structure.structureId} map={map} structure={structure} detailTier={detailTier} />)}
        {citizens.filter(citizen => citizen.isAlive).map(citizen => <CitizenModel key={citizen.citizenId} citizen={citizen} map={map} structures={structures} presentationClock={presentationClock} worldSeed={worldSeed} operationalSpeed={operationalSpeed} paused={paused} reducedMotion={reducedMotion} selected={citizen.citizenId === selectedCitizenId} onSelect={() => onSelectCitizen(citizen.citizenId)} />)}
        {diagnosticsEnabled && <SceneStats onStats={onStats} />}
      </Suspense>
    </group>
    <CameraRig focus={focus} rotation={rotation} resetToken={resetToken} nudge={nudge} follow={follow} controlsEnabled={controlsEnabled} />
    <AdaptiveDpr />
    <PerformanceMonitor flipflops={2} onDecline={() => onDetailTier('reduced')} onIncline={() => onDetailTier('full')} />
  </>
}

export type WorldViewportProps = {
  map: Map
  citizens: Citizen[]
  structures: Structure[]
  settlement: Settlement | null
  worldSeed: string | null
  operationalSpeed: number | null
  worldMinute?: number
  paused: boolean
  controlsEnabled?: boolean
  selectedCitizenId: string | null
  onSelectCitizen: (citizenId: string) => void
  onOpenSelected?: (citizenId: string) => void
  /** Development art review only; ignored by production builds. */
  previewDetailTier?: DetailTier
}

export function WorldViewport(props: WorldViewportProps) {
  const [fallback, setFallback] = useState(() => !supportsWebGL())
  const [rotation, setRotation] = useState(0)
  const [resetToken, setResetToken] = useState(0)
  const [followCitizenId, setFollowCitizenId] = useState<string | null>(null)
  const [detailTier, setDetailTier] = useState<DetailTier>('full')
  const effectiveDetailTier = import.meta.env.DEV ? props.previewDetailTier ?? detailTier : detailTier
  const [stats, setStats] = useState<PerfStats>({ fps: 0, calls: 0, triangles: 0, p95: 0, readyMs: 0 })
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
        <Canvas dpr={[1, 1.5]} shadows frameloop={reducedMotion ? 'demand' : 'always'} gl={{ antialias: true, powerPreference: 'high-performance' }} onCreated={({ gl }) => { gl.toneMapping = THREE.ACESFilmicToneMapping; gl.toneMappingExposure = 1.05 }}>
          <ContextLossGuard onLoss={() => setFallback(true)} />
          <DioramaScene {...props} controlsEnabled={props.controlsEnabled !== false} reducedMotion={reducedMotion} rotation={rotation} resetToken={resetToken} nudge={nudge} followCitizenId={effectiveFollowCitizenId} detailTier={effectiveDetailTier} onDetailTier={setDetailTier} onStats={setStats} />
        </Canvas>
      </SceneBoundary>}
    </div>
    {selected && <div className="world-selection" role="status"><strong>{selected.name}</strong><span>{selected.lifeStage} · {selected.occupation}</span><span>{restingHome(selected, props.structures) ? 'Resting indoors' : `${selected.currentAction} · ${selected.actionPhase}`}</span>{props.onOpenSelected && <button type="button" onClick={() => props.onOpenSelected?.(selected.citizenId)}>Open record</button>}</div>}
    {diagnosticsEnabled && !fallback && <output className="world-perf" aria-label="3D performance">{stats.fps} FPS · p95 {stats.p95.toFixed(1)} ms · ready {(stats.readyMs / 1000).toFixed(2)} s · {stats.calls} calls · {stats.triangles.toLocaleString()} tris · {props.citizens.filter(citizen => citizen.isAlive).length} villagers · {(DIORAMA_ASSET_BYTES / 1024).toFixed(1)} KB assets · {effectiveDetailTier}</output>}
    <span className="world-accessibility-note">Starting site</span><span className="world-accessibility-note">Keyboard: arrow keys pan, +/− zoom, R rotates. All citizen details remain available in Observer records.</span>
  </section>
}

// Start essential model downloads while the bootstrap API requests are still in flight.
for (const model of ['villager', 'shelter', 'stockpile', 'workshop', 'food', 'wood', 'stone']) useGLTF.preload(`/assets/diorama/${model}.glb`)
