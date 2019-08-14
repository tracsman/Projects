# PiFrame

Source code for a Flikr based Raspberry PI picture frame based off of the frame project from Beaconsfield at [Instructables](https://www.instructables.com/id/Internet-Photo-Frame/).

## Raspberry Pi OS Install

1. I started with the latest mininal Raspian install (at the time "[Raspbian Buster Lite](https://www.raspberrypi.org/downloads/raspbian/)")

2. Once started, directly connect to the Pi with a keyboard/monitor
3. Use raspi-config to configure:

    - update new Pi password
    - set new network hostname if desired
    - configure wifi settings (DHCP)
    - set startup to be "Desktop Autologin"
    - configure locaiation options; locale/timezone/keyboard/wifi/etc
    - under interfacing options, enable SSH
    - under advanced options, expand file system (if needed)
    - when done, select finish to reboot

4. You can now connect via SSH or continue locally
5. Update/upgrade/add/remove the following packages:

    ``` bash
    sudo apt-get update
    sudo apt-get upgrade
    sudo apt-get install ldxe
    sudo apt-get install feh
    sudo apt-get remove xscreensaver
    sudo apt-get autoremove
    ```

6. Create two new directories under /home/pi, move to the PiFrame directory, download script files from GitHub, add PiFrame directory to path, and update script file permissions to allow execution:

    ``` bash
    mkdir /home/pi/PiFrame
    mkdir /home/pi/PiFrame/photos

    cd /home/pi/PiFrame

    wget https://raw.githubusercontent.com/tracsman/Projects/master/PiFrame/download_flickr_set.py
    wget https://raw.githubusercontent.com/tracsman/Projects/master/PiFrame/start_slideshow.sh
    wget https://raw.githubusercontent.com/tracsman/Projects/master/PiFrame/stop_slideshow.sh

    PATH=$PATH:/home/pi/PiFrame

    sudo chmod 755 download_flickr_set.py
    sudo chmod 755 start_slideshow.sh
    sudo chmod 755 stop_slideshow.sh
    ```

7. Get key and account data from Flickr
8. Update download_flickr_set.py with the Flickr account and key data (script lines 8 - 11), also select the picture size desired (script line 27) based on the [Flickr Pic Size Info](#flickr-pic-size-info) section below (default size is 'z').

    ``` bash
    sudo nano download_flickr_set.py
    ```

    To exit from the nano editor first press ctrl+o and then enter to save the file, then ctrl+x to exit back to the console prompt.
9. Add crontab job to run pic check every minute:

- crontab -e
- add line after remarks (# lines): ```* * * * * python /home/pi/PiFrame/download_flickr_set.py >> /home/pi/PiFrame/flickr.log```
- ctrl+o and ctrl+x to save and exit
- ```crontab -l``` to see that the job is saved correctly

## Flickr Pic Size Info

The Flickr api uses a variety of labels for image sizes:

- s - square 75x75
- q - large square
- t - thumbnail
- m - small
- n - small
- z - 640x 480
- c - medium 800x600
- b - large - 1024 x 768
- o - original 2400x1800

## Minutiae

- [Modem Info](https://www.development-cycle.com/2017/04/27/zte-mf823-inside/)
- [Pi Downloads](https://packaging.python.org/tutorials/installing-packages/)
- [My Public Flickr](https://www.flickr.com/photos/8518455@N07/albums/72157710172154342)
- [Script Updates](https://gist.github.com/Jarvl/3799acac27283f80641d57804faac9ae)
- [Pi Configuration](https://www.instructables.com/id/Ultimate-Raspberry-Pi-Configuration-Guide/#step0)
- [Starting point project](https://www.instructables.com/id/Internet-Photo-Frame/)
- [Another interesting frame](https://www.instructables.com/id/Raspberry-Pi-Digital-Picture-Frame/)
