using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Net.Sockets;
using ScreenshotInterface;
using System.Timers;
using System.Threading;

namespace AVIStreamCLI
{

    public interface ScreenshotSink
    {
        void processData(byte[] imageData, int width, int height, PixelFormat fmt);
        void close();
    }

    public class DummySink : ScreenshotSink
    {
        public void processData(byte[] imageData, int width, int height, PixelFormat fmt)
        {
        }

        public void close() { }
    }

    class IPCHostSink : ScreenshotSink
    {
        private ScreenshotInterface.ScreenshotInterface ssInterface;
        private int pid;
        private Guid requestId;

        public IPCHostSink(int pid, Guid requestId, ScreenshotInterface.ScreenshotInterface ssInterface)
        {
            this.ssInterface = ssInterface;
            this.pid = pid;
            this.requestId = requestId;
        }

        public void processData(byte[] imageData, int width, int height, PixelFormat fmt)
        {
            ssInterface.OnScreenshotResponse(pid, requestId, width, height, fmt, imageData);
        }

        public void close() { }
    }

    public class FrameServer : ScreenshotSink
    {
        private Socket ffmpegWrapper;
        private bool inited;

        public FrameServer(string ffmpegWrapperHost, int ffmpegWrapperPort)
        {
            inited = false;
            ffmpegWrapper = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            ffmpegWrapper.Connect(ffmpegWrapperHost, ffmpegWrapperPort);
        }

        private void sendHeader(int width, int height, PixelFormat format)
        {
            byte fmt;

            switch (format)
            {
                case PixelFormat.Format24bppRgb:
                    fmt = 1;
                    break;
                case PixelFormat.Format32bppArgb:
                    fmt = 2;
                    break;
                default:
                    throw new ArgumentException("Cannot handle pixel format " + format);
            }

            MemoryStream ms = new MemoryStream();
            BinaryWriter bin = new BinaryWriter(ms);
            bin.Write(width);
            bin.Write(height);
            bin.Write(fmt);

            ffmpegWrapper.Send(ms.GetBuffer(), (int)ms.Length, SocketFlags.None);
            bin.Close();
        }

        public void close()
        {
            if (ffmpegWrapper != null && ffmpegWrapper.Connected)
            {
                ffmpegWrapper.Close();
            }
        }

        /// <summary>
        /// The callback for when the screenshot has been taken
        /// </summary>
        /// <param name="clientPID"></param>
        /// <param name="status"></param>
        /// <param name="screenshotResponse"></param>
        public void processData(byte[] img, int width, int height, PixelFormat fmt)
        {
            if (!inited)
            {
                sendHeader(width, height, fmt);
                inited = true;
            }

            lock (ffmpegWrapper)
            {
                ffmpegWrapper.Send(img, img.Length, SocketFlags.None);
            }
        }
    }
}
