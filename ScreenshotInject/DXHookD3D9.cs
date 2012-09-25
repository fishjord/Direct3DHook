using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SlimDX.Direct3D9;
using EasyHook;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading;
using System.Drawing;
using SlimDX;
using System.Drawing.Imaging;

namespace ScreenshotInject
{
    internal class DXHookD3D9 : BaseDXHook
    {
        public DXHookD3D9(ScreenshotInterface.ScreenshotInterface ssInterface)
            : base(ssInterface)
        {
        }

        LocalHook Direct3DDevice_EndSceneHook = null;
        LocalHook Direct3DDevice_ResetHook = null;
        object _lockRenderTarget = new object();
        Surface _renderTarget;

        protected override string HookName
        {
            get
            {
                return "DXHookD3D9";
            }
        }

        const int D3D9_DEVICE_METHOD_COUNT = 119;
        public override void Hook()
        {
            this.DebugMessage("Hook: Begin");
            // First we need to determine the function address for IDirect3DDevice9
            Device device;
            List<IntPtr> id3dDeviceFunctionAddresses = new List<IntPtr>();
            this.DebugMessage("Hook: Before device creation");
            using (Direct3D d3d = new Direct3D())
            {
                this.DebugMessage("Hook: Device created");
                using (device = new Device(d3d, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing, new PresentParameters() { BackBufferWidth = 1, BackBufferHeight = 1 }))
                {
                    id3dDeviceFunctionAddresses.AddRange(GetVTblAddresses(device.ComPointer, D3D9_DEVICE_METHOD_COUNT));
                }
            }

            // We want to hook each method of the IDirect3DDevice9 interface that we are interested in

            // 42 - EndScene (we will retrieve the back buffer here)
            Direct3DDevice_EndSceneHook = LocalHook.Create(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.EndScene],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                // (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x1ce09),
                // A 64-bit app would use 0xff18
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_EndSceneDelegate(EndSceneHook),
                this);

            // 16 - Reset (called on resolution change or windowed/fullscreen change - we will reset some things as well)
            Direct3DDevice_ResetHook = LocalHook.Create(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Reset],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                //(IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x58dda),
                // A 64-bit app would use 0x3b3a0
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_ResetDelegate(ResetHook),
                this);

            /*
             * Don't forget that all hooks will start deactivated...
             * The following ensures that all threads are intercepted:
             * Note: you must do this for each hook.
             */
            Direct3DDevice_EndSceneHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            Direct3DDevice_ResetHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            this.DebugMessage("Hook: End");
        }

        /// <summary>
        /// Just ensures that the surface we created is cleaned up.
        /// </summary>
        public override void Cleanup()
        {
            base.Cleanup();
            try
            {
                lock (_lockRenderTarget)
                {
                    if (_renderTarget != null)
                    {
                        _renderTarget.Dispose();
                        _renderTarget = null;
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// The IDirect3DDevice9.EndScene function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_EndSceneDelegate(IntPtr device);

        /// <summary>
        /// The IDirect3DDevice9.Reset function definition
        /// </summary>
        /// <param name="device"></param>
        /// <param name="presentParameters"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_ResetDelegate(IntPtr device, ref D3DPRESENT_PARAMETERS presentParameters);

        /// <summary>
        /// Reset the _renderTarget so that we are sure it will have the correct presentation parameters (required to support working across changes to windowed/fullscreen or resolution changes)
        /// </summary>
        /// <param name="devicePtr"></param>
        /// <param name="presentParameters"></param>
        /// <returns></returns>
        int ResetHook(IntPtr devicePtr, ref D3DPRESENT_PARAMETERS presentParameters)
        {
            using (Device device = Device.FromPointer(devicePtr))
            {
                PresentParameters pp = new PresentParameters()
                {
                    AutoDepthStencilFormat = (Format)presentParameters.AutoDepthStencilFormat,
                    BackBufferCount = presentParameters.BackBufferCount,
                    BackBufferFormat = (Format)presentParameters.BackBufferFormat,
                    BackBufferHeight = presentParameters.BackBufferHeight,
                    BackBufferWidth = presentParameters.BackBufferWidth,
                    DeviceWindowHandle = presentParameters.DeviceWindowHandle,
                    EnableAutoDepthStencil = presentParameters.EnableAutoDepthStencil,
                    FullScreenRefreshRateInHertz = presentParameters.FullScreen_RefreshRateInHz,
                    Multisample = (MultisampleType)presentParameters.MultiSampleType,
                    MultisampleQuality = presentParameters.MultiSampleQuality,
                    PresentationInterval = (PresentInterval)presentParameters.PresentationInterval,
                    PresentFlags = (PresentFlags)presentParameters.Flags,
                    SwapEffect = (SwapEffect)presentParameters.SwapEffect,
                    Windowed = presentParameters.Windowed
                };
                lock (_lockRenderTarget)
                {
                    if (_renderTarget != null)
                    {
                        _renderTarget.Dispose();
                        _renderTarget = null;
                    }
                }
                // EasyHook has already repatched the original Reset so calling it here will not cause an endless recursion to this function
                return device.Reset(pp).Code;
            }
        }


        private int width;
        private int height;
        private PixelFormat format;
        private byte[] data = new byte[1];
        /// <summary>
        /// Hook for IDirect3DDevice9.EndScene
        /// </summary>
        /// <param name="devicePtr">Pointer to the IDirect3DDevice9 instance. Note: object member functions always pass "this" as the first parameter.</param>
        /// <returns>The HRESULT of the original EndScene</returns>
        /// <remarks>Remember that this is called many times a second by the Direct3D application - be mindful of memory and performance!</remarks>
        int EndSceneHook(IntPtr devicePtr)
        {
            using (Device device = Device.FromPointer(devicePtr))
            {
                // If you need to capture at a particular frame rate, add logic here decide whether or not to skip the frame
                try
                {
                    #region Screenshot Request
                    // Is there a screenshot request? If so lets grab the backbuffer
                    lock (_lockRenderTarget)
                    {
                        if (base.readyForScreenshot())
                        {
                            lastScreenshot = DateTime.Now;
                            try
                            {
                                // First ensure we have a Surface to the render target data into
                                if (_renderTarget == null || _renderTarget.Disposed)
                                {
                                    // Create offscreen surface to use as copy of render target data
                                    using (SwapChain sc = device.GetSwapChain(0))
                                    {
                                        width = sc.PresentParameters.BackBufferWidth;
                                        height = sc.PresentParameters.BackBufferHeight;
                                        format = PixelFormat.Format32bppArgb;
                                        this.DebugMessage("Back buffer width=" + width + " height=" + height + " format=" + sc.PresentParameters.BackBufferFormat);


                                        _renderTarget = Surface.CreateOffscreenPlain(device, sc.PresentParameters.BackBufferWidth, sc.PresentParameters.BackBufferHeight, sc.PresentParameters.BackBufferFormat, Pool.SystemMemory);
                                    }
                                }

                                using (Surface backBuffer = device.GetBackBuffer(0, 0))
                                {
                                    // Create a super fast copy of the back buffer on our Surface
                                    device.GetRenderTargetData(backBuffer, _renderTarget);

                                    // We have the back buffer data and can now work on copying it to a bitmap
                                    ProcessRequest();
                                }
                            }
                            finally
                            {
                            }
                        }
                    }
                    #endregion
                }
                catch (Exception e)
                {
                    // If there is an error we do not want to crash the hooked application, so swallow the exception
                    this.ErrorMessage("EndSceneHook: Exeception: " + e.GetType().FullName + ": " + e.Message + " " + e);
                }

                // EasyHook has already repatched the original EndScene - so calling EndScene against the SlimDX device will call the original
                // EndScene and bypass this hook. EasyHook will automatically reinstall the hook after this hook function exits.
                return device.EndScene().Code;
            }
        }

        /// <summary>
        /// Copies the _renderTarget surface into a stream and starts a new thread to send the data back to the host process
        /// </summary>
        void ProcessRequest()
        {

            // After the Stream is created we are now finished with _renderTarget and have our own separate copy of the data,
            // therefore it will now be safe to begin a new thread to complete processing.
            // Note: RetrieveImageData will take care of closing the stream.
            // Note 2: Surface.ToStream is the slowest part of the screen capture process - the other methods
            //         available to us at this point are _renderTarget.GetDC(), and _renderTarget.LockRectangle/UnlockRectangle
            DataRectangle dr = _renderTarget.LockRectangle(LockFlags.ReadOnly);

            if (data.Length != dr.Data.Length)
            {
                DebugMessage("Data isn't the right size for the frame, resizing from " + data.Length + " to " + dr.Data.Length);
                Array.Resize<byte>(ref data, (int)dr.Data.Length);
            }
            dr.Data.Read(data, 0, data.Length);
            _renderTarget.UnlockRectangle();
            base.queueRawImage(data, width, height, format);
        }
    }
}
