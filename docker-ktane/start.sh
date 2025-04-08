#!/bin/bash

Xvfb :1 -screen 0 800x600x24 -ac -nolisten tcp -shmem &
export DISPLAY=:1

sleep 5

python3 /app/request_handler.py &

# some env vars 
export UNITY_LIMIT_FRAMERATE=8
export __GL_SYNC_TO_VBLANK=1
export __GL_SYNC_DISPLAY_DEVICE=virtual1
export __GL_YIELD="USLEEP"

# HAS TO RUN FROM THE KTANE DIRECTORY FOR MODS TO LOAD
cd /app/ktane
./ktane.x86_64 &

sleep 5
xdotool getactivewindow 2>/dev/null

wait