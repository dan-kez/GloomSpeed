#!/bin/bash
# Finder opens .command files in Terminal when double-clicked. The prompt keeps the result on screen.
bash "$(dirname "$0")/macos.sh" uninstall
echo
read -r -p "Press Return, then close this window. " _
