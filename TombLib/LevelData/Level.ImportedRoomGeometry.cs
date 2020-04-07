using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TombLib.LevelData
{
    partial class Level
    {
        private List<int> getReplacedRoomsFromImportedRoomMeshFile(string path)
        {
            List<int> replacedRooms = new List<int>();
            string parsedPath = Settings.ParseVariables(path);
            return replacedRooms;
        }
    }
}
