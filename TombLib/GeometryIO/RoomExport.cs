using NLog;
using System;
using System.Collections.Generic;
using System.IO;
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
    static public class RoomExport
    {
        const int PAGESIZE = 256;
        private static Vector2 GetNormalizedPageUVs(Vector2 uv, int textureWidth, int textureHeight,int page)
        {
            int numXPages = getNumXPages(textureWidth);
            int numYPages = getNumYPages(textureHeight);
            int yPage = page / numXPages;
            int xPage = page % numXPages;
            int uOffset = xPage * PAGESIZE;
            int vOffset = yPage * PAGESIZE;
            return new Vector2((uv.X-uOffset) / PAGESIZE, (uv.Y-vOffset) / PAGESIZE);
        }

        private static int getNumXPages(int width)
        {
            return (int)Math.Ceiling((float)width / PAGESIZE);
        }

        private static int getNumYPages(int height)
        {
            return (int)Math.Ceiling((float)height / PAGESIZE);
        }

        public static RoomExportResult ExportRooms(IEnumerable<Room> roomsToExport,string filePath, Level level)
        {
            RoomExportResult result = new RoomExportResult();
             try
             {
                //Prepare data for export
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
                    var tex = usedTextures[j];
                    int numXPages = getNumXPages(tex.Image.Width);
                    int numYPages = getNumXPages(tex.Image.Height);
                    string baseName = Path.GetFileNameWithoutExtension(tex.Image.FileName);
                    for (int y = 0; y < numYPages; y++)
                    {
                        for (int x  = 0; x < numXPages; x++)
                        {
                            int page = y * numXPages + x;
                            string textureFileName = baseName + "_" + x +"_"+ y + ".png";
                            int startX = x * PAGESIZE;
                            int width = (x * PAGESIZE + PAGESIZE > tex.Image.Width ? tex.Image.Width - x * PAGESIZE : PAGESIZE);
                            int startY = y * PAGESIZE;
                            int height = (y * PAGESIZE + PAGESIZE > tex.Image.Height ? tex.Image.Height - y * PAGESIZE : PAGESIZE);
                            ImageC newImage = ImageC.CreateNew(PAGESIZE, PAGESIZE);
                            newImage.CopyFrom(0, 0, tex.Image, startX, startY, width, height);
                            string absoluteTexturefilePath = Path.Combine(Path.GetDirectoryName(filePath), textureFileName);
                            newImage.Save(absoluteTexturefilePath);
                            var matOpaque = new IOMaterial(Material.Material_Opaque + "_" + j,tex, absoluteTexturefilePath, false,false,0,page);
                            var matOpaqueDoubleSided = new IOMaterial(Material.Material_OpaqueDoubleSided + "_" + page, tex, absoluteTexturefilePath, false,true,0,page);
                            var matAdditiveBlending = new IOMaterial(Material.Material_AdditiveBlending + "_" + page,tex, absoluteTexturefilePath, true,false,0,page);
                            var matAdditiveBlendingDoubleSided = new IOMaterial(Material.Material_AdditiveBlendingDoubleSided + "_" + page,tex, absoluteTexturefilePath, true,true,0,page);
                            model.Materials.Add(matOpaque);
                            model.Materials.Add(matOpaqueDoubleSided);
                            model.Materials.Add(matAdditiveBlending);
                            model.Materials.Add(matAdditiveBlendingDoubleSided);
                        }
                    }
                    
                    // Build materials for this texture pahe
                    
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
                                    int textureAreaPage = GetTextureAreaPage(textureArea1, textureArea2);
                                    if(textureAreaPage < 0)
                                    {
                                        result.Warnings.Add(string.Format("Quad at ({0},{1}) in Room {2} has a texture that is beyond the 256px boundary. TexturePage is set to 0",x,z,room));
                                        textureAreaPage = 1;
                                    }
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
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea2.TexCoord0, textureWidth, textureHeight, textureAreaPage));
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea1.TexCoord2, textureWidth, textureHeight, textureAreaPage));
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea1.TexCoord0, textureWidth, textureHeight, textureAreaPage));
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea1.TexCoord1, textureWidth, textureHeight, textureAreaPage));
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
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea1.TexCoord1, textureWidth, textureHeight, textureAreaPage));
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea1.TexCoord2, textureWidth, textureHeight, textureAreaPage));
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea1.TexCoord0, textureWidth, textureHeight, textureAreaPage));
                                        mesh.UV.Add(GetNormalizedPageUVs(textureArea2.TexCoord2, textureWidth, textureHeight, textureAreaPage));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 1], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 2], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 0], 1.0f));
                                        mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 5], 1.0f));
                                    }
                                    var mat = model.GetMaterial(textureArea1.Texture,
                                                                textureArea1.BlendMode == BlendMode.Additive,
                                                                textureAreaPage,
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
                                    int textureAreaPage = GetTextureAreaPage(textureArea,null);
                                    if (textureAreaPage < 0)
                                    {
                                        result.Warnings.Add(string.Format("Triangle at ({0},{1}) in Room {2} has a texture that is beyond the 256px boundary. TexturePage is set to 0", x, z, room));
                                        textureAreaPage = 1;
                                    }
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
                                    mesh.UV.Add(GetNormalizedPageUVs(textureArea.TexCoord0, textureWidth, textureHeight,textureAreaPage));
                                    mesh.UV.Add(GetNormalizedPageUVs(textureArea.TexCoord1, textureWidth, textureHeight,textureAreaPage));
                                    mesh.UV.Add(GetNormalizedPageUVs(textureArea.TexCoord2, textureWidth, textureHeight,textureAreaPage));

                                    mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i], 1.0f));
                                    mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 1], 1.0f));
                                    mesh.Colors.Add(new Vector4(room.RoomGeometry.VertexColors[i + 2], 1.0f));

                                    var mat = model.GetMaterial(textureArea.Texture,
                                                                textureArea.BlendMode == BlendMode.Additive,
                                                                textureAreaPage,
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

        private static int GetTextureAreaPage(TextureArea textureArea1, TextureArea? textureArea2)
        {
            int width = textureArea1.Texture.Image.Width;
            int height = textureArea1.Texture.Image.Height;
            int numXPages = (int)Math.Ceiling((float)width / PAGESIZE);
            int numYPages = (int)Math.Ceiling((float)height / PAGESIZE);
            Rectangle2 textureRect = textureArea2 != null ? textureArea1.GetRect().Union(textureArea2.Value.GetRect()) : textureArea1.GetRect();
            for (int yPage = 0; yPage < numYPages; yPage++)
                for (int xPage = 0; xPage < numXPages; xPage++)
                {
                    Rectangle2 pageRect = new RectangleInt2(xPage * PAGESIZE, yPage * PAGESIZE, (xPage + 1) * PAGESIZE, (yPage + 1) * PAGESIZE);
                    if(pageRect.Contains(textureRect))
                    {
                        return yPage * numXPages + xPage;
                    }
                }
            return -1;
        }
    }
}
