#!/usr/bin/env python3
"""Compatibility entry point for the shared clean-package installer."""
from pathlib import Path
import subprocess
import sys

if __name__ == '__main__':
    installer = Path(__file__).resolve().parents[2] / 'scripts/install.py'
    raise SystemExit(subprocess.call([sys.executable, str(installer), '--packages', 'appearance.muyifeng', *sys.argv[1:]]))
