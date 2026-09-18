# Little Ages diorama kit

`little-ages-diorama-kit.blend` and the runtime GLBs are generated from
[`scripts/build-diorama-assets.py`](../../scripts/build-diorama-assets.py).

Conventions:

- one Blender unit equals one world tile;
- model origins sit at ground center;
- runtime format is GLB/glTF 2.0;
- materials are shared, texture-free, rough, and storybook-stylized;
- stable root names are `Shelter`, `Stockpile`, `Workshop`, `FoodCluster`,
  `WoodCluster`, `StoneCluster`, and `Villager`;
- villager clip domains are Idle, Walk, Carry, Gather, Build, Socialize, and Rest.

Regenerate with Blender 5.2 or newer:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
  --background --factory-startup `
  --python .\scripts\build-diorama-assets.py
```

After regeneration, validate and optimize GLBs with glTF Transform before
shipping:

```powershell
Get-ChildItem .\src\LittleAges.Web\public\assets\diorama\*.glb | ForEach-Object {
  npx gltf-transform optimize $_.FullName $_.FullName --compress meshopt
  npx gltf-transform inspect $_.FullName
}
```

Do not hand-edit runtime GLBs.
