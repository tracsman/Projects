#!/usr/bin/env python 

import flickrapi
import requests
import os
import re

FLICKR_KEY = "7f34f6af087f13a6e9c2a8727a7cf6ee"
FLICKR_SECRET = "7290584b44a9c8fb"
USER_ID = "123390813@N02"
SET_ID = "72157644059098505"

def make_url(photo):
    # url_template = "http://farm{farm-id}.staticflickr.com/
    #                 {server-id}/{id}_{secret}_[mstzb].jpg"
    photo['filename'] = "%(id)s_%(secret)s_z.jpg" % photo
    url = ("http://farm%(farm)s.staticflickr.com/%(server)s/%(filename)s" 
           % photo)
    return url, photo['filename']

def main():
    #get new imaged from flickr
    print " ---> Requesting photos..."
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
            print " ---> Already have %s" % url
        except IOError:
            print " ---> Downloading %s" % url
            r = requests.get(url)      
            image_file = open(path, 'w')
            image_file.write(r.content)
            image_file.close()
            update = True

    #check to see if it needs to remove photos from folder
    filelist = os.listdir("/home/pi/PiFrame/photos")
    if count < len(filelist):
        print " ---> Removing photos"
        for f in filelist:
            pics = flickr.walk_set(SET_ID)
            print f
            for pic in pics:
                url, filename = make_url(pic.attrib)
                matchObj = re.match(f, filename)
                if matchObj:
                    print " ---> Found %s, matched %s" %(f,filename)
                    break
            else:
                print " ---> Deleting %s" %f
                os.remove("/home/pi/PiFrame/photos/%s" %f)
                update = True    

    #if it added or removed a photo, update slideshow
    if update == True:
        print " ---> Restarting slideshow"
        os.system("kill $(ps aux | grep '[f]eh' | awk '{print $2}')")
        os.system("/home/pi/PiFrame/start_slideshow.sh")

if __name__ == '__main__':
    main()
