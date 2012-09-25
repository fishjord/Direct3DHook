using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing.Imaging;

namespace ScreenshotInject
{
    class ImageCache
    {

        private Guid requestId;
        private int width;
        private int height;
        private PixelFormat pixFmt;
        private List<byte[]> cache;

        public Guid RequestId
        {
            get
            {
                lock (cache)
                {
                    return requestId;
                }
            }
        }
        public int Width
        {
            get {
                lock (cache)
                {
                    return width;
                }
            }
        }
        public int Height
        {
            get {
                lock (cache)
                {
                    return height;
                }
            }
        }
        public PixelFormat PixFmt
        {
            get {
                lock (cache)
                { 
                    return pixFmt; 
                }
            }
        }
        public int Length
        {
            get {
                lock (cache)
                {
                    return cache.Count;
                }
            }
        }

        public ImageCache() : this(Guid.Empty, 0, 0, PixelFormat.Undefined)
        {
        }

        public ImageCache(Guid requestId, int width, int height, PixelFormat format)
        {
            this.requestId = requestId;
            this.width = width;
            this.height = height;
            this.pixFmt = format;
            this.cache = new List<byte[]>();
        }

        public void addImage(byte[] resp)
        {
            lock (cache)
            {
                cache.Add(resp);
            }
        }


        public List<byte[]> flushCache()
        {
            return flushCache(width, height, pixFmt, requestId);
        }

        public List<byte[]> flushCache(int newWidth, int newHeight, PixelFormat newPixFmt, Guid newRequestId)
        {
            List<byte[]> ret;
            lock (cache)
            {
                requestId = newRequestId;
                width = newWidth;
                height = newHeight;
                pixFmt = newPixFmt;
                ret = cache;
                cache = new List<byte[]>();
            }

            return ret;
        }
    }
}
