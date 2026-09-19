"""Build Little Ages original painted cartoon GLBs with Blender 5.2+."""
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / 'scripts'))
from diorama_art import build
OUTPUT = ROOT / 'src/LittleAges.Web/public/assets/diorama'
SOURCE = ROOT / 'art/diorama/little-ages-diorama-kit.blend'
OUTPUT.mkdir(parents=True, exist_ok=True)
SOURCE.parent.mkdir(parents=True, exist_ok=True)
build(OUTPUT, SOURCE)
