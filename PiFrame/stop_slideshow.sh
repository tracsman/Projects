#!/bin/bash

if [ ! -z "$1" ]; then
    PROCESS=$1
    PIDS= ps ax | grep $PROCESS | sed -e "s/.*killp.*//" -e "s/ *\ ([0-9]\{$.*/\1/" | grep -v "^$"
    if [ ! -z "$PIDS" ]; then
        echo Killing $PROCESS pid=$PIDS
        kill -9 $PIDS
    else
        echo No process $PROCESS in process list
    fi
else
    echo Usage : $0 process_name
fi