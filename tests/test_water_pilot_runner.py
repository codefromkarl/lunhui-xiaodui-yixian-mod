import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
import run_water_pilot
class WaterPilotRunnerTests(unittest.TestCase):
 def test_failed_plain_probe_restores_original_product_configuration(self):
  with tempfile.TemporaryDirectory() as d:
   game=Path(d);config=game/'Mods/yixian.sect/content/five-water-pilot.json';config.parent.mkdir(parents=True)
   original=b'{"waterMasteryMultiplier":2,"userSetting":"keep"}\n';config.write_bytes(original)
   def launch(*args,**kwargs):
    self.assertEqual(json.loads(config.read_text())['waterMasteryMultiplier'],1)
    raise RuntimeError('launch failed')
   with patch.object(run_water_pilot,'game_running',return_value=False),patch.object(run_water_pilot.subprocess,'run',side_effect=launch):
    with self.assertRaises(RuntimeError):run_water_pilot.run_arm('plain',False,game,600)
   self.assertEqual(config.read_bytes(),original)
