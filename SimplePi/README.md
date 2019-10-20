# Simple Pi

## Overview

This instruction set configures a Raspberry Pi to be a very simple web server with a simple web page. Assumes all networking connectivity is DHCP.

## Installation

Install latest lite version of Raspian from <https://www.raspberrypi.org/downloads/raspbian/>

Boot up the device.

Log in: <br/>
&nbsp;&nbsp;&nbsp;&nbsp;User: **pi**<br/>
&nbsp;&nbsp;&nbsp;&nbsp;Pwd: **raspberry**

```bash
sudo apt update
sudo apt upgrade
sudo apt dist-upgrade
sudo apt autoremove

sudo raspi-config
```

In raspi-config set the following:
  Change pwd
  Change Host Name
  Locale - Country, Keyboard
  Time Zone: Loas Angeles
  SSH Server On
  Resize Partition

```bash
mkdir web
cd web
wget -N https://raw.githubusercontent.com/tracsman/Examples/master/SimplePi/index.html
wget -N https://raw.githubusercontent.com/tracsman/Examples/master/SimplePi/favicon.ico
wget -N https://raw.githubusercontent.com/tracsman/Examples/master/SimplePi/startsite.sh

chmod +x startsite.sh
nano index.html # update server name
crontab -e
@reboot ( sleep 60 ; sh /home/pi/web/startsite.sh )
```

reboot and test web page

sudo halt to power down
