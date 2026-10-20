#!/usr/bin/env python3
"""Compatibility entry: generate from the canonical school data, never rewrite it.
The retired linkedEdge authoring generator is preserved in the conflict backup.
"""
from prepare_original_assets import main as assets
from build_content import main
if __name__ == '__main__':
    assets()
    main()
