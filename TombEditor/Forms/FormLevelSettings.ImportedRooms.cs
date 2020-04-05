using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TombLib.GeometryIO;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Forms
{
    partial class FormLevelSettings
    {
        private BindingList<Room> roomBindings;
        private void importedRoomsDataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _soundDataGridViewDataSource.Count)
                return;

            if (importedRoomsDataGridView.Columns[e.ColumnIndex].Name == colImportedRoomGeoPath.Name)
            {
                string path = _importedRoomGeometryGridViewDataSource[e.RowIndex].Path;
                string parsedPath = _levelSettings.ParseVariables(path);
                string absolutePath = _levelSettings.MakeAbsolute(path);
                bool isRooted = Path.IsPathRooted(parsedPath);
                bool exists = File.Exists(absolutePath);
                if (isRooted = Path.IsPathRooted(parsedPath) && !exists)
                {
                    e.CellStyle.BackColor = _wrongColor;
                    e.CellStyle.SelectionBackColor = e.CellStyle.SelectionBackColor.MixWith(_wrongColor, 0.4);
                }else
                {
                    e.CellStyle.BackColor = _columnMessageCorrectColor;
                    e.CellStyle.SelectionBackColor = e.CellStyle.SelectionBackColor.MixWith(_columnMessageCorrectColor, 0.4);
                }
                importedRoomsDataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = absolutePath;
                e.FormattingApplied = true;
            }
        }
        private void initializeImportedRoomsDataGridView()
        {
            importedRoomsDataGridView.Load += ImportedRoomsDataGridView_Load;
            importedRoomsDataGridView.CellFormatting += importedRoomsDataGridView_CellFormatting;
            importedRoomsControls.Enabled = true;
            importedRoomsControls.DataGridView = importedRoomsDataGridView;
            importedRoomsControls.AllowUserMove = true;
            importedRoomsControls.AllowUserDelete = true;
            importedRoomsControls.AllowUserNew = true;
            importedRoomsControls.CreateNewRow = importedRoomsDataGridViewCreateNewRow;
            importedRoomsDataGridView.DataSource = _importedRoomGeometryGridViewDataSource;
            importedRoomsDataGridView.CellContentClick += importedRoomDataGridView_CellContentClick;
            foreach (var entry in _levelSettings.ImportedRoomGeometryPaths)
            {
                _importedRoomGeometryGridViewDataSource.Add(entry.Clone());
            }
        }

        private void ImportedRoomsDataGridView_Load(object sender, EventArgs e)
        {
        }

        private void importedRoomDataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _importedRoomGeometryGridViewDataSource.Count)
                return;
            if (importedRoomsDataGridView.Columns[e.ColumnIndex].Name == colImportedRoomGeoPathBrowse.Name)
            {
                string result = LevelFileDialog.BrowseFile(this, _levelSettings, _importedRoomGeometryGridViewDataSource[e.RowIndex].Path,"Choose Room Geometry File", BaseGeometryImporter.FileExtensions,VariableType.LevelDirectory,false);
                if (result != null)
                    _importedRoomGeometryGridViewDataSource[e.RowIndex].Path = result;
            }
        }
    }
}
