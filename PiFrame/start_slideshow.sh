#!/bin/bash

export DISPLAY=:0.0
XAUTHORITY=/home/pi/.Xauthority
feh -q -Z -F -z -Y -D 20  /home/pi/PiFrame/photos &
echo "PiFrame slide show started"