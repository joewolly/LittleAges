import { spawnSync } from 'node:child_process'
import { readFileSync, statSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = fileURLToPath(new URL('../', import.meta.url))
const web = path.join(root, 'src/LittleAges.Web')
const cli = path.join(web, 'node_modules/@gltf-transform/cli/bin/cli.js')
const assets = path.join(web, 'public/assets/diorama')
const files = ['shelter', 'stockpile', 'workshop', 'food', 'wood', 'stone', 'villager']
let bytes = 0
for (const name of files) {
  const file = path.join(assets, `${name}.glb`)
  const result = spawnSync(process.execPath, [cli, 'optimize', file, file, '--compress', 'meshopt', '--texture-compress', 'webp', '--flatten', 'false', '--join', 'false', '--simplify', 'true', '--simplify-ratio', '0.5', '--simplify-error', '0.003', '--instance', 'false'], { stdio: 'inherit' })
  if (result.status !== 0) throw new Error(`Optimization failed: ${name}`)
  const validation = spawnSync(process.execPath, [cli, 'validate', file], { encoding: 'utf8' })
  process.stdout.write(validation.stdout)
  process.stderr.write(validation.stderr)
  if (validation.status !== 0 || !validation.stdout.includes('No errors found.') || !validation.stdout.includes('No warnings found.')) throw new Error(`Validation failed: ${name}`)
  bytes += statSync(file).size
}
if (bytes > 5 * 1024 * 1024) throw new Error(`Asset budget exceeded: ${bytes}`)
// Fail if optimization stripped the public animation domains.
const buffer = readFileSync(path.join(assets, 'villager.glb'))
const gltf = JSON.parse(buffer.subarray(20, 20 + buffer.readUInt32LE(12)).toString())
for (const domain of ['Idle', 'Walk', 'Carry', 'Gather', 'Build', 'Socialize', 'Rest']) {
  if (!gltf.animations?.some(animation => animation.name.startsWith(domain + '_'))) throw new Error(`Missing clip domain: ${domain}`)
}
writeFileSync(path.join(web, 'src/world/art-manifest.json'), JSON.stringify({ bytes, models: files.length }, null, 2) + '\n')
console.log(`Validated ${files.length} models, ${bytes} bytes`)
