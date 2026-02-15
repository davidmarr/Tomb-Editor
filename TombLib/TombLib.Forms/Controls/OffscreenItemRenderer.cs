using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Toolkit.Graphics;
using System;
using System.Collections.Generic;
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
    /// Uses the same rendering pipeline as PanelItemPreview but without a window/swap chain.
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

        private static readonly Vector4 ClearColor = new Vector4(0.235f, 0.246f, 0.255f, 1.0f); // Dark background

        public OffscreenItemRenderer()
        {
            _device = (Dx11RenderingDevice)DeviceManager.DefaultDeviceManager.Device;
            _legacyDevice = DeviceManager.DefaultDeviceManager.___LegacyDevice;
            _wadRenderer = new WadRenderer(_legacyDevice, true, true, 1024, 512, false);
        }

        /// <summary>
        /// Renders an IWadObject to a thumbnail image of the specified size (square).
        /// </summary>
        /// <param name="wadObject">The object to render.</param>
        /// <param name="size">Thumbnail width and height in pixels.</param>
        /// <param name="fieldOfView">Camera field of view in degrees.</param>
        /// <returns>The rendered image, or null if rendering fails.</returns>
        public ImageC RenderThumbnail(IWadObject wadObject, int size = 128, float fieldOfView = 50.0f)
        {
            if (wadObject == null)
                return ImageC.CreateNew(size, size);

            try
            {
                EnsureRenderTarget(size);

                // Set up camera
                var camera = CreateCameraForObject(wadObject, fieldOfView);
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

                // Render the object
                RenderObject(wadObject, viewProjection, camera);

                // Read back pixels
                return ReadBackPixels(size);
            }
            catch
            {
                return ImageC.CreateNew(size, size);
            }
        }

        private ArcBallCamera CreateCameraForObject(IWadObject wadObject, float fieldOfView)
        {
            var bs = new BoundingSphere(new Vector3(0.0f, 256.0f, 0.0f), 640.0f);

            if (wadObject is WadMoveable moveable)
            {
                if (moveable.Meshes.Count == 0 || (moveable.Meshes.Count == 1 && moveable.Meshes[0] == null))
                    return null;

                AnimatedModel model = _wadRenderer.GetMoveable(moveable);
                if (model.Animations.Count > 0 && model.Animations[0].KeyFrames.Count > 0)
                {
                    model.UpdateAnimation(0, 0);
                    var bb = model.Animations[0].KeyFrames[0].CalculateBoundingBox(model, model);
                    bs = BoundingSphere.FromBoundingBox(bb);
                }
            }
            else if (wadObject is WadStatic staticObj)
            {
                if (staticObj.Mesh != null)
                    bs = staticObj.Mesh.CalculateBoundingSphere();
            }
            else if (wadObject is ImportedGeometry impGeo)
            {
                if (impGeo.DirectXModel != null && impGeo.DirectXModel.Meshes != null)
                {
                    var bb = new BoundingBox();
                    foreach (var mesh in impGeo.DirectXModel.Meshes)
                        bb = bb.Union(mesh.BoundingBox);
                    bs = BoundingSphere.FromBoundingBox(bb);
                }
            }
            else
            {
                return null;
            }

            var center = bs.Center;
            var radius = bs.Radius * 1.15f;

            return new ArcBallCamera(center,
                MathC.DegToRad(35), MathC.DegToRad(35),
                -(float)Math.PI / 2, (float)Math.PI / 2,
                radius * 3, 50, 1000000,
                fieldOfView * (float)(Math.PI / 180));
        }

        private void RenderObject(IWadObject wadObject, Matrix4x4 viewProjection, ArcBallCamera camera)
        {
            if (wadObject is WadMoveable moveable)
                RenderMoveable(moveable, viewProjection, camera);
            else if (wadObject is WadStatic staticObj)
                RenderStatic(staticObj, viewProjection, camera);
            else if (wadObject is ImportedGeometry impGeo)
                RenderImportedGeometry(impGeo, viewProjection, camera);
        }

        private void RenderMoveable(WadMoveable moveable, Matrix4x4 viewProjection, ArcBallCamera camera)
        {
            if (moveable.Meshes.Count == 0 || (moveable.Meshes.Count == 1 && moveable.Meshes[0] == null))
                return;

            AnimatedModel model = _wadRenderer.GetMoveable(moveable);
            model.UpdateAnimation(0, 0);

            var effect = DeviceManager.DefaultDeviceManager.___LegacyEffects["Model"];

            effect.Parameters["AlphaTest"].SetValue(false);
            effect.Parameters["Color"].SetValue(Vector4.One);
            effect.Parameters["StaticLighting"].SetValue(false);
            effect.Parameters["ColoredVertices"].SetValue(false);
            effect.Parameters["Texture"].SetResource(_wadRenderer.Texture);
            effect.Parameters["TextureSampler"].SetResource(_legacyDevice.SamplerStates.Default);

            var matrices = new List<Matrix4x4>();
            if (model.Animations.Count != 0)
            {
                for (var b = 0; b < model.Meshes.Count; b++)
                    matrices.Add(model.AnimationTransforms[b]);
            }
            else
            {
                foreach (var bone in model.Bones)
                    matrices.Add(bone.GlobalTransform);
            }

            if (model.Skin != null)
                model.RenderSkin(_legacyDevice, effect, viewProjection.ToSharpDX());

            for (int i = 0; i < model.Meshes.Count; i++)
            {
                var mesh = model.Meshes[i];
                if (mesh.Vertices.Count == 0)
                    continue;

                if (model.Skin != null && mesh.Hidden)
                    continue;

                mesh.UpdateBuffers(camera.GetPosition());

                _legacyDevice.SetVertexBuffer(0, mesh.VertexBuffer);
                _legacyDevice.SetIndexBuffer(mesh.IndexBuffer, true);
                _legacyDevice.SetVertexInputLayout(mesh.InputLayout);

                effect.Parameters["ModelViewProjection"].SetValue((matrices[i] * viewProjection).ToSharpDX());
                effect.Techniques[0].Passes[0].Apply();

                foreach (var submesh in mesh.Submeshes)
                {
                    submesh.Value.Material.SetStates(_legacyDevice, false);
                    _legacyDevice.Draw(SharpDX.Toolkit.Graphics.PrimitiveType.TriangleList, submesh.Value.NumIndices, submesh.Value.BaseIndex);
                }
            }
        }

        private void RenderStatic(WadStatic staticObj, Matrix4x4 viewProjection, ArcBallCamera camera)
        {
            StaticModel model = _wadRenderer.GetStatic(staticObj);

            var effect = DeviceManager.DefaultDeviceManager.___LegacyEffects["Model"];

            effect.Parameters["ModelViewProjection"].SetValue(viewProjection.ToSharpDX());
            effect.Parameters["AlphaTest"].SetValue(false);
            effect.Parameters["Color"].SetValue(Vector4.One);
            effect.Parameters["StaticLighting"].SetValue(false);
            effect.Parameters["ColoredVertices"].SetValue(false);
            effect.Parameters["Texture"].SetResource(_wadRenderer.Texture);
            effect.Parameters["TextureSampler"].SetResource(_legacyDevice.SamplerStates.Default);

            for (int i = 0; i < model.Meshes.Count; i++)
            {
                var mesh = model.Meshes[i];
                if (mesh.Vertices.Count == 0)
                    continue;

                mesh.UpdateBuffers(camera.GetPosition());

                _legacyDevice.SetVertexBuffer(0, mesh.VertexBuffer);
                _legacyDevice.SetIndexBuffer(mesh.IndexBuffer, true);
                _legacyDevice.SetVertexInputLayout(mesh.InputLayout);

                effect.Parameters["ModelViewProjection"].SetValue(viewProjection.ToSharpDX());
                effect.Techniques[0].Passes[0].Apply();

                foreach (var submesh in mesh.Submeshes)
                {
                    submesh.Value.Material.SetStates(_legacyDevice, false);
                    _legacyDevice.DrawIndexed(SharpDX.Toolkit.Graphics.PrimitiveType.TriangleList, submesh.Value.NumIndices, submesh.Value.BaseIndex);
                }
            }
        }

        private void RenderImportedGeometry(ImportedGeometry geo, Matrix4x4 viewProjection, ArcBallCamera camera)
        {
            var model = geo.DirectXModel;
            if (model == null || model.Meshes == null || model.Meshes.Count == 0)
                return;

            var effect = DeviceManager.DefaultDeviceManager.___LegacyEffects["RoomGeometry"];

            effect.Parameters["UseVertexColors"].SetValue(true);
            effect.Parameters["AlphaTest"].SetValue(false);
            effect.Parameters["Color"].SetValue(Vector4.One);
            effect.Parameters["TextureSampler"].SetResource(_legacyDevice.SamplerStates.AnisotropicWrap);

            for (int i = 0; i < model.Meshes.Count; i++)
            {
                var mesh = model.Meshes[i];
                if (mesh.Vertices.Count == 0)
                    continue;

                mesh.UpdateBuffers(camera.GetPosition());

                _legacyDevice.SetVertexBuffer(0, mesh.VertexBuffer);
                _legacyDevice.SetIndexBuffer(mesh.IndexBuffer, true);
                _legacyDevice.SetVertexInputLayout(mesh.InputLayout);

                effect.Parameters["ModelViewProjection"].SetValue(viewProjection.ToSharpDX());

                foreach (var submesh in mesh.Submeshes)
                {
                    var texture = submesh.Value.Material.Texture;
                    if (texture != null && texture is ImportedGeometryTexture)
                    {
                        effect.Parameters["TextureEnabled"].SetValue(true);
                        effect.Parameters["Texture"].SetResource(((ImportedGeometryTexture)texture).DirectXTexture);
                        effect.Parameters["ReciprocalTextureSize"].SetValue(new Vector2(1.0f / texture.Image.Width, 1.0f / texture.Image.Height));
                    }
                    else
                        effect.Parameters["TextureEnabled"].SetValue(false);

                    effect.Techniques[0].Passes[0].Apply();
                    submesh.Value.Material.SetStates(_legacyDevice, false);
                    _legacyDevice.DrawIndexed(SharpDX.Toolkit.Graphics.PrimitiveType.TriangleList, submesh.Value.NumIndices, submesh.Value.BaseIndex);
                }
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
