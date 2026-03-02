using DarkUI.Docking;
using System;
using System.Collections.Generic;
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
        private Timer _thumbnailTimer;

        /// <summary>
        /// Queue of items waiting for thumbnail rendering. Processed in small batches
        /// across multiple timer ticks to keep the UI responsive.
        /// </summary>
        private List<AssetItemViewModel> _thumbnailQueue;
        private int _thumbnailQueueIndex;

        /// <summary>
        /// Number of thumbnails to render per timer tick. Balances rendering speed
        /// with UI responsiveness — each render involves a D3D11 GPU draw + readback.
        /// </summary>
        private const int ThumbnailBatchSize = 10;

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

            // Timer for batched/deferred thumbnail rendering (50ms between batches)
            _thumbnailTimer = new Timer { Interval = 50 };
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
        /// Builds the queue of items needing thumbnails and starts the batched timer.
        /// </summary>
        private void ViewModel_ThumbnailRenderRequested(object sender, EventArgs e)
        {
            // Build/rebuild the queue from items that still need thumbnails
            _thumbnailQueue = _viewModel.GetItemsNeedingThumbnails().ToList();
            _thumbnailQueueIndex = 0;

            if (_thumbnailQueue.Count > 0)
            {
                _thumbnailTimer?.Stop();
                _thumbnailTimer?.Start();
            }
        }

        /// <summary>
        /// Renders the next batch of thumbnails on each timer tick.
        /// Keeps the UI responsive by only processing ThumbnailBatchSize items per tick.
        /// </summary>
        private void ThumbnailTimer_Tick(object sender, EventArgs e)
        {
            if (_thumbnailQueue == null || _thumbnailQueueIndex >= _thumbnailQueue.Count)
            {
                // All items rendered — stop timer and clean up
                _thumbnailTimer?.Stop();
                _thumbnailQueue = null;
                _thumbnailQueueIndex = 0;
                _renderer?.GarbageCollect();
                return;
            }

            RenderThumbnailBatch();
        }

        /// <summary>
        /// Renders a small batch of thumbnails (up to ThumbnailBatchSize).
        /// Called from the timer tick so D3D11 rendering stays on the UI thread.
        /// </summary>
        private void RenderThumbnailBatch()
        {
            try
            {
                // Lazily create the renderer (requires D3D11 device to be ready)
                if (_renderer == null)
                    _renderer = new OffscreenItemRenderer();

                // Sync background color from editor's color scheme
                _renderer.ClearColor = _editor.Configuration.UI_ColorScheme.Color3DBackground;

                // Determine thumbnail pixel size from current tile settings
                int thumbPixelSize = Math.Max(64, (int)(_viewModel.ThumbSize * 2)); // render at 2x for quality

                int end = Math.Min(_thumbnailQueueIndex + ThumbnailBatchSize, _thumbnailQueue.Count);

                for (int i = _thumbnailQueueIndex; i < end; i++)
                {
                    var item = _thumbnailQueue[i];
                    try
                    {
                        // Skip items that already got a thumbnail (e.g., from cache restore)
                        if (item.Thumbnail != null)
                            continue;

                        // Apply Lara skin substitution for moveables (shared with ItemBrowser)
                        IWadObject renderObject = WadObjectRenderHelper.ApplyLaraSkin(
                            item.WadObject, _editor.Level.Settings);

                        var image = _renderer.RenderThumbnail(renderObject, thumbPixelSize);
                        var bitmapSource = AssetItemViewModel.ImageCToBitmapSource(image);
                        _viewModel.SetThumbnail(item, bitmapSource);
                    }
                    catch
                    {
                        // Silently skip items that fail to render (e.g., missing meshes)
                    }
                }

                _thumbnailQueueIndex = end;
            }
            catch
            {
                // If renderer creation fails, stop rendering
                _thumbnailTimer?.Stop();
                _thumbnailQueue = null;
                _thumbnailQueueIndex = 0;
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
                    // Stop any in-progress rendering
                    _thumbnailTimer?.Stop();
                    _thumbnailQueue = null;
                    _thumbnailQueueIndex = 0;

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
