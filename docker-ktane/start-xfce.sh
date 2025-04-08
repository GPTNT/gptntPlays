#!/bin/bash

Xvfb :1 -screen 0 1024x768x24 &
export DISPLAY=:1
#startxfce4 &
#x11vnc -display :1 -forever -rfbport 5900 &

sleep 5

python3 /app/request_handler.py &
/app/ktane/ktane.x86_64 &

wait