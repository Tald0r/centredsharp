using CentrED.Map;
using ClassicUO.Assets;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Numerics;
using static CentrED.Application; // Assuming CEDGame access

namespace CentrED.UI.Windows;

public class MultisWindow : Window
{
    public override string Name => "Multis";
     public override WindowState DefaultState => new()
    {
        IsOpen = false // Default to closed initially
    };

    private string _filter = "";
    private int _selectedId = -1;
    private int[] _matchedIds = System.Array.Empty<int>();
    private RenderTarget2D _previewTarget;
    private GraphicsDevice _graphicsDevice;
    private SpriteBatch _spriteBatch; // Use a dedicated SpriteBatch for preview
    private float _zoom = 1.0f;
    private Vector2 _previewOffset = Vector2.Zero; // For panning
    private bool _isDraggingPreview = false;
    private Vector2 _dragStartPreviewMouse;
    private Vector2 _dragStartOffset;


    public MultisWindow(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
        _spriteBatch = new SpriteBatch(gd); // Create a new SpriteBatch
        // Attempt to load multis immediately if manager might already be loaded, or rely on filtering later
        if (MultisManager.Instance != null)
        {
            FilterMultis();
        }
    }

     ~MultisWindow()
    {
        _previewTarget?.Dispose();
        _spriteBatch?.Dispose();
    }


    private void FilterMultis()
    {
        if (MultisManager.Multis == null)
        {
             _matchedIds = System.Array.Empty<int>();
             return;
        }

        var allIds = MultisManager.Multis.Keys;
        if (string.IsNullOrWhiteSpace(_filter))
        {
            _matchedIds = allIds.OrderBy(id => id).ToArray();
        }
        else
        {
            var lowerFilter = _filter.ToLowerInvariant();
             _matchedIds = allIds
                .Where(id => id.ToString().Contains(lowerFilter) || $"0x{id:x4}".Contains(lowerFilter)) // Filter by decimal or hex ID
                .OrderBy(id => id)
                .ToArray();
        }
         if (_selectedId != -1 && !_matchedIds.Contains(_selectedId))
        {
            // If current selection is filtered out, try to select the first available, otherwise deselect
            _selectedId = _matchedIds.Length > 0 ? _matchedIds[0] : -1;
            ResetPreview(); // Reset zoom/pan on selection change
        }
        else if (_selectedId == -1 && _matchedIds.Length > 0)
        {
             _selectedId = _matchedIds[0]; // Select first if nothing selected
             ResetPreview();
        }
    }

    private void ResetPreview()
    {
        _zoom = 1.0f;
        _previewOffset = Vector2.Zero;
    }

    protected override void InternalDraw()
    {
        if (!CEDClient.Initialized) // Check if client/game is ready
        {
            ImGui.Text("Not connected or initialized.");
            return;
        }
         if (MultisManager.Instance == null || MultisManager.Multis == null)
        {
             ImGui.Text("Multis not loaded. Check file paths or initialization order.");
             return;
        }
         // Ensure filter is run at least once if needed
        if (_matchedIds.Length == 0 && MultisManager.Count > 0)
        {
            FilterMultis();
        }


        ImGui.InputText("Filter ID", ref _filter, 64);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            FilterMultis();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _filter = "";
            FilterMultis();
        }

        float listWidth = 150;
        float previewHeight = 300; // Initial height, will be adjusted by splitter

        var bottomPaneHeight = 50; // Height for the details pane

        // Use a splitter for list and preview/info
        ImGui.BeginChild("TopPane", new Vector2(-1, -bottomPaneHeight - ImGui.GetStyle().ItemSpacing.Y), false); // Leave space for bottom pane

        ImGui.Columns(2, "MultiSplitter", true);
        if (ImGui.GetColumnIndex() == 0) // Set initial width only once
            ImGui.SetColumnWidth(0, listWidth);

        // Left Column: List
        ImGui.BeginChild("MultiList", Vector2.Zero, ImGuiChildFlags.Border); // Use Vector2.Zero to fill column
        if (ImGui.BeginListBox("##multislistbox", new Vector2(-1, -1))) // Fill available space
        {
             unsafe // Use clipper for performance
            {
                ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                clipper.Begin(_matchedIds.Length);
                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        if (i < 0 || i >= _matchedIds.Length) continue; // Bounds check

                        var id = _matchedIds[i];
                        bool isSelected = id == _selectedId;
                        if (ImGui.Selectable($"0x{id:X4} ({id})", isSelected))
                        {
                            if (_selectedId != id) // Check if selection actually changed
                            {
                                _selectedId = id;
                                ResetPreview();
                            }
                        }
                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                }
                clipper.End();
            }
            ImGui.EndListBox();
        }
        ImGui.EndChild(); // End MultiList

        ImGui.NextColumn();

        // Right Column: Preview and Info
        ImGui.BeginChild("MultiPreviewPane", Vector2.Zero); // Fill column

        // Info Area
        var infoHeight = 50;
        ImGui.BeginChild("MultiInfo", new Vector2(-1, infoHeight), ImGuiChildFlags.Border);
        if (_selectedId != -1)
        {
             var multi = MultisManager.GetMulti(_selectedId);
             if (multi != null)
             {
                 ImGui.Text($"ID: 0x{_selectedId:X4} ({_selectedId})"); ImGui.SameLine();
                 ImGui.Text($"Size: {multi.Width}x{multi.Height}"); ImGui.SameLine();
                 ImGui.Text($"Components: {multi.Items.Length}");
             }
             else
             {
                 ImGui.Text($"ID: 0x{_selectedId:X4} (Not Found/Loaded)");
             }
        }
        else
        {
            ImGui.Text("Select a multi from the list.");
        }
        ImGui.EndChild(); // End MultiInfo

        // Preview Area
        ImGui.BeginChild("MultiPreview", Vector2.Zero, ImGuiChildFlags.Border); // Fill remaining space in right column
        DrawMultiPreview();
        HandlePreviewInput(); // Handle zoom and pan
        ImGui.EndChild(); // End MultiPreview

        ImGui.EndChild(); // End MultiPreviewPane

        ImGui.Columns(1); // Reset columns before ending top pane

        ImGui.EndChild(); // End TopPane

        // Bottom Pane: Details/Controls
        ImGui.BeginChild("MultiDetails", Vector2.Zero, ImGuiChildFlags.Border); // Fill bottom space
        ImGui.Text("Preview Controls:"); ImGui.SameLine();
        ImGui.PushItemWidth(100);
        if (ImGui.SliderFloat("Zoom", ref _zoom, 0.1f, 10.0f))
        {
            // Clamp zoom if needed
            _zoom = Math.Max(0.1f, _zoom);
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Reset View"))
        {
            ResetPreview();
        }
        // Add more controls here if needed
        ImGui.EndChild(); // End MultiDetails
    }

     private void DrawMultiPreview()
    {
        var previewMin = ImGui.GetItemRectMin();
        var previewMax = ImGui.GetItemRectMax();
        var previewSize = previewMax - previewMin;

        if (previewSize.X <= 0 || previewSize.Y <= 0) return;

        int targetWidth = (int)previewSize.X;
        int targetHeight = (int)previewSize.Y;

        // Recreate RenderTarget if size changed
        if (_previewTarget == null || _previewTarget.Width != targetWidth || _previewTarget.Height != targetHeight)
        {
            _previewTarget?.Dispose();
            // Ensure dimensions are valid
            if (targetWidth > 0 && targetHeight > 0)
            {
                 _previewTarget = new RenderTarget2D(_graphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.Color, DepthFormat.Depth24);
            }
            else
            {
                _previewTarget = null; // Can't create if size is invalid
                return;
            }
        }

        var currentTarget = _graphicsDevice.GetRenderTargets();
        _graphicsDevice.SetRenderTarget(_previewTarget);
        _graphicsDevice.Clear(Color.DimGray); // Use a neutral background

        if (_selectedId != -1)
        {
            var multi = MultisManager.GetMulti(_selectedId);
            if (multi != null && multi.Items.Length > 0)
            {
                // Use MapManager's rendering logic if possible, otherwise draw manually
                var arts = CEDGame.MapManager.Arts; // Access existing Arts

                // Sort items for correct drawing order (Z -> Y -> X)
                var sortedItems = multi.Items
                    .OrderBy(item => item.Z)
                    .ThenBy(item => item.Y)
                    .ThenBy(item => item.X)
                    .ToArray();

                // Calculate center point for drawing based on multi's center
                // Offset drawing so multi's logical center (MinX + CenterX, MinY + CenterY) appears at the preview center + _previewOffset
                float centerX = (multi.MinX + multi.CenterX) * 22; // TILE_WIDTH / 2
                float centerY = (multi.MinY + multi.CenterY) * 22; // TILE_HEIGHT / 2

                float renderOriginX = (targetWidth / 2f) + _previewOffset.X;
                float renderOriginY = (targetHeight / 2f) + _previewOffset.Y; // Adjust Y origin if needed (e.g., for tall multis)


                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone);

                foreach (var item in sortedItems)
                {
                    // Skip "dynamic" items (invisible placeholders) or invalid TileIDs
                    if (item.TileID <= 0 || item.TileID >= TileDataLoader.Instance.StaticData.Length) continue;

                    var tileData = TileDataLoader.Instance.StaticData[item.TileID];
                    var spriteInfo = arts.GetArt(item.TileID + ArtLoader.MAX_LAND_DATA_INDEX_COUNT);

                    if (spriteInfo.Texture != null)
                    {
                        // Calculate isometric screen position relative to multi origin (item.X, item.Y, item.Z)
                        // Using the standard UO projection:
                        float isoX = (item.X - item.Y) * 22; // TILE_WIDTH / 2
                        float isoY = (item.X + item.Y) * 22; // TILE_HEIGHT / 2

                        // Adjust for Z height
                        isoY -= item.Z * 4; // Z_STEP

                        // Adjust for tile graphic height
                        isoY -= tileData.Height;

                        // Center the graphic (spriteInfo.UV is the rectangle within the texture)
                        float drawX = isoX - spriteInfo.UV.Width / 2f;
                        float drawY = isoY - spriteInfo.UV.Height; // Origin is usually bottom-center/bottom-left for UO statics

                        // Apply zoom and centering/offset
                        drawX = renderOriginX + (drawX - centerX) * _zoom;
                        drawY = renderOriginY + (drawY - centerY) * _zoom;

                        // Apply hue (Simplified: White for now)
                        // TODO: Integrate HuesManager for proper hue rendering if needed
                        Color tint = Color.White;

                        _spriteBatch.Draw
                        (
                            spriteInfo.Texture,
                            new Vector2(drawX, drawY),
                            spriteInfo.UV,
                            tint,
                            0f,
                            Vector2.Zero, // Origin is top-left for SpriteBatch.Draw
                            _zoom, // Apply zoom scale
                            SpriteEffects.None,
                            0f // Depth layer (can be refined based on Z/Y for complex sorts)
                        );
                    }
                }
                _spriteBatch.End();
            }
        }

        _graphicsDevice.SetRenderTargets(currentTarget);

        // Display the RenderTarget in ImGui
        IntPtr texPtr = CEDGame.UIManager._uiRenderer.BindTexture(_previewTarget);
        // UV coordinates are flipped vertically for RenderTargets in FNA/MonoGame by default when drawing to screen
        ImGui.Image(texPtr, previewSize, Vector2.UnitY, Vector2.UnitX, Vector4.One, Vector4.Zero);
    }

     private void HandlePreviewInput()
    {
        if (!ImGui.IsItemHovered())
        {
            _isDraggingPreview = false; // Stop dragging if mouse leaves preview
            return;
        }

        var io = ImGui.GetIO();

        // Zooming with Mouse Wheel
        if (io.MouseWheel != 0)
        {
            float zoomFactor = 1.1f;
            float scale = (io.MouseWheel > 0) ? zoomFactor : 1.0f / zoomFactor;
            _zoom *= scale;
            _zoom = Math.Clamp(_zoom, 0.1f, 10.0f); // Clamp zoom level

            // Optional: Zoom towards mouse cursor
            // Vector2 mousePosInPreview = io.MousePos - ImGui.GetItemRectMin();
            // _previewOffset = mousePosInPreview + (_previewOffset - mousePosInPreview) * scale;
        }

        // Panning with Middle Mouse Button (or Right Button)
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            if (!_isDraggingPreview)
            {
                _isDraggingPreview = true;
                _dragStartPreviewMouse = io.MousePos;
                _dragStartOffset = _previewOffset;
            }
        }

        if (_isDraggingPreview)
        {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle) || ImGui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                _isDraggingPreview = false;
            }
            else
            {
                Vector2 delta = io.MousePos - _dragStartPreviewMouse;
                _previewOffset = _dragStartOffset + delta;
            }
        }
    }
}