"""Regression tests for conversion and Unity runtime deployment."""

from __future__ import annotations

import contextlib
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import _importall
import mugen_to_flare
from mugen_deploy import deploy_character


class MugenDeployTests(unittest.TestCase):
    """Verify output selection, conversion compatibility and explicit failures."""

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "UnityProject"
        (self.project / "Assets").mkdir(parents=True)
        self.source = self.root / "out" / "TestCharacter"
        self.source.mkdir(parents=True)
        self.stream = self.project / "Assets" / "StreamingAssets" / "Custom_Characters" / self.source.name
        self.art = self.project / "Assets" / "Art" / "Custom_Characters" / self.source.name
        self.manifest = self.source / "TestCharacter.json"
        self.write_json(self.manifest, {"image": "atlas.png", "images": ["atlas.png", "atlas_1.png"]})
        self.write_json(self.source / "TestCharacter.stats.json", {"maxHp": 100})
        (self.source / "atlas.png").write_bytes(b"first atlas")
        (self.source / "atlas_1.png").write_bytes(b"second atlas")
        (self.source / "sounds").mkdir()
        (self.source / "sounds" / "sample.WAV").write_bytes(b"sound")
        (self.source / "sounds" / "sounds.json").write_text("{}")
        self.write_json(self.source / "states.json", {"states": []})
        self.write_json(self.source / "constants.json", {})
        self.write_json(self.source / "animations.json", {
            "actions": [{
                "number": 0,
                "directions": {"E": {"frames": [{"sprite": "0_0", "duration": 0.1}]}},
            }],
        })
        self.write_json(self.source / "atlas.sprites.json", {
            "pages": [{"index": 0, "file": "atlas.png"}, {"index": 1, "file": "atlas_1.png"}],
            "sprites": [{"key": "0_0", "x": 0, "y": 0, "w": 16, "h": 16}],
        })

    @staticmethod
    def write_json(path: Path, document: object) -> None:
        """Write a compact test fixture."""
        path.write_text(json.dumps(document), encoding="utf-8")

    def run_cli(self, *arguments: str) -> tuple[int, str]:
        """Capture CLI output without touching the real Unity project."""
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            result = mugen_to_flare.main([*arguments, "--project-root", str(self.project)])
        return result, output.getvalue()

    def test_deploys_only_runtime_files_and_all_pages(self) -> None:
        self.assertEqual(deploy_character(self.manifest, self.project), (2, 3))
        self.assertEqual(
            sorted(path.name for path in self.stream.iterdir()),
            ["TestCharacter.json", "TestCharacter.stats.json"],
        )
        self.assertEqual(
            sorted(str(path.relative_to(self.art)) for path in self.art.rglob("*") if path.is_file()),
            sorted(["atlas.png", "atlas_1.png", str(Path("sounds") / "sample.WAV")]),
        )
        for filename in ("TestCharacter.json", "TestCharacter.stats.json"):
            self.assertEqual((self.stream / filename).read_bytes(), (self.source / filename).read_bytes())
        self.assertEqual((self.art / "atlas_1.png").read_bytes(), b"second atlas")
        self.assertEqual((self.art / "sounds" / "sample.WAV").read_bytes(), b"sound")
        self.assertTrue((self.source / "animations.json").is_file())

    def test_repeated_deployment_preserves_metadata_and_unrelated_files(self) -> None:
        deploy_character(self.manifest, self.project)
        metadata = self.art / "atlas.png.meta"
        metadata.write_text("existing Unity GUID")
        unrelated = self.stream / "designer.json"
        unrelated.write_text("preserve")
        (self.source / "atlas.png").write_bytes(b"updated")
        deploy_character(self.manifest, self.project)
        self.assertEqual((self.art / "atlas.png").read_bytes(), b"updated")
        self.assertEqual(metadata.read_text(), "existing Unity GUID")
        self.assertEqual(unrelated.read_text(), "preserve")

    def test_missing_page_fails_before_any_copy(self) -> None:
        (self.source / "atlas_1.png").unlink()
        with self.assertRaisesRegex(FileNotFoundError, "Referenced atlas page"):
            deploy_character(self.manifest, self.project)
        self.assertFalse(self.stream.exists())
        self.assertFalse(self.art.exists())

    def test_legacy_single_page_and_optional_stats_and_sounds(self) -> None:
        self.write_json(self.manifest, {"image": "atlas.png"})
        (self.source / "TestCharacter.stats.json").unlink()
        (self.source / "sounds" / "sample.WAV").unlink()
        self.assertEqual(deploy_character(self.manifest, self.project), (1, 1))

    def test_invalid_manifest_and_page_names_are_rejected(self) -> None:
        for document in (
            [],
            {},
            {"images": "atlas.png"},
            {"images": ["../atlas.png"]},
            {"images": ["..\\atlas.png"]},
            {"images": ["C:\\atlas.png"]},
            {"images": ["states.json"]},
        ):
            with self.subTest(document=document):
                self.write_json(self.manifest, document)
                with self.assertRaises(ValueError):
                    deploy_character(self.manifest, self.project)
                self.assertFalse(self.stream.exists())

    def test_missing_project_assets_fails(self) -> None:
        with self.assertRaisesRegex(FileNotFoundError, "Unity project Assets"):
            deploy_character(self.manifest, self.root / "MissingProject")

    def test_source_destination_overlap_is_rejected_before_copy(self) -> None:
        self.stream.mkdir(parents=True)
        overlap_manifest = self.stream / self.manifest.name
        self.write_json(overlap_manifest, {"image": "atlas.png"})
        (self.stream / "atlas.png").write_bytes(b"atlas")
        with self.assertRaisesRegex(ValueError, "source and destination must differ"):
            deploy_character(overlap_manifest, self.project)
        self.assertFalse(self.art.exists())

    def test_cli_converts_then_deploys_by_default(self) -> None:
        result, output = self.run_cli(str(self.source))
        self.assertEqual(result, 0, output)
        self.assertIn("[deploy]", output)
        document = json.loads((self.stream / self.manifest.name).read_text())
        self.assertIn("stance", document["animations"])
        self.assertEqual(document["images"], ["atlas.png", "atlas_1.png"])
        self.assertTrue((self.art / "atlas_1.png").is_file())

    def test_cli_help_describes_deployment_without_formatting_artifacts(self) -> None:
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            with self.assertRaises(SystemExit) as exit_context:
                mugen_to_flare.main(["--help"])
        self.assertEqual(exit_context.exception.code, 0)
        self.assertIn("--no-deploy", output.getvalue())
        self.assertIn("--project-root", output.getvalue())
        self.assertIn("99%", output.getvalue())
        self.assertNotIn("option_strings", output.getvalue())

    def test_no_deploy_keeps_conversion_local(self) -> None:
        result, output = self.run_cli(str(self.source), "--no-deploy")
        self.assertEqual(result, 0, output)
        self.assertNotIn("[deploy]", output)
        self.assertIn("stance", json.loads(self.manifest.read_text())["animations"])
        self.assertFalse(self.stream.exists())
        self.assertFalse(self.art.exists())

    def test_direct_conversion_api_does_not_deploy(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()):
            result = mugen_to_flare.convert_character(self.source)
        self.assertEqual(result, self.manifest)
        self.assertIn("stance", json.loads(self.manifest.read_text())["animations"])
        self.assertFalse(self.stream.exists())
        self.assertFalse(self.art.exists())

    def test_default_project_root_comes_from_script_location(self) -> None:
        script_path = self.project / "Tools" / "Scripts" / "mugen_import" / "mugen_to_flare.py"
        output = io.StringIO()
        with patch.object(mugen_to_flare, "__file__", str(script_path)):
            with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                result = mugen_to_flare.main([str(self.source)])
        self.assertEqual(result, 0, output.getvalue())
        self.assertTrue((self.stream / self.manifest.name).is_file())
        self.assertTrue((self.art / "atlas.png").is_file())

    def test_multiple_valid_character_directories(self) -> None:
        second_source = self.source.with_name("SecondCharacter")
        shutil.copytree(self.source, second_source)
        result, output = self.run_cli(str(self.source), str(second_source))
        self.assertEqual(result, 0, output)
        second_stream = self.stream.with_name("SecondCharacter")
        second_art = self.art.with_name("SecondCharacter")
        self.assertTrue((self.stream / self.manifest.name).is_file())
        self.assertEqual(
            sorted(path.name for path in second_stream.iterdir()),
            ["SecondCharacter.json", "SecondCharacter.stats.json"],
        )
        self.assertTrue((second_art / "atlas_1.png").is_file())

    def test_atlas_override_keeps_character_folder(self) -> None:
        result, output = self.run_cli(str(self.source), "--atlas-name", "CustomAtlas")
        self.assertEqual(result, 0, output)
        self.assertEqual(
            sorted(path.name for path in self.stream.iterdir()),
            ["CustomAtlas.json", "CustomAtlas.stats.json"],
        )

    def test_cli_failures_return_nonzero_and_continue_other_characters(self) -> None:
        result, output = self.run_cli(str(self.root / "Missing"), str(self.source))
        self.assertEqual(result, 1)
        self.assertIn("[error]", output)
        self.assertTrue((self.stream / self.manifest.name).is_file())

    def test_cli_missing_atlas_returns_nonzero(self) -> None:
        (self.source / "atlas_1.png").unlink()
        result, output = self.run_cli(str(self.source))
        self.assertEqual(result, 1)
        self.assertIn("Referenced atlas page not found", output)
        self.assertFalse(self.stream.exists())

    def test_invalid_atlas_override_does_not_write_outside_source(self) -> None:
        result, output = self.run_cli(str(self.source), "--atlas-name", "..\\Outside")
        self.assertEqual(result, 1)
        self.assertIn("filename without a path", output)
        self.assertFalse(self.stream.exists())

    def test_batch_reuses_deployment_helper(self) -> None:
        with patch.object(_importall, "_HERE", str(self.root)):
            with patch.object(_importall, "deploy_character", return_value=(2, 3)) as deploy:
                self.assertEqual(_importall.deploy(self.source.name), (2, 3))
                deploy.assert_called_once_with(self.manifest)


if __name__ == "__main__":
    unittest.main()
