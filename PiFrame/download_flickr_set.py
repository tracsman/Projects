#!/usr/bin/env python 

import flickrapi
import requests
import os
import re
import timestamp

FLICKR_KEY = "7f34f6af087f13a6e9c2a8727a7cf6ee"
FLICKR_SECRET = "7290584b44a9c8fb"
USER_ID = "8518455@N07"
SET_ID = "72157710172154342"

def make_url(photo):
    # url_template = "http://farm{farm-id}.staticflickr.com/
    #                 {server-id}/{id}_{secret}_[mstzb].jpg"
    # The Flickr api uses a variety of labels for image sizes:
    # s - square 75x75
    # q - large square
    # t - thumbnail
    # m - small
    # n - small
    # z - 640x 480
    # c - medium 800x600
    # b - large - 1024 x 768
    # o - original 2400x1800

    photo['filename'] = "%(id)s_%(secret)s_z.jpg" % photo
    url = ("http://farm%(farm)s.staticflickr.com/%(server)s/%(filename)s" 
           % photo)
    return url, photo['filename']

def main():
    print("Running at %s" %datetime.datetime.now())
    #get new imaged from flickr
    print(" ---> Requesting photos...")
    count = 0
    update = False
    flickr = flickrapi.FlickrAPI(FLICKR_KEY,FLICKR_SECRET)
    photos = flickr.walk_set(SET_ID)
    for photo in photos:
        count += 1
        url, filename = make_url(photo.attrib)
        path = '/home/pi/PiFrame/photos/%s' % filename
        try:
            image_file = open(path)
            print(" ---> Already have %s" % filename)
        except IOError:
            print(" ---> Downloading %s" % filename)
            r = requests.get(url)      
            image_file = open(path, 'w')
            image_file.write(r.content)
            image_file.close()
            update = True

    #check to see if it needs to remove photos from folder
    filelist = os.listdir("/home/pi/PiFrame/photos")
    if count < len(filelist):
        print(" ---> Removing photos")
        for f in filelist:
            pics = flickr.walk_set(SET_ID)
            for pic in pics:
                url, filename = make_url(pic.attrib)
                matchObj = re.match(f, filename)
                if matchObj:
                    break
            else:
                print(" ---> Deleting %s" %f)
                os.remove("/home/pi/PiFrame/photos/%s" %f)
                update = True    

    #if it added or removed a photo, update slideshow
    if update == True:
        print(" ---> Restarting slideshow")
        os.system("kill $(ps aux | grep '[f]eh' | awk '{print $2}')")
        os.system("/home/pi/PiFrame/start_slideshow.sh &")
    
    print("End download_flickr_set.py run")
if __name__ == '__main__':
    main()
