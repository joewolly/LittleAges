import { useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import * as THREE from 'three'
import type { Map, Settlement, Structure } from '../api'
import { worldToScene } from './visuals'

/** One inexpensive batch keeps props grounded when dynamic shadows decline. */
export function GroundContacts({ map, structures, settlement }: { map: Map; structures: Structure[]; settlement: Settlement | null }) {
  const mesh = useRef<THREE.InstancedMesh>(null)
  const texture = useMemo(() => {
    const pixels = new Uint8Array(32 * 32 * 4)
    for (let y = 0; y < 32; y++) for (let x = 0; x < 32; x++) {
      const radius = Math.hypot((x - 15.5) / 15.5, (y - 15.5) / 15.5)
      const offset = (y * 32 + x) * 4
      pixels[offset] = 25; pixels[offset + 1] = 40; pixels[offset + 2] = 12
      pixels[offset + 3] = Math.round(Math.pow(Math.max(0, 1 - radius), 1.5) * 120)
    }
    const result = new THREE.DataTexture(pixels, 32, 32)
    result.magFilter = THREE.LinearFilter
    result.needsUpdate = true
    return result
  }, [])
  useEffect(() => () => texture.dispose(), [texture])
  const patches = useMemo(() => {
    const quantities = new globalThis.Map(settlement?.resources.map(resource => [resource.resourceNodeId, resource.currentQuantity]))
    return [
      ...map.resources.filter(resource => (quantities.get(resource.resourceNodeId) ?? resource.maximumQuantity) > 0).map(resource => ({ ...resource.location, radius: resource.resourceType === 'Wood' ? 1.05 : 0.52 })),
      ...structures.map(structure => ({ ...structure.location, radius: 1.5 })),
    ]
  }, [map.resources, settlement, structures])
  useLayoutEffect(() => {
    if (!mesh.current) return
    const dummy = new THREE.Object3D()
    dummy.rotation.x = -Math.PI / 2
    patches.forEach((patch, i) => {
      const point = worldToScene(map, patch.x, patch.y, 0.028)
      dummy.position.set(point.x, point.y, point.z)
      dummy.scale.set(patch.radius * 2, patch.radius * 2, 1)
      dummy.updateMatrix()
      mesh.current!.setMatrixAt(i, dummy.matrix)
    })
    mesh.current.instanceMatrix.needsUpdate = true
    mesh.current.computeBoundingSphere()
  }, [map, patches])
  return <instancedMesh ref={mesh} args={[undefined, undefined, patches.length]} renderOrder={1}>
    <planeGeometry /><meshBasicMaterial map={texture} transparent depthWrite={false} polygonOffset polygonOffsetFactor={-1} />
  </instancedMesh>
}
