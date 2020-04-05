using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using TombLib.Graphics;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombLib.GeometryIO
{
    sealed public class RoomExportResult
    {
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public IOModel Model { get; set; }
    }
    sealed public class RoomExport
    {
        private static Vector2 GetNormalizedUV(Vector2 uv, int textureWidth, int textureHeight)
        {
            return new Vector2(uv.X / textureWidth, uv.Y / textureHeight);
        }
        public static RoomExportResult ExportRooms(IEnumerable<Room> roomsToExport, Level level)
        {
            RoomExportResult result = new RoomExportResult();
            try
            {
                // Prepare data for export
                var model = new IOModel();

                var usedTextures = new List<Texture>();
                foreach (var room in roomsToExport)
                {
                    for (int x = 0; x < room.NumXSectors; x++)
                    {
                        for (int z = 0; z < room.NumZSectors; z++)
                        {
                            var block = room.GetBlock(new VectorInt2(x, z));

                            for (int faceType = 0; faceType < (int)BlockFace.Count; faceType++)
                            {
                                var faceTexture = block.GetFaceTexture((BlockFace)faceType);
                                if (faceTexture.TextureIsInvisible || faceTexture.TextureIsUnavailable || faceTexture.Texture == null)
                                    continue;
                                if (!usedTextures.Contains(faceTexture.Texture))
                                    usedTextures.Add(faceTexture.Texture);
                            }
                        }
                    }
                }

                for (int j = 0; j < usedTextures.Count; j++)
                {
                    if (usedTextures[j].Image.Width > 8192)
                    {
                        result.Warnings.Add("The Texture " + usedTextures[j].Image.FileName + "Has a width higher than 8192. Possible UV Coordinate precision loss!");
                    }
                    if (usedTextures[j].Image.Height > 8192)
                    {
                        result.Warnings.Add("The Texture " + usedTextures[j].Image.FileName + "Has a height higher than 8192. Possible UV Coordinate precision loss!");
                    }
                    // Build materials for this texture pahe
                    var matOpaque = new IOMaterial(Material.Material_Opaque + "_" + j,
                                                   usedTextures[j],
                                                   usedTextures[j].Image.FileName,
                                                   false,
                                                   false,
                                                   0);

                    var matOpaqueDoubleSided = new IOMaterial(Material.Material_OpaqueDoubleSided + "_" + j,
                                                              usedTextures[j],
                                                              usedTextures[j].Image.FileName,
                                                              false,
                                                              true,
                                                              0);

                    var matAdditiveBlending = new IOMaterial(Material.Material_AdditiveBlending + "_" + j,
                                                             usedTextures[j],
                                                             usedTextures[j].Image.FileName,
                                                             true,
                                                             false,
                                                             0);

                    var matAdditiveBlendingDoubleSided = new IOMaterial(Material.Material_AdditiveBlendingDoubleSided + "_" + j,
                                                                        usedTextures[j],
                                                                        usedTextures[j].Image.FileName,
                                                                        true,
                                                                        true,
                                                                        0);

                    model.Materials.Add(matOpaque);
                    model.Materials.Add(matOpaqueDoubleSided);
                    model.Materials.Add(matAdditiveBlending);
                    model.Materials.Add(matAdditiveBlendingDoubleSided);
                }

                foreach (var room in roomsToExport)
                {
                    int index = level.Rooms.ReferenceIndexOf(room);
                    int xOff = room.Position.X;
                    int yOff = room.Position.Y;
                    int zOff = room.Position.Z;
                    //Append the Offset to the Mesh name, we can later calculate the correct position
                    string meshFormat = "TeRoom_{0}_{1}_{2}_{3}";
                    var mesh = new IOMesh(string.Format(meshFormat, index, xOff, yOff, zOff));
                    mesh.Position = room.WorldPos;

                    // Add submeshes
                    foreach (var material in model.Materials)
                        mesh.Submeshes.Add(material, new IOSubmesh(material));

                    if (room.RoomGeometry == null)
                        continue;

                    int lastIndex = 0;
                    for (int x = 0; x < room.NumXSectors; x++)
                    {
                        for (int z = 0; z < room.NumZSectors; z++)
                        {
                            var block = room.GetBlock(new VectorInt2(x, z));

                            for (int faceType = 0; faceType < (int)BlockFace.Count; faceType++)
                            {
                                var faceTexture = block.GetFaceTexture((BlockFace)faceType);

                                if (faceTexture.TextureIsInvisible || faceTexture.TextureIsUnavailable || faceTexture.Texture == null)
                                    continue;
                                var range = room.RoomGeometry.VertexRangeLookup.TryGetOrDefault(new SectorInfo(x, z, (BlockFace)faceType));
                                var shape = room.GetFaceShape(x, z, (BlockFace)faceType);

                                if (shape == BlockFaceShape.Quad)
                                {
                                    int i = range.Start;

                                    var textureArea1 = room.RoomGeometry.TriangleTextureAreas[i / 3];
                                    var textureArea2 = room.RoomGeometry.TriangleTextureAreas[(i + 3) / 3];

                                    if (textureArea1.TextureIsUnavailable || textureArea1.TextureIsInvisible || textureArea1.Texture == null)
                                        continue;
                                    if (textureArea2.TextureIsUnavailable || textureArea2.TextureIsInvisible || textureArea2.Texture == null)
                                        continue;

                                    var poly = new IOPolygon(IOPolygonShape.Quad);
                                    poly.Indices.Add(lastIndex + 0);
                                    poly.Indices.Add(lastIndex + 1);
                                    poly.Indices.Add(lastIndex + 2);
                                    poly.Indices.Add(lastIndex + 3);
                                    var uvFactor = new Vector2(0.5f / (float)textureArea1.Texture.Image.Width, 0.5f / (float)textureArea1.Texture.Image.Height);
                                    int textureWidth = textureArea1.Texture.Image.Width;
                                    int textureHeight = textureArea1.Texture.Image.Height;

                                    if (faceType != (int)BlockFace.Ceiling)
                                    {
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 3] + room.WorldPos);
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 2] + room.WorldPos);
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 0] + room.WorldPos);
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 1] + room.WorldPos);
                                        mesh.UV.Add(GetNormalizedUV(textureArea2.TexCoord0, textureWidth, textureHeight));
                                        mesh.UV.Add(GetNormalizedUV(textureArea1.TexCoord2, textureWidth, textureHeight));
                                        mesh.UV.Add(GetNormalizedUV(textureArea1.TexCoord0, textureWidth, textureHeight));
                                        mesh.UV.Add(GetNormalizedUV(textureArea1.TexCoord1, textureWidth, textureHeight));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 3], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 2], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 0], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 1], 1.0f));
                                    }
                                    else
                                    {
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 1] + room.WorldPos);
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 2] + room.WorldPos);
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 0] + room.WorldPos);
                                        mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 5] + room.WorldPos);
                                        mesh.UV.Add(GetNormalizedUV(textureArea1.TexCoord1, textureWidth, textureHeight));
                                        mesh.UV.Add(GetNormalizedUV(textureArea1.TexCoord2, textureWidth, textureHeight));
                                        mesh.UV.Add(GetNormalizedUV(textureArea1.TexCoord0, textureWidth, textureHeight));
                                        mesh.UV.Add(GetNormalizedUV(textureArea2.TexCoord2, textureWidth, textureHeight));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 1], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 2], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 0], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 5], 1.0f));
                                    }
                                    var mat = model.GetMaterial(textureArea1.Texture,
                                                                textureArea1.BlendMode == BlendMode.Additive,
                                                                textureArea1.DoubleSided,
                                                                0);

                                    var submesh = mesh.Submeshes[mat];
                                    submesh.Polygons.Add(poly);

                                    lastIndex += 4;
                                }
                                else
                                {
                                    int i = range.Start;

                                    var textureArea = room.RoomGeometry.TriangleTextureAreas[i / 3];
                                    if (textureArea.TextureIsUnavailable || textureArea.TextureIsInvisible || textureArea.Texture == null)
                                        continue;

                                    var poly = new IOPolygon(IOPolygonShape.Triangle);
                                    poly.Indices.Add(lastIndex);
                                    poly.Indices.Add(lastIndex + 1);
                                    poly.Indices.Add(lastIndex + 2);

                                    mesh.Positions.Add(room.RoomGeometry.VertexPositions[i] + room.WorldPos);
                                    mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 1] + room.WorldPos);
                                    mesh.Positions.Add(room.RoomGeometry.VertexPositions[i + 2] + room.WorldPos);

                                    var uvFactor = new Vector2(0.5f / (float)textureArea.Texture.Image.Width, 0.5f / (float)textureArea.Texture.Image.Height);
                                    int textureWidth = textureArea.Texture.Image.Width;
                                    int textureHeight = textureArea.Texture.Image.Height;
                                    mesh.UV.Add(GetNormalizedUV(textureArea.TexCoord0, textureWidth, textureHeight));
                                    mesh.UV.Add(GetNormalizedUV(textureArea.TexCoord1, textureWidth, textureHeight));
                                    mesh.UV.Add(GetNormalizedUV(textureArea.TexCoord2, textureWidth, textureHeight));

                                    mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i], 1.0f));
                                    mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 1], 1.0f));
                                    mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 2], 1.0f));

                                    var mat = model.GetMaterial(textureArea.Texture,
                                                                textureArea.BlendMode == BlendMode.Additive,
                                                                textureArea.DoubleSided,
                                                                0);
                                    var submesh = mesh.Submeshes[mat];
                                    submesh.Polygons.Add(poly);
                                    lastIndex += 3;
                                }
                            }
                        }
                    }
                    model.Meshes.Add(mesh);
                }
                result.Model = model;
            }
            catch (Exception e)
            {
                result.Errors.Add(e.Message);
            }
            return result;
        }
    }
}
