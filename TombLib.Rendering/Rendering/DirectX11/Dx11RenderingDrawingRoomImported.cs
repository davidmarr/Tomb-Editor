using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using TombLib.GeometryIO;
using TombLib.LevelData;
using TombLib.Utils;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace TombLib.Rendering.DirectX11
{
    public class Dx11RenderingDrawingRoomImported : RenderingDrawingRoom
    {
        public readonly Dx11RenderingDevice Device;
        public readonly ShaderResourceView TextureView;
        public readonly RenderingTextureAllocator TextureAllocator;
        public Buffer VertexBuffer;
        public readonly VertexBufferBinding[] VertexBufferBindings;
        public readonly int VertexCount;
        public readonly int VertexBufferSize;
        public bool TexturesInvalidated = false;
        public bool TexturesInvalidatedRetried = false;

        public unsafe Dx11RenderingDrawingRoomImported(Dx11RenderingDevice device, Description description)
        {
            Device = device;
            TextureView = ((Dx11RenderingTextureAllocator)(description.TextureAllocator)).TextureView;
            TextureAllocator = description.TextureAllocator;
            Vector2 textureScaling = new Vector2(16777216.0f) / new Vector2(TextureAllocator.Size.X, TextureAllocator.Size.Y);

            IOMesh roomGeometry = description.Room.GeometryReplacement;

            // Create buffer
            Vector3 worldPos = description.Room.WorldPos + description.Offset;
            int singleSidedVertexCount = roomGeometry.Positions.Count;
            int vertexCount = VertexCount = singleSidedVertexCount;
            if (vertexCount == 0)
                return;
            VertexBufferSize = vertexCount * (sizeof(Vector3) + sizeof(uint) + sizeof(uint) + sizeof(ulong) + sizeof(uint));
            fixed (byte* data = new byte[VertexBufferSize])
            {
                Vector3* positions = (Vector3*)(data);
                uint* colors = (uint*)(data + vertexCount * sizeof(Vector3));
                uint* overlays = (uint*)(data + vertexCount * (sizeof(Vector3) + sizeof(uint)));
                ulong* uvwAndBlendModes = (ulong*)(data + vertexCount * (sizeof(Vector3) + sizeof(uint) + sizeof(uint)));
                uint* editorUVAndSectorTexture = (uint*)(data + vertexCount * (sizeof(Vector3) + sizeof(uint) + sizeof(uint) + sizeof(ulong)));

                // Setup vertices
                for (int i = 0; i < singleSidedVertexCount; ++i)
                    positions[i] = roomGeometry.Positions[i] + worldPos;
                for (int i = 0; i < singleSidedVertexCount; ++i)
                    colors[i] = Dx11RenderingDevice.CompressColor(roomGeometry.Colors[i]);
                for (int i = 0; i < singleSidedVertexCount; ++i)
                {
                    Vector2 vertexEditorUv = roomGeometry.UV[i];
                    uint editorUv = 0;
                    editorUv |= (uint)((int)vertexEditorUv.X) & 3;
                    editorUv |= ((uint)((int)vertexEditorUv.Y) & 3) << 2;
                    editorUVAndSectorTexture[i] = editorUv;
                }
                // Create GPU resources
                VertexBuffer = new Buffer(device.Device, new IntPtr(data),
                    new BufferDescription(VertexBufferSize, ResourceUsage.Immutable, BindFlags.VertexBuffer,
                    CpuAccessFlags.None, ResourceOptionFlags.None, 0));
                VertexBufferBindings = new VertexBufferBinding[] {
                    new VertexBufferBinding(VertexBuffer, sizeof(Vector3), (int)((byte*)positions - data)),
                    new VertexBufferBinding(VertexBuffer, sizeof(uint), (int)((byte*)colors - data)),
                    new VertexBufferBinding(VertexBuffer, sizeof(uint), (int)((byte*)overlays - data)),
                    new VertexBufferBinding(VertexBuffer, sizeof(ulong), (int)((byte*)uvwAndBlendModes - data)),
                    new VertexBufferBinding(VertexBuffer, sizeof(uint), (int)((byte*)editorUVAndSectorTexture - data))
                };
                VertexBuffer.SetDebugName("Room " + (description.Room.Name ?? ""));
            }
            TextureAllocator.GarbageCollectionCollectEvent.Add(GarbageCollectTexture);
        }

        public override void Dispose()
        {
            TextureAllocator.GarbageCollectionCollectEvent.Remove(GarbageCollectTexture);
            if (VertexBuffer != null)
                VertexBuffer.Dispose();
        }

        public unsafe RenderingTextureAllocator.GarbageCollectionAdjustDelegate GarbageCollectTexture(RenderingTextureAllocator allocator,
            RenderingTextureAllocator.Map map, HashSet<RenderingTextureAllocator.Map.Entry> inOutUsedTextures)
        {
            TexturesInvalidated = true;
            if (VertexBuffer == null)
                return null;

            byte[] data = Device.ReadBuffer(VertexBuffer, VertexBufferSize);
            Vector2 textureScaling = new Vector2(16777216.0f) / new Vector2(TextureAllocator.Size.X, TextureAllocator.Size.Y);
            int uvwAndBlendModesOffset = VertexBufferBindings[3].Offset;

            // Collect all used textures
            fixed (byte* dataPtr = data)
            {
                ulong* uvwAndBlendModesPtr = (ulong*)(dataPtr + uvwAndBlendModesOffset);
                for (int i = 0; i < VertexCount; ++i)
                {
                    if (uvwAndBlendModesPtr[i] < 0x1000000) // Very small coordinates make no sense, they are used as a placeholder
                        continue;
                    var texture = map.Lookup(Dx11RenderingDevice.UncompressUvw(uvwAndBlendModesPtr[i], textureScaling));
                    if (texture == null)
#if DEBUG
                        throw new ArgumentOutOfRangeException("Texture unrecognized.");
#else
                        continue;
#endif
                    inOutUsedTextures.Add(texture);
                }
            }

            // Provide a methode to update the buffer with new UV coordinates
            return delegate (RenderingTextureAllocator allocator2, RenderingTextureAllocator.Map map2)
            {
                Vector2 textureScaling2 = new Vector2(16777216.0f) / new Vector2(TextureAllocator.Size.X, TextureAllocator.Size.Y);

                // Update data
                fixed (byte* dataPtr = data)
                {
                    ulong* uvwAndBlendModesPtr = (ulong*)(dataPtr + uvwAndBlendModesOffset);
                    for (int i = 0; i < VertexCount; ++i)
                    {
                        if (uvwAndBlendModesPtr[i] < 0x1000000) // Very small coordinates make no sense, they are used as a placeholder
                            continue;
                        var texture = map.Lookup(Dx11RenderingDevice.UncompressUvw(uvwAndBlendModesPtr[i], textureScaling));
                        Vector2 uv;
                        uint highestBits;
                        Dx11RenderingDevice.UncompressUvw(uvwAndBlendModesPtr[i], texture.Pos, textureScaling, out uv, out highestBits);
                        uvwAndBlendModesPtr[i] = Dx11RenderingDevice.CompressUvw(allocator2.Get(texture.Texture), textureScaling2, uv, highestBits);
                    }
                }

                // Upload data
                var oldVertexBuffer = VertexBuffer;
                fixed (byte* dataPtr = data)
                {
                    VertexBuffer = new Buffer(Device.Device, new IntPtr(dataPtr),
                        new BufferDescription(VertexBufferSize, ResourceUsage.Immutable, BindFlags.VertexBuffer,
                        CpuAccessFlags.None, ResourceOptionFlags.None, 0));
                    oldVertexBuffer.Dispose();
                }
                for (int i = 0; i < VertexBufferBindings.Length; ++i)
                    if (VertexBufferBindings[i].Buffer == oldVertexBuffer)
                        VertexBufferBindings[i].Buffer = VertexBuffer;
            };
        }

        public override void Render(RenderArgs arg)
        {
            if (VertexCount == 0)
                return;
            var context = Device.Context;

            // Setup state
            ((Dx11RenderingSwapChain)arg.RenderTarget).Bind();
            Device.RoomShader.Apply(context, arg.StateBuffer);
            context.PixelShader.SetSampler(0, Device.SamplerDefault);
            context.PixelShader.SetShaderResources(0, TextureView, Device.SectorTextureArrayView);
            context.InputAssembler.SetVertexBuffers(0, VertexBufferBindings);

            // Render
            context.Draw(VertexCount, 0);
        }
    }
}
