#!/bin/bash
set -e
cd "$(dirname "$0")"
game_path="$(osascript -e 'POSIX path of (choose folder with prompt "选择含 ReincarnationSquad.exe 的游戏目录")')"
python3 install.py --all --apply --game "$game_path"
read -r -p '安装完成。请在游戏内启用Mod并确认代码许可。按回车关闭。'
