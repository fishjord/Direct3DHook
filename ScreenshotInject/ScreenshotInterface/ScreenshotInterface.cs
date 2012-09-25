using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using EasyHook;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Drawing.Imaging;

namespace ScreenshotInterface
{
    public enum Direct3DVersion
    {
        Unknown,
        AutoDetect,
        Direct3D9,
        Direct3D10,
        Direct3D10_1,
        Direct3D11,
        Direct3D11_1,
    }

    #region Screenshot Requests
    public abstract class ScreenshotRequest : MarshalByRefObject
    {
        private Guid requestId;
        public Guid RequestId
        {
            get { return requestId; }
        }

        public ScreenshotRequest()
        {
            this.requestId = Guid.NewGuid();
        }            
    }

    public class PauseRequest : ScreenshotRequest
    {
    }

    public class ResumeRequest : ScreenshotRequest
    {
    }

    public class StopRequest : ScreenshotRequest
    {
    }

    public class CaptureRequest : ScreenshotRequest
    {
        private Rectangle region;
        private double fps;

        public Rectangle Region
        {
            get { return region; }
        }

        public double Fps
        {
            get { return fps; }
        }

        public CaptureRequest(Rectangle region, double fps)
        {
            this.region = region;
            this.fps = fps;
        }

    }

    public class StreamRequest : CaptureRequest
    {
        private string host;
        private int port;

        public string Host
        {
            get { return host; }
        }

        public int Port
        {
            get { return port; }
        }

        public StreamRequest(Rectangle region, string streamTo, int onPort, double fps)
            : base(region, fps)
        {
            this.host = streamTo;
            this.port = onPort;
        }

    }
    #endregion



    public class ScreenshotResponse : MarshalByRefObject
    {
        private Guid _requestId;
        private int width;
        private int height;
        private PixelFormat fmt;
        private byte[] rawImage;

        public Guid RequestId
        {
            get
            {
                return _requestId;
            }
        }

        public int Width
        {
            get { return width; }
        }

        public int Height
        {
            get { return height; }
        }

        public PixelFormat Fmt
        {
            get { return fmt; }
        }

        public byte[] RawImage
        {
            get { return rawImage; }
        }

        public ScreenshotResponse(Guid requestId, int width, int height, PixelFormat fmt, byte[] rawImage)
        {
            _requestId = requestId;
            this.width = width;
            this.height = height;
            this.fmt = fmt;
            this.rawImage = rawImage;
        }
    }

    public class ScreenshotInterface : MarshalByRefObject
    {
        public void ReportError(Int32 clientPID, Exception e)
        {
            OnMessage(clientPID, MessageType.error, "A client process (" + clientPID + ") has reported an error\r\n" + e.Message);
            //MessageBox.Show(e.ToString(), "A client process (" + clientPID + ") has reported an error...", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
        }

        public bool Ping(Int32 clientPID)
        {
            /*
             * We should just check if the client is still in our list
             * of hooked processes...
             */
            lock (HookManager.ProcessList)
            {
                return HookManager.HookedProcesses.Contains(clientPID);
            }
        }

        public ScreenshotRequest GetScreenshotRequest(Int32 clientPID)
        {
            return ScreenshotManager.GetScreenshotRequest(clientPID);
        }


        private class RequestNotificationThreadParameter
        {
            public Int32 ClientPID;
            public ScreenshotResponse Response;
        }

        private void ProcessResponseThread(object data)

        {
            RequestNotificationThreadParameter responseData = (RequestNotificationThreadParameter)data;
            ScreenshotManager.SetScreenshotResponse(responseData.ClientPID, responseData.Response);
        }

        public void OnScreenshotResponse(Int32 clientPID, Guid requestId, int width, int height, PixelFormat pixFmt, byte[] bitmapData)
        {
            Thread t = new Thread(new ParameterizedThreadStart(ProcessResponseThread));
            t.Start(new RequestNotificationThreadParameter() { ClientPID = clientPID, Response = new ScreenshotResponse(requestId, width, height, pixFmt, bitmapData) });
        }

        public void OnMessage(Int32 clientPID, MessageType type, string message)
        {
            ScreenshotManager.AddScreenshotMessage(clientPID, type, message);
        }

    }
}
