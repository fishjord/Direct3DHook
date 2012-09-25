#define DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using EasyHook;
using System.IO;
using System.Runtime.Remoting;
using System.Collections;
using System.Drawing.Imaging;
using System.Threading;
using AVIStreamCLI;
using ScreenshotInterface;
using System.Drawing;

namespace ScreenshotInject
{

    internal abstract class BaseDXHook : IDXHook
    {
        public BaseDXHook(ScreenshotInterface.ScreenshotInterface ssInterface)
        {
            this.Interface = ssInterface;
        }

        int _processId = 0;
        protected int ProcessId
        {
            get
            {
                if (_processId == 0)
                {
                    _processId = RemoteHooking.GetCurrentProcessId();
                }
                return _processId;
            }
        }

        protected virtual string HookName
        {
            get
            {
                return "BaseDXHook";
            }
        }

        #region Message Handling

        protected void ErrorMessage(string message)
        {
            try
            {
                Interface.OnMessage(this.ProcessId, MessageType.error, HookName + ": " + message);
            }
            catch (RemotingException re)
            {
                // Ignore remoting exceptions
            }
        }

        protected void InfoMessage(string message)
        {
            try
            {
                Interface.OnMessage(this.ProcessId, MessageType.info, HookName + ": " + message);
            }
            catch (RemotingException re)
            {
                // Ignore remoting exceptions
            }
        }

        protected void WarningMessage(string message)
        {
            try
            {
                Interface.OnMessage(this.ProcessId, MessageType.warning, HookName + ": " + message);
            }
            catch (RemotingException re)
            {
                // Ignore remoting exceptions
            }
        }

        protected void DebugMessage(string message)
        {
#if DEBUG
            try
            {
                Interface.OnMessage(this.ProcessId, MessageType.debug, HookName + ": " + message);
            }
            catch (RemotingException re)
            {
                // Ignore remoting exceptions
            }
#endif
        }

        #endregion

        protected IntPtr[] GetVTblAddresses(IntPtr pointer, int numberOfMethods)
        {
            List<IntPtr> vtblAddresses = new List<IntPtr>();

            IntPtr vTable = Marshal.ReadIntPtr(pointer);
            for (int i = 0; i < numberOfMethods; i++)
                vtblAddresses.Add(Marshal.ReadIntPtr(vTable, i * IntPtr.Size)); // using IntPtr.Size allows us to support both 32 and 64-bit processes

            return vtblAddresses.ToArray();
        }

        #region Frame Sending Code

        private ScreenshotSink frameTarget;
        private bool paused = true;
        private double freq;
        protected DateTime lastScreenshot;
        protected Rectangle captureRect;

        private object requestLock = new object();

        public bool Paused
        {
            get { return paused; }
        }

        public void newRequest(ScreenshotRequest request)
        {
            try
            {
                lock (requestLock)
                {
                    if (request is PauseRequest)
                    {
                        paused = true;
                    }
                    else if (request is StopRequest)
                    {
                        paused = true;
                        if (frameTarget != null)
                        {
                            frameTarget.close();
                            frameTarget = null;
                        }
                    }
                    else if (request is ResumeRequest)
                    {
                        if (frameTarget != null)
                        {
                            paused = false;
                        }
                    }
                    else if (request is StreamRequest)
                    {
                        StreamRequest streamRequest = (StreamRequest)request;
                        frameTarget = new FrameServer(streamRequest.Host, streamRequest.Port);
                        this.captureRect = streamRequest.Region;
                        this.freq = 1 / streamRequest.Fps;
                        paused = false;
                    }
                    else if (request is CaptureRequest)
                    {
                        CaptureRequest captureRequest = (CaptureRequest)request;
                        frameTarget = new IPCHostSink(ProcessId, request.RequestId, Interface);
                        this.captureRect = captureRequest.Region;
                        this.freq = 1 / captureRequest.Fps;
                        paused = false;
                    }
                }
            }
            catch (Exception e)
            {
                paused = true;
                if (frameTarget != null)
                {
                    frameTarget.close();
                    frameTarget = null;
                }
                ErrorMessage("Exception when processing request" + request + "\n\r" + e);
                paused = true;
            }
        }

        protected void queueRawImage(byte[] imageData, int width, int height, PixelFormat fmt)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (requestLock)
                {
                    if (frameTarget == null)
                    {
                        return;
                    }

                    try
                    {
                        frameTarget.processData(imageData, width, height, fmt);
                    }
                    catch (Exception e)
                    {
                        paused = true;
                        ErrorMessage("Error when passing image to the sink\n\r" + e);
                    }
                }
            });
        }
        #endregion

        public virtual void Cleanup()
        {
            lock (requestLock)
            {
                if (frameTarget != null)
                {
                    frameTarget.close();
                    frameTarget = null;
                }
            }
        }

        protected bool readyForScreenshot()
        {
            return !paused && (DateTime.Now - lastScreenshot).TotalMilliseconds > freq;
        }

        #region IDXHook Members

        public ScreenshotInterface.ScreenshotInterface Interface
        {
            get;
            set;
        }

        public abstract void Hook();

        #endregion
    }
}
