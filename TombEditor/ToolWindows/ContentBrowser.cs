using DarkUI.Docking;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using TombLib.Controls;
using TombLib.Forms;
using TombLib.LevelData;
using TombLib.Wad;
using TombEditor.ViewModels;

namespace TombEditor.ToolWindows
{
    /// <summary>
    /// Content Browser tool window — provides a grid-based asset explorer
    /// for all IWadObject assets (Moveables, Statics, ImportedGeometry).
    /// Hosts a WPF ContentBrowserView via ElementHost inside a DarkToolWindow.
    /// </summary>
    public partial class ContentBrowser : DarkToolWindow
    {
        private readonly Editor _editor;
        private readonly ContentBrowserViewModel _viewModel;
        private OffscreenItemRenderer _renderer;
        private bool _thumbnailRenderPending;
        private Timer _thumbnailTimer;

        public ContentBrowser()
        {
            InitializeComponent();

            _editor = Editor.Instance;
            _viewModel = new ContentBrowserViewModel();

            // Set the WPF view's DataContext
            contentBrowserView.DataContext = _viewModel;

            // Subscribe to ViewModel events
            _viewModel.SelectedItemChanged += ViewModel_SelectedItemChanged;
            _viewModel.DragDropRequested += ViewModel_DragDropRequested;
            _viewModel.ThumbnailRenderRequested += ViewModel_ThumbnailRenderRequested;
            _viewModel.LocateItemRequested += ViewModel_LocateItemRequested;
            _viewModel.AddItemRequested += ViewModel_AddItemRequested;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Load saved tile width from configuration
            _viewModel.TileWidth = _editor.Configuration.ContentBrowser_TileWidth;

            // Timer for batched/deferred thumbnail rendering
            _thumbnailTimer = new Timer { Interval = 100 };
            _thumbnailTimer.Tick += ThumbnailTimer_Tick;

            // Enable drag-drop on this WinForms control
            AllowDrop = false; // We are a drag source, not a target

            // Subscribe to editor events
            _editor.EditorEventRaised += EditorEventRaised;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _editor.EditorEventRaised -= EditorEventRaised;
                _viewModel.SelectedItemChanged -= ViewModel_SelectedItemChanged;
                _viewModel.DragDropRequested -= ViewModel_DragDropRequested;
                _viewModel.ThumbnailRenderRequested -= ViewModel_ThumbnailRenderRequested;
                _viewModel.LocateItemRequested -= ViewModel_LocateItemRequested;
                _viewModel.AddItemRequested -= ViewModel_AddItemRequested;
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

                _thumbnailTimer?.Stop();
                _thumbnailTimer?.Dispose();
                _thumbnailTimer = null;

                _renderer?.Dispose();
                _renderer = null;
            }
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>
        /// Handles drag-drop requests from the WPF view.
        /// Uses WinForms Control.DoDragDrop which is compatible with
        /// Panel3D's OnDragDrop handler (expects IWadObject via GetFormats()[0]).
        /// </summary>
        private void ViewModel_DragDropRequested(object sender, AssetItemViewModel item)
        {
            if (item?.WadObject == null)
                return;

            // WinForms DoDragDrop — Panel3D picks this up via:
            //   e.Data.GetData(e.Data.GetFormats()[0]) as IWadObject
            DoDragDrop(item.WadObject, DragDropEffects.Copy);
        }

        /// <summary>
        /// Handles locate item requests from the WPF context menu.
        /// </summary>
        private void ViewModel_LocateItemRequested(object sender, EventArgs e)
        {
            var selected = _viewModel.SelectedItem;
            if (selected == null) return;

            if (selected.WadObject is ImportedGeometry geo)
                EditorActions.FindImportedGeometry(geo);
            else
                EditorActions.FindItem();
        }

        /// <summary>
        /// Handles add item requests from the WPF context menu.
        /// </summary>
        private void ViewModel_AddItemRequested(object sender, EventArgs e)
        {
            var selected = _viewModel.SelectedItem;
            if (selected == null) return;

            if (selected.WadObject is ImportedGeometry)
            {
                _editor.Action = new EditorActionPlace(false, (l, r) => new ImportedGeometryInstance());
            }
            else if (_editor.ChosenItem.HasValue)
            {
                var currentItem = _editor.ChosenItem.Value;
                if (!currentItem.IsStatic && _editor.SelectedRoom != null &&
                    _editor.SelectedRoom.Alternated && _editor.SelectedRoom.AlternateRoom == null)
                {
                    _editor.SendMessage("You can't add moveables to a flipped room.", PopupType.Info);
                    return;
                }
                _editor.Action = new EditorActionPlace(false, (r, l) => ItemInstance.FromItemType(currentItem));
            }
        }

        /// <summary>
        /// Saves tile width to configuration when it changes.
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ContentBrowserViewModel.TileWidth))
            {
                _editor.Configuration.ContentBrowser_TileWidth = _viewModel.TileWidth;
            }
        }

        /// <summary>
        /// Queues thumbnail rendering when assets change.
        /// Uses a short timer to batch multiple rapid requests.
        /// </summary>
        private void ViewModel_ThumbnailRenderRequested(object sender, EventArgs e)
        {
            _thumbnailRenderPending = true;
            _thumbnailTimer?.Stop();
            _thumbnailTimer?.Start();
        }

        /// <summary>
        /// Processes queued thumbnail rendering on the UI thread.
        /// Renders thumbnails in batches to avoid blocking the UI for too long.
        /// </summary>
        private void ThumbnailTimer_Tick(object sender, EventArgs e)
        {
            _thumbnailTimer?.Stop();

            if (!_thumbnailRenderPending)
                return;

            _thumbnailRenderPending = false;
            RenderThumbnails();
        }

        /// <summary>
        /// Renders 3D thumbnails for all items that don't have one yet.
        /// Creates the OffscreenItemRenderer on first use.
        /// </summary>
        private void RenderThumbnails()
        {
            try
            {
                // Lazily create the renderer (requires D3D11 device to be ready)
                if (_renderer == null)
                {
                    _renderer = new OffscreenItemRenderer();
                }

                // Sync background color from editor's color scheme
                _renderer.ClearColor = _editor.Configuration.UI_ColorScheme.Color3DBackground;

                var itemsToRender = _viewModel.GetItemsNeedingThumbnails().ToList();
                if (itemsToRender.Count == 0)
                    return;

                // Determine thumbnail pixel size from current tile settings
                int thumbPixelSize = Math.Max(64, (int)(_viewModel.ThumbSize * 2)); // render at 2x for quality

                foreach (var item in itemsToRender)
                {
                    try
                    {
                        var image = _renderer.RenderThumbnail(item.WadObject, thumbPixelSize);
                        var bitmapSource = AssetItemViewModel.ImageCToBitmapSource(image);
                        _viewModel.SetThumbnail(item, bitmapSource);
                    }
                    catch
                    {
                        // Silently skip items that fail to render (e.g., missing meshes)
                    }
                }

                // Clean up GPU resources after batch
                _renderer.GarbageCollect();
            }
            catch
            {
                // If renderer creation fails, disable thumbnail rendering
                _renderer?.Dispose();
                _renderer = null;
            }
        }

        private void EditorEventRaised(IEditorEvent obj)
        {
            // Refresh asset list when wads, geometries, or game version change
            if (obj is Editor.LoadedWadsChangedEvent ||
                obj is Editor.LoadedImportedGeometriesChangedEvent ||
                obj is Editor.GameVersionChangedEvent ||
                obj is Editor.LevelChangedEvent)
            {
                // Invalidate thumbnail cache when wads change since meshes may differ
                if (obj is Editor.LoadedWadsChangedEvent ||
                    obj is Editor.LoadedImportedGeometriesChangedEvent)
                {
                    _viewModel.InvalidateThumbnailCache();
                    _renderer?.Dispose();
                    _renderer = null;
                }

                RefreshAssets();
            }

            // Also refresh on configuration change (game version display may change)
            if (obj is Editor.ConfigurationChangedEvent)
            {
                RefreshAssets();
            }

            // Sync selection when item is chosen from other tool windows
            if (obj is Editor.ChosenItemChangedEvent itemChanged)
            {
                if (itemChanged.Current.HasValue)
                    SyncSelectionFromEditor(itemChanged.Current.Value);
            }

            if (obj is Editor.ChosenImportedGeometryChangedEvent geoChanged)
            {
                if (geoChanged.Current != null)
                    SyncImportedGeometrySelection(geoChanged.Current);
            }

            // Update keyboard shortcuts
            if (obj is Editor.ConfigurationChangedEvent configChanged)
            {
                if (configChanged.UpdateKeyboardShortcuts)
                    CommandHandler.AssignCommandsToControls(_editor, this, toolTip, true);
            }

            // Activate default control
            if (obj is Editor.DefaultControlActivationEvent activationEvent)
            {
                if (DockPanel != null && activationEvent.ContainerName == GetType().Name)
                {
                    MakeActive();
                }
            }

            // Initial load
            if (obj is Editor.InitEvent)
            {
                RefreshAssets();
            }
        }

        private void RefreshAssets()
        {
            LevelSettings settings = _editor?.Level?.Settings;
            if (settings == null)
                return;

            _viewModel.RefreshAssets(settings);
        }

        /// <summary>
        /// Updates the Editor's ChosenItem/ChosenImportedGeometry when the user
        /// selects an asset in the Content Browser.
        /// </summary>
        private void ViewModel_SelectedItemChanged(object sender, AssetItemViewModel item)
        {
            if (item == null)
                return;

            var wadObject = item.WadObject;

            if (wadObject is WadMoveable moveable)
            {
                _editor.ChosenItem = new ItemType(moveable.Id, _editor?.Level?.Settings);
            }
            else if (wadObject is WadStatic staticMesh)
            {
                _editor.ChosenItem = new ItemType(staticMesh.Id, _editor?.Level?.Settings);
            }
            else if (wadObject is ImportedGeometry geo)
            {
                _editor.ChosenImportedGeometry = geo;
            }
        }

        /// <summary>
        /// Syncs the Content Browser selection when an item is chosen from
        /// another tool window (e.g., ItemBrowser).
        /// </summary>
        private void SyncSelectionFromEditor(ItemType item)
        {
            if (item.IsStatic)
            {
                var staticObj = _editor.Level.Settings.WadTryGetStatic(item.StaticId);
                if (staticObj != null)
                    SelectWadObject(staticObj);
            }
            else
            {
                var moveable = _editor.Level.Settings.WadTryGetMoveable(item.MoveableId);
                if (moveable != null)
                    SelectWadObject(moveable);
            }
        }

        /// <summary>
        /// Syncs the Content Browser selection when an imported geometry is chosen
        /// from another tool window.
        /// </summary>
        private void SyncImportedGeometrySelection(ImportedGeometry geo)
        {
            SelectWadObject(geo);
        }

        private void SelectWadObject(IWadObject wadObject)
        {
            // Temporarily detach event to avoid feedback loop
            _viewModel.SelectedItemChanged -= ViewModel_SelectedItemChanged;

            _viewModel.SelectedItem = _viewModel.AllItems
                .FirstOrDefault(i => ReferenceEquals(i.WadObject, wadObject));

            _viewModel.SelectedItemChanged += ViewModel_SelectedItemChanged;
        }
    }
}
