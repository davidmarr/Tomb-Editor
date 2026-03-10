using DarkUI.Docking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TombLib.Controls;
using TombLib.LevelData;
using TombLib.Rendering;
using TombLib.Wad;
using TombLib.Wad.Catalog;

namespace TombEditor.ToolWindows
{
    public partial class ItemBrowser : DarkToolWindow
    {
        private readonly Editor _editor;

        public ItemBrowser()
        {
            InitializeComponent();
            CommandHandler.AssignCommandsToControls(Editor.Instance, this, toolTip);

            _editor = Editor.Instance;
            _editor.EditorEventRaised += EditorEventRaised;

            lblFromWad.ForeColor = DarkUI.Config.Colors.DisabledText;
        }

        public void InitializeRendering(RenderingDevice device)
        {
            panelItem.InitializeRendering(device, _editor.Configuration.RenderingItem_Antialias);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _editor.EditorEventRaised -= EditorEventRaised;
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        private void EditorEventRaised(IEditorEvent obj)
        {
            // Update available items combo box
            if (obj is Editor.LoadedWadsChangedEvent ||
                obj is Editor.GameVersionChangedEvent ||
                obj is Editor.ConfigurationChangedEvent)
            {
                var allMoveables = _editor.Level.Settings.WadGetAllMoveables();
                var allStatics   = _editor.Level.Settings.WadGetAllStatics();

                comboItems.GameVersion = _editor.Level.Settings.GameVersion;
                comboItems.Items.Clear();
                foreach (var moveable in allMoveables.Values)
                    if (!_editor.Configuration.RenderingItem_HideInternalObjects ||
                        !TrCatalog.IsHidden(_editor.Level.Settings.GameVersion, moveable.Id.TypeId))
                        comboItems.Items.Add(moveable);
                
                foreach (var staticMesh in allStatics.Values)
                    comboItems.Items.Add(staticMesh);

                if (comboItems.Items.Count > 0)
                {
                    // Check if any reloaded wads still have current selected item present. If they do, re-select it
                    // to preserve item list position. If item is not present, just reset selection to first item in the list.

                    var chosenWadObject = GetFirstWadObject(_editor.ChosenItems);

                    if (chosenWadObject != null && comboItems.Items.Contains(chosenWadObject))
                    {
                        comboItems.SelectedItem = panelItem.CurrentObject = chosenWadObject;
                        panelItem.ResetCamera();
                    }
                    else
                    {
                        comboItems.SelectedIndex = 0;

                        // Update visible conflicting item, otherwise it's not updated in 3D control.
                        if (comboItems.SelectedItem is WadMoveable)
                        {
                            var currentObject = (WadMoveableId)panelItem.CurrentObject.Id;
                            if (allMoveables.ContainsKey(currentObject))
                                panelItem.CurrentObject = allMoveables[currentObject];
                        }
                        else if (comboItems.SelectedItem is WadStatic)
                        {
                            var currentObject = (WadStaticId)panelItem.CurrentObject.Id;
                            if (allStatics.ContainsKey(currentObject))
                                panelItem.CurrentObject = allStatics[currentObject];
                        }
                    }
                }
            }

            // Update selection of items combo box.
            if (obj is Editor.ChosenItemsChangedEvent itemsChanged)
            {
                var wadObject = GetFirstWadObject(itemsChanged.Current);
                if (wadObject != null)
                {
                    comboItems.SelectedItem = panelItem.CurrentObject = wadObject;
                    MakeActive();
                    panelItem.ResetCamera();
                }
            }

            if (obj is Editor.ChosenItemsChangedEvent ||
                obj is Editor.GameVersionChangedEvent ||
                obj is Editor.LevelChangedEvent ||
                obj is Editor.LoadedWadsChangedEvent ||
                obj is Editor.ConfigurationChangedEvent)
                FindLaraSkin();

            // Update tooltip texts
            if (obj is Editor.ConfigurationChangedEvent)
            {
                if (((Editor.ConfigurationChangedEvent)obj).UpdateKeyboardShortcuts)
                    CommandHandler.AssignCommandsToControls(_editor, this, toolTip, true);
            }

            // Activate default control
            if (obj is Editor.DefaultControlActivationEvent)
            {
                if (DockPanel != null && ((Editor.DefaultControlActivationEvent)obj).ContainerName == GetType().Name)
                {
                    MakeActive();
                    comboItems.Search();
                }
            }

            // Update UI
            if (obj is Editor.ConfigurationChangedEvent ||
                obj is Editor.InitEvent)
            {
                panelItem.AnimatePreview = _editor.Configuration.RenderingItem_Animate;
                lblFromWad.Visible = _editor.Configuration.RenderingItem_ShowMultipleWadsPrompt;
            }

        }

        private static IWadObject GetFirstWadObject(IReadOnlyList<IWadObject> items)
        {
            if (items == null)
                return null;

            foreach (var obj in items)
            {
                if (obj is WadMoveable || obj is WadStatic)
                    return obj;
            }

            return null;
        }

        private void FindLaraSkin()
        {
            if (comboItems.Items.Count == 0 || comboItems.SelectedIndex < 0 || !(comboItems.SelectedItem is WadMoveable item))
                return;

            panelItem.CurrentObject = WadObjectRenderHelper.GetRenderObject(item, _editor.Level.Settings);
            panelItem.ResetCamera();
        }

        private void comboItems_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboItems.SelectedItem is IWadObject wadObject)
                _editor.ChosenItems = new[] { wadObject };

            var itemType = comboItems.SelectedItem switch
            {
                WadMoveable m => (ItemType?)new ItemType(m.Id, _editor?.Level?.Settings),
                WadStatic s   => (ItemType?)new ItemType(s.Id, _editor?.Level?.Settings),
                _             => null
            };

            if (itemType != null)
            {
                bool multiple;
                var wad = _editor.Level.Settings.WadTryGetWad(itemType.Value, out multiple);

                if (wad != null)
                {
                    lblFromWad.Text = "From " + Path.GetFileName(wad.Path);
                    toolTip.SetToolTip(lblFromWad, (multiple ? "This object exists in several wads." + "\n" + "Used one is: " : "From: ") + _editor.Level.Settings.MakeAbsolute(wad.Path));
                    return;
                }
            }

            lblFromWad.Text = string.Empty;
            toolTip.SetToolTip(lblFromWad, string.Empty);
        }

        private void comboItems_Format(object sender, ListControlConvertEventArgs e)
        {
            TRVersion.Game? gameVersion = _editor?.Level?.Settings?.GameVersion;
            IWadObject listItem = e.ListItem as IWadObject;
            if (gameVersion != null && listItem != null)
                e.Value = listItem.ToString(gameVersion.Value);
        }

        private void butItemUp_Click(object sender, EventArgs e)
        {
            if (comboItems.Items.Count == 0)
                return;

            if (comboItems.SelectedIndex > 0)
                comboItems.SelectedIndex--;
            else
                comboItems.SelectedIndex = comboItems.Items.Count - 1;
        }

        private void butItemDown_Click(object sender, EventArgs e)
        {
            if (comboItems.Items.Count == 0)
                return;

            if (comboItems.SelectedIndex < comboItems.Items.Count - 1)
                comboItems.SelectedIndex++;
            else
                comboItems.SelectedIndex = 0;
        }
    }
}
