using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Toolkit.Graphics;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using TombLib.Graphics;
using TombLib.LevelData;
using TombLib.Rendering.DirectX11;
using TombLib.Utils;
using TombLib.Wad;
using Buffer = SharpDX.Direct3D11.Buffer;
using Texture2D = SharpDX.Direct3D11.Texture2D;

namespace TombLib.Controls
{
    /// <summary>
    /// Renders IWadObject items to offscreen render targets and returns pixel data as ImageC.
    /// Uses WadObjectRenderHelper for shared rendering logic with PanelItemPreview.
    /// Thread-safety: NOT thread-safe. Must be called from the UI thread (same as all D3D11 rendering).
    /// </summary>
    public class OffscreenItemRenderer : IDisposable
    {
        private readonly Dx11RenderingDevice _device;
        private readonly GraphicsDevice _legacyDevice;
        private readonly WadRenderer _wadRenderer;

        private Texture2D _renderTarget;
        private RenderTargetView _renderTargetView;
        private Texture2D _depthBuffer;
        private DepthStencilView _depthBufferView;
        private Texture2D _stagingTexture;
        private int _currentSize;

        /// <summary>
        /// Background clear color for rendered thumbnails.
        /// Set this from the editor's UI_ColorScheme.Color3DBackground for consistency.
        /// </summary>
        public Vector4 ClearColor { get; set; } = new Vector4(0.235f, 0.246f, 0.255f, 1.0f);

        public OffscreenItemRenderer()
        {
            _device = (Dx11RenderingDevice)DeviceManager.DefaultDeviceManager.Device;
            _legacyDevice = DeviceManager.DefaultDeviceManager.___LegacyDevice;
            _wadRenderer = new WadRenderer(_legacyDevice, true, true, 1024, 512, false);
        }

        /// <summary>
        /// Renders an IWadObject to a thumbnail image of the specified size (square).
        /// </summary>
        public ImageC RenderThumbnail(IWadObject wadObject, int size = 128, float fieldOfView = 50.0f)
        {
            if (wadObject == null)
                return ImageC.CreateNew(size, size);

            try
            {
                EnsureRenderTarget(size);

                // Set up camera using shared helper
                var camera = WadObjectRenderHelper.CreateCameraForObject(wadObject, _wadRenderer, fieldOfView);
                if (camera == null)
                    return ImageC.CreateNew(size, size);

                // Bind our offscreen render target
                BindRenderTarget(size);

                // Clear
                _device.Context.ClearRenderTargetView(_renderTargetView,
                    new SharpDX.Color4(ClearColor.X, ClearColor.Y, ClearColor.Z, ClearColor.W));
                _device.Context.ClearDepthStencilView(_depthBufferView,
                    DepthStencilClearFlags.Depth, 1.0f, 0);

                // Reset device state
                _device.ResetState();

                // Get view-projection matrix
                Matrix4x4 viewProjection = camera.GetViewProjectionMatrix(size, size);

                // Render the object using shared helper
                WadObjectRenderHelper.RenderObject(wadObject, _wadRenderer, _legacyDevice,
                    viewProjection, camera.GetPosition(), false);

                // Read back pixels
                return ReadBackPixels(size);
            }
            catch
            {
                return ImageC.CreateNew(size, size);
            }
        }

        private void EnsureRenderTarget(int size)
        {
            if (_renderTarget != null && _currentSize == size)
                return;

            DisposeRenderTargets();
            _currentSize = size;

            // Create color render target
            _renderTarget = new Texture2D(_device.Device, new Texture2DDescription
            {
                Format = Format.R8G8B8A8_UNorm,
                Width = size,
                Height = size,
                ArraySize = 1,
                MipLevels = 1,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            });
            _renderTargetView = new RenderTargetView(_device.Device, _renderTarget);

            // Create depth buffer
            _depthBuffer = new Texture2D(_device.Device, new Texture2DDescription
            {
                Format = Format.D32_Float,
                Width = size,
                Height = size,
                ArraySize = 1,
                MipLevels = 1,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            });
            _depthBufferView = new DepthStencilView(_device.Device, _depthBuffer);

            // Create staging texture for CPU readback
            _stagingTexture = new Texture2D(_device.Device, new Texture2DDescription
            {
                Format = Format.R8G8B8A8_UNorm,
                Width = size,
                Height = size,
                ArraySize = 1,
                MipLevels = 1,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None
            });
        }

        private void BindRenderTarget(int size)
        {
            _device.Context.Rasterizer.SetViewport(0, 0, size, size, 0.0f, 1.0f);
            _device.Context.OutputMerger.SetTargets(_depthBufferView, _renderTargetView);
            // Mark device so existing swap chains re-bind properly next time
            _device.CurrentRenderTarget = null;
        }

        private ImageC ReadBackPixels(int size)
        {
            // Copy render target to staging texture
            _device.Context.CopyResource(_renderTarget, _stagingTexture);

            // Map and read pixels
            var dataBox = _device.Context.MapSubresource(_stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            try
            {
                int bytesPerPixel = 4;
                int rowPitch = dataBox.RowPitch;
                byte[] pixels = new byte[size * size * bytesPerPixel];

                // Copy row by row (rowPitch may differ from size * bytesPerPixel due to alignment)
                for (int y = 0; y < size; y++)
                {
                    Marshal.Copy(dataBox.DataPointer + y * rowPitch, pixels, y * size * bytesPerPixel, size * bytesPerPixel);
                }

                // Convert RGBA to BGRA (ImageC uses BGRA format)
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte r = pixels[i];
                    byte b = pixels[i + 2];
                    pixels[i] = b;
                    pixels[i + 2] = r;
                }

                return ImageC.FromByteArray(pixels, size, size);
            }
            finally
            {
                _device.Context.UnmapSubresource(_stagingTexture, 0);
            }
        }

        private void DisposeRenderTargets()
        {
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _depthBufferView?.Dispose();
            _depthBufferView = null;
            _depthBuffer?.Dispose();
            _depthBuffer = null;
            _renderTargetView?.Dispose();
            _renderTargetView = null;
            _renderTarget?.Dispose();
            _renderTarget = null;
        }

        public void GarbageCollect()
        {
            _wadRenderer?.GarbageCollect();
        }

        public void Dispose()
        {
            DisposeRenderTargets();
            _wadRenderer?.Dispose();
        }
    }
}
