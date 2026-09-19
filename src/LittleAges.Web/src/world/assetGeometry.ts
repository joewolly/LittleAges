import * as THREE from 'three'

/** Meshopt GLBs may contain normalized integers. Transform only float copies:
 * applying world transforms directly to normalized integer attributes clips
 * positions outside [-1,1], which can bury tree trunks and distort foliage. */
export function worldGeometry(source: THREE.Mesh): THREE.BufferGeometry {
  const geometry = source.geometry.clone()
  for (const name of ['position', 'normal']) {
    const attribute = geometry.getAttribute(name)
    if (!attribute) continue
    const data = new Float32Array(attribute.count * 3)
    for (let i = 0; i < attribute.count; i++) {
      data[i * 3] = attribute.getX(i)
      data[i * 3 + 1] = attribute.getY(i)
      data[i * 3 + 2] = attribute.getZ(i)
    }
    geometry.setAttribute(name, new THREE.BufferAttribute(data, 3))
  }
  return geometry.applyMatrix4(source.matrixWorld)
}
