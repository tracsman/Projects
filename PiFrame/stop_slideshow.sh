#!/bin/bash

kill $(ps aux | grep '[f]eh' | awk '{print $2}')
echo "PiFrame slide show stopped"
