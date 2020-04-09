using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
namespace TombLib.LevelData
{
    partial class Room
    {

        private bool RayTraceCheckFloorCeiling(int x, int y, int z, int xLight, int zLight)
        {
            int currentX = x / 1024 - (x > xLight ? 1 : 0);
            int currentZ = z / 1024 - (z > zLight ? 1 : 0);
            if (currentX > this.NumXSectors)
                currentX = this.NumXSectors -1;
            if (currentZ > this.NumZSectors)
                currentZ = this.NumZSectors - 1;
            Block block = Blocks[currentX, currentZ];
            int floorMin = block.Floor.Min;
            int ceilingMax = block.Ceiling.Max;

            return floorMin <= y / 256 && ceilingMax >= y / 256;
        }

        private bool RayTraceX(int x, int y, int z, int xLight, int yLight, int zLight)
        {
            int deltaX;
            int deltaY;
            int deltaZ;

            int minX;
            int maxX;

            yLight = -yLight;
            y = -y;

            int yPoint = y;
            int zPoint = z;

            if (x <= xLight)
            {
                deltaX = xLight - x;
                deltaY = yLight - y;
                deltaZ = zLight - z;

                minX = x;
                maxX = xLight;
            }
            else
            {
                deltaX = x - xLight;
                deltaY = y - yLight;
                deltaZ = z - zLight;

                minX = xLight;
                maxX = x;

                yPoint = yLight;
                zPoint = zLight;
            }

            if (deltaX == 0)
                return true;

            int fracX = (((minX >> 10) + 1) << 10) - minX;
            int currentX = ((minX >> 10) + 1) << 10;
            int currentZ = deltaZ * fracX / (deltaX + 1) + zPoint;
            int currentY = deltaY * fracX / (deltaX + 1) + yPoint;

            if (currentX > maxX)
                return true;

            do
            {
                int currentXblock = currentX / 1024;
                int currentZblock = currentZ / 1024;

                if (currentZblock < 0 || currentXblock >= NumXSectors || currentZblock >= NumZSectors)
                {
                    if (currentX == maxX)
                        return true;
                }
                else
                {
                    int currentYclick = currentY / -256;

                    if (currentXblock > 0)
                    {
                        Block currentBlock = Blocks[currentXblock - 1, currentZblock];

                        if ((currentBlock.Floor.XnZp + currentBlock.Floor.XnZn) / 2 > currentYclick ||
                            (currentBlock.Ceiling.XnZp + currentBlock.Ceiling.XnZn) / 2 < currentYclick ||
                            currentBlock.Type == BlockType.Wall)
                        {
                            return false;
                        }
                    }

                    if (currentX == maxX)
                    {
                        return true;
                    }

                    if (currentXblock > 0)
                    {
                        var currentBlock = Blocks[currentXblock - 1, currentZblock];
                        var nextBlock = Blocks[currentXblock, currentZblock];

                        if ((currentBlock.Floor.XpZn + currentBlock.Floor.XpZp) / 2 > currentYclick ||
                            (currentBlock.Ceiling.XpZn + currentBlock.Ceiling.XpZp) / 2 < currentYclick ||
                            currentBlock.Type == BlockType.Wall ||
                            (nextBlock.Floor.XnZp + nextBlock.Floor.XnZn) / 2 > currentYclick ||
                            (nextBlock.Ceiling.XnZp + nextBlock.Ceiling.XnZn) / 2 < currentYclick ||
                            nextBlock.Type == BlockType.Wall)
                        {
                            return false;
                        }
                    }
                }

                currentX += 1024;
                currentZ += (deltaZ << 10) / (deltaX + 1);
                currentY += (deltaY << 10) / (deltaX + 1);
            }
            while (currentX <= maxX);

            return true;
        }

        private bool RayTraceZ(int x, int y, int z, int xLight, int yLight, int zLight)
        {
            int deltaX;
            int deltaY;
            int deltaZ;

            int minZ;
            int maxZ;

            yLight = -yLight;
            y = -y;

            int yPoint = y;
            int xPoint = x;

            if (z <= zLight)
            {
                deltaX = xLight - x;
                deltaY = yLight - y;
                deltaZ = zLight - z;

                minZ = z;
                maxZ = zLight;
            }
            else
            {
                deltaX = x - xLight;
                deltaY = y - yLight;
                deltaZ = z - zLight;

                minZ = zLight;
                maxZ = z;

                xPoint = xLight;
                yPoint = yLight;
            }

            if (deltaZ == 0)
                return true;

            int fracZ = (((minZ >> 10) + 1) << 10) - minZ;
            int currentZ = ((minZ >> 10) + 1) << 10;
            int currentX = deltaX * fracZ / (deltaZ + 1) + xPoint;
            int currentY = deltaY * fracZ / (deltaZ + 1) + yPoint;

            if (currentZ > maxZ)
                return true;

            do
            {
                int currentXblock = currentX / 1024;
                int currentZblock = currentZ / 1024;

                if (currentXblock < 0 || currentZblock >= NumZSectors || currentXblock >= NumXSectors)
                {
                    if (currentZ == maxZ)
                        return true;
                }
                else
                {
                    int currentYclick = currentY / -256;

                    if (currentZblock > 0)
                    {
                        var currentBlock = Blocks[currentXblock, currentZblock - 1];

                        if ((currentBlock.Floor.XpZn + currentBlock.Floor.XnZn) / 2 > currentYclick ||
                            (currentBlock.Ceiling.XpZn + currentBlock.Ceiling.XnZn) / 2 < currentYclick ||
                            currentBlock.Type == BlockType.Wall)
                        {
                            return false;
                        }
                    }

                    if (currentZ == maxZ)
                    {
                        return true;
                    }

                    if (currentZblock > 0)
                    {
                        var currentBlock = Blocks[currentXblock, currentZblock - 1];
                        var nextBlock = Blocks[currentXblock, currentZblock];

                        if ((currentBlock.Floor.XnZp + currentBlock.Floor.XpZp) / 2 > currentYclick ||
                            (currentBlock.Ceiling.XnZp + currentBlock.Ceiling.XpZp) / 2 < currentYclick ||
                            currentBlock.Type == BlockType.Wall ||
                            (nextBlock.Floor.XpZn + nextBlock.Floor.XnZn) / 2 > currentYclick ||
                            (nextBlock.Ceiling.XpZn + nextBlock.Ceiling.XnZn) / 2 < currentYclick ||
                            nextBlock.Type == BlockType.Wall)
                        {
                            return false;
                        }
                    }
                }

                currentZ += 1024;
                currentX += (deltaX << 10) / (deltaZ + 1);
                currentY += (deltaY << 10) / (deltaZ + 1);
            }
            while (currentZ <= maxZ);

            return true;
        }

        public void RebuildLighting(bool highQualityLighting)
        {
            // Collect lights
            IEnumerable<LightInstance> lights = Objects.OfType<LightInstance>();
            if(GeometryReplacement == null)
            {
                if (RoomGeometry == null) return;
                // Calculate lighting
                for (int i = 0; i < RoomGeometry.VertexPositions.Count; i += 3)
                {
                    var normal = Vector3.Cross(
                        RoomGeometry.VertexPositions[i + 1] - RoomGeometry.VertexPositions[i],
                        RoomGeometry.VertexPositions[i + 2] - RoomGeometry.VertexPositions[i]);
                    normal = Vector3.Normalize(normal);

                    for (int j = 0; j < 3; ++j)
                    {
                        var position = RoomGeometry.VertexPositions[i + j];
                        Vector3 color = AmbientLight * 128;

                        foreach (var light in lights) // No Linq here because it's slow
                        {
                            if (light.IsStaticallyUsed)
                                color += CalculateLightForVertex(light, position, normal, true, highQualityLighting);
                        }

                        // Apply color
                        SetRoomGeometryVertexColor(Vector3.Max(color, new Vector3()) * (1.0f / 128.0f), i + j);
                    }
                }
            }else
            {
                // Calculate lighting
                for (int i = 0; i < GeometryReplacement.Positions.Count; i += 3)
                {
                    var normal = GeometryReplacement.Normals[i];

                    var position = GeometryReplacement.Positions[i];
                    Vector3 color = AmbientLight * 128;

                    foreach (var light in lights) // No Linq here because it's slow
                    {
                        if (light.IsStaticallyUsed)
                            color += CalculateLightForVertex(light, position, normal, true, highQualityLighting);
                    }

                    // Apply color
                    SetRoomGeometryReplacementVertexColor(Vector3.Max(color, new Vector3()) * (1.0f / 128.0f), i);
                }
            }
            
        }
        private static int GetLightSampleCount(LightInstance light)
        {
            int numSamples = 1;
            switch (light.Quality)
            {
                case LightQuality.Low:
                    numSamples = 3;
                    break;
                case LightQuality.Medium:
                    numSamples = 5;
                    break;
                case LightQuality.High:
                    numSamples = 7;
                    break;
            }
            return numSamples;
        }
        private bool LightRayTrace(Vector3 position, Vector3 lightPosition)
        {
            return !(
            RayTraceCheckFloorCeiling((int)position.X, (int)position.Y, (int)position.Z, (int)lightPosition.X, (int)lightPosition.Z) &&
            RayTraceX((int)position.X, (int)position.Y, (int)position.Z, (int)lightPosition.X, (int)lightPosition.Y, (int)lightPosition.Z) &&
            RayTraceZ((int)position.X, (int)position.Y, (int)position.Z, (int)lightPosition.X, (int)lightPosition.Y, (int)lightPosition.Z));
        }
        private float GetSampleSumFromLightTracing(int numSamples, Vector3 position, LightInstance light)
        {
            /*object lockingObject = new object();
            float sampleSum = 0;
            Parallel.For((int)(-numSamples / 2.0f), (int)(numSamples / 2.0f) + 1, (x) =>
            {
                Parallel.For((int)(-numSamples / 2.0f), (int)(numSamples / 2.0f) + 1, (y) =>
                {
                    Parallel.For((int)(-numSamples / 2.0f), (int)(numSamples / 2.0f) + 1, (z) =>
                    {
                        Vector3 samplePos = new Vector3(x * 256, y * 256, z * 256);
                        if (!LightRayTrace(room, position, light.Position + samplePos))
                            lock (lockingObject)
                            {
                                sampleSum += 1.0f;
                            }
                    });
                });
            });*/
            float sampleSum = 0.0f;
            for (int x = (int)(-numSamples / 2.0f); x <= (int)(numSamples / 2.0f); x++)
                for (int y = 0; y <= 0; y++)
                    for (int z = (int)(-numSamples / 2.0f); z <= (int)(numSamples / 2.0f); z++)
                    {
                        Vector3 samplePos = new Vector3(x * 256, y * 256, z * 256);
                        if (light.IsObstructedByRoomGeometry)
                        {
                            if (!LightRayTrace( position, light.Position + samplePos))
                            {
                                sampleSum += 1.0f;
                            }
                        }
                        else
                        {
                            sampleSum += 1;
                        }

                    }
            sampleSum /= (numSamples * 1 * numSamples);
            return sampleSum;
        }
        private Vector3 CalculateLightForVertex(LightInstance light, Vector3 position,
                                                      Vector3 normal, bool forRooms, bool highQuality)
        {
            if (!light.Enabled)
                return Vector3.Zero;

            Vector3 lightDirection;
            Vector3 lightVector;
            float distance;

            switch (light.Type)
            {
                case LightType.Point:
                case LightType.Shadow:
                    // Get the light vector
                    lightVector = position - light.Position;

                    // Get the distance between light and vertex
                    distance = lightVector.Length();

                    // Normalize the light vector
                    lightVector = Vector3.Normalize(lightVector);

                    if (distance + 64.0f <= light.OuterRange * 1024.0f)
                    {
                        // If distance is greater than light out radius, then skip this light
                        if (distance > light.OuterRange * 1024.0f)
                            return Vector3.Zero;

                        // Calculate light diffuse value
                        int diffuse = (int)(light.Intensity * 8192);

                        // Calculate the length squared of the normal vector
                        float dotN = Vector3.Dot((!forRooms ? -lightVector : normal), normal);

                        // Do raytracing
                        float sampleSum = 0;
                        if (dotN <= 0 || forRooms)
                        {
                            int numSamples;
                            if (highQuality)
                                numSamples = GetLightSampleCount(light);
                            else
                                numSamples = 1;
                            sampleSum = GetSampleSumFromLightTracing(numSamples, position, light);

                            if (sampleSum < 0.000001f)
                                return Vector3.Zero;
                        }

                        // Calculate the attenuation
                        float attenuaton = (light.OuterRange * 1024.0f - distance) / (light.OuterRange * 1024.0f - light.InnerRange * 1024.0f);
                        if (attenuaton > 1.0f)
                            attenuaton = 1.0f;
                        if (attenuaton <= 0.0f)
                            return Vector3.Zero;

                        // Calculate final light color
                        float finalIntensity = dotN * attenuaton * diffuse * sampleSum;
                        return finalIntensity * light.Color * (1.0f / 64.0f);
                    }
                    break;

                case LightType.Effect:
                    if (Math.Abs(Vector3.Distance(position, light.Position)) + 64.0f <= light.OuterRange * 1024.0f)
                    {
                        int x1 = (int)(Math.Floor(light.Position.X / 1024.0f) * 1024);
                        int z1 = (int)(Math.Floor(light.Position.Z / 1024.0f) * 1024);
                        int x2 = (int)(Math.Ceiling(light.Position.X / 1024.0f) * 1024);
                        int z2 = (int)(Math.Ceiling(light.Position.Z / 1024.0f) * 1024);

                        // TODO: winroomedit was supporting effect lights placed on vertical faces and effects light was applied to owning face
                        if ((position.X == x1 && position.Z == z1 || position.X == x1 && position.Z == z2 || position.X == x2 && position.Z == z1 ||
                             position.X == x2 && position.Z == z2) && position.Y <= light.Position.Y)
                        {
                            float finalIntensity = light.Intensity * 8192 * 0.25f;
                            return finalIntensity * light.Color * (1.0f / 64.0f);
                        }
                    }
                    break;

                case LightType.Sun:
                    {
                        // Do raytracing now for saving CPU later
                        float sampleSum = 0;
                        if (forRooms)
                        {
                            int numSamples;
                            if (highQuality)
                                numSamples = GetLightSampleCount(light);
                            else
                                numSamples = 1;
                            sampleSum = GetSampleSumFromLightTracing(numSamples, position, light);

                            if (sampleSum < 0.000001f)
                                return Vector3.Zero;
                        }

                        // Calculate the light direction
                        lightDirection = light.GetDirection();

                        // calcolo la luce diffusa
                        float diffuse = -Vector3.Dot(lightDirection, normal);

                        if (diffuse <= 0)
                            return Vector3.Zero;

                        if (diffuse > 1)
                            diffuse = 1.0f;


                        float finalIntensity = diffuse * light.Intensity * 8192.0f * sampleSum;
                        if (finalIntensity < 0)
                            return Vector3.Zero;
                        return finalIntensity * light.Color * (1.0f / 64.0f);
                    }

                case LightType.Spot:
                    if (Math.Abs(Vector3.Distance(position, light.Position)) + 64.0f <= light.OuterRange * 1024.0f)
                    {
                        // Calculate the ray from light to vertex
                        lightVector = Vector3.Normalize(position - light.Position);

                        // Get the distance between light and vertex
                        distance = Math.Abs((position - light.Position).Length());

                        // If distance is greater than light length, then skip this light
                        if (distance > light.OuterRange * 1024.0f)
                            return Vector3.Zero;

                        // Calculate the light direction
                        lightDirection = light.GetDirection();

                        // Calculate the cosines values for In, Out
                        double d = Vector3.Dot(lightVector, lightDirection);
                        double cosI2 = Math.Cos(light.InnerAngle * (Math.PI / 180));
                        double cosO2 = Math.Cos(light.OuterAngle * (Math.PI / 180));

                        if (d < cosO2)
                            return Vector3.Zero;

                        float sampleSum = 0;
                        if (forRooms)
                        {
                            int numSamples;
                            if (highQuality)
                                numSamples = GetLightSampleCount(light);
                            else
                                numSamples = 1;
                            sampleSum = GetSampleSumFromLightTracing(numSamples,position, light);

                            if (sampleSum < 0.000001f)
                                return Vector3.Zero;
                        }

                        // Calculate light diffuse value
                        float factor = (float)(1.0f - (d - cosI2) / (cosO2 - cosI2));
                        if (factor > 1.0f)
                            factor = 1.0f;
                        if (factor <= 0.0f)
                            return Vector3.Zero;

                        float attenuation = 1.0f;
                        if (distance >= light.InnerRange * 1024.0f)
                            attenuation = 1.0f - (distance - light.InnerRange * 1024.0f) / (light.OuterRange * 1024.0f - light.InnerRange * 1024.0f);

                        if (attenuation > 1.0f)
                            attenuation = 1.0f;
                        if (attenuation < 0.0f)
                            return Vector3.Zero;

                        float dot1 = -Vector3.Dot(lightDirection, normal);
                        if (dot1 < 0.0f)
                            return Vector3.Zero;
                        if (dot1 > 1.0f)
                            dot1 = 1.0f;

                        float finalIntensity = attenuation * dot1 * factor * light.Intensity * 8192.0f * sampleSum;
                        return finalIntensity * light.Color * (1.0f / 64.0f);
                    }
                    break;

            }

            return Vector3.Zero;
        }
    }
}
