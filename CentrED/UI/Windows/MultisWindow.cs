using CentrED.Map;
using ClassicUO.Assets;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using CentrED.IO;
using CentrED.IO.Models;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static CentrED.Application; // Assuming CEDGame access for UIManager/Arts/Hues
using Vector2 = System.Numerics.Vector2;

namespace CentrED.UI.Windows;

public class MultisWindow : Window
{
    public override string Name => "Multis";
    public override WindowState DefaultState => new()
    {
        IsOpen = false
    };

    private string _filter = "";
    private int _selectedId = -1;
    private int[] _matchedIds = System.Array.Empty<int>();
    private RenderTarget2D _previewTarget;
    private GraphicsDevice _graphicsDevice;
    private SpriteBatch _spriteBatch;
    private float _zoom = 1.0f;
    private Vector2 _previewOffset = Vector2.Zero;
    private bool _isDraggingPreview = false;
    private Vector2 _dragStartPreviewMouse;
    private Vector2 _dragStartOffset;

    public MultisWindow(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
        _spriteBatch = new SpriteBatch(gd);
        // Filter will be called in InternalDraw when MultisManager.Instance is confirmed available
    }

    ~MultisWindow()
    {
        _previewTarget?.Dispose();
        _spriteBatch?.Dispose();
    }

    private void FilterMultis()
    {
        // Use MultisManager which wraps the ClassicUO loader access
        if (MultisManager.Instance == null) // Check if our manager is ready
        {
            _matchedIds = System.Array.Empty<int>();
            return;
        }

        // Get all potentially valid IDs from the manager
        var allValidIds = MultisManager.Instance.GetAllValidIds();

        if (string.IsNullOrWhiteSpace(_filter))
        {
            _matchedIds = allValidIds.ToArray(); // Already sorted by ID implicitly
        }
        else
        {
            var lowerFilter = _filter.ToLowerInvariant();
            _matchedIds = allValidIds
                .Where(id => id.ToString().Contains(lowerFilter) || $"0x{id:x4}".Contains(lowerFilter))
                // No need to OrderBy, already sorted by ID
                .ToArray();
        }
        if (_selectedId != -1 && !_matchedIds.Contains(_selectedId))
        {
            _selectedId = _matchedIds.Length > 0 ? _matchedIds[0] : -1;
            ResetPreview();
        }
        else if (_selectedId == -1 && _matchedIds.Length > 0)
        {
            _selectedId = _matchedIds[0];
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
        if (!CEDClient.Initialized)
        {
            ImGui.Text("Not connected or initialized.");
            return;
        }
        // Check if our MultisManager instance is ready
        if (MultisManager.Instance == null)
        {
            ImGui.Text("MultisManager not loaded. Check initialization order.");
            return;
        }
        // Ensure filter is run at least once if needed
        if (_matchedIds.Length == 0 && MultisManager.Instance.Count > 0)
        {
            FilterMultis(); // Run initial filter or if cleared
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

        // --- Layout remains the same ---
        float listWidth = 150;
        var bottomPaneHeight = 50;
        ImGui.BeginChild("TopPane", new Vector2(-1, -bottomPaneHeight - ImGui.GetStyle().ItemSpacing.Y), false);
        ImGui.Columns(2, "MultiSplitter", true);
        if (ImGui.GetColumnIndex() == 0) ImGui.SetColumnWidth(0, listWidth);

        // Left Column: List
        ImGui.BeginChild("MultiList", Vector2.Zero, ImGuiChildFlags.Borders);
        if (ImGui.BeginListBox("##multislistbox", new Vector2(-1, -1)))
        {
            unsafe
            {
                ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                clipper.Begin(_matchedIds.Length);
                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        if (i < 0 || i >= _matchedIds.Length) continue;

                        var id = _matchedIds[i];
                        bool isSelected = id == _selectedId;
                        if (ImGui.Selectable($"0x{id:X4} ({id})", isSelected))
                        {
                            if (_selectedId != id)
                            {
                                _selectedId = id;
                                ResetPreview();
                            }
                        }
                        if (isSelected) ImGui.SetItemDefaultFocus();
                    }
                }
                clipper.End();
            }
            ImGui.EndListBox();
        }
        ImGui.EndChild(); // End MultiList

        ImGui.NextColumn();

        // Right Column: Preview and Info
        ImGui.BeginChild("MultiPreviewPane", Vector2.Zero);

        // Info Area
        var infoHeight = 50;
        ImGui.BeginChild("MultiInfo", new Vector2(-1, infoHeight), ImGuiChildFlags.Borders);
        if (_selectedId != -1)
        {
            // Get multi components using our manager
            List<MultiComponent>? multiComponents = MultisManager.Instance.GetMultiComponents(_selectedId);

            // Check the result of GetMultiComponents
            if (multiComponents != null) // Call succeeded (index was in bounds, no read error)
            {
                if (multiComponents.Count > 0) // Multi has components
                {
                    // Calculate bounds dynamically
                    int minX = short.MaxValue, minY = short.MaxValue, maxX = short.MinValue, maxY = short.MinValue;
                    foreach (var item in multiComponents)
                    {
                        if (item.X < minX) minX = item.X;
                        if (item.Y < minY) minY = item.Y;
                        if (item.X > maxX) maxX = item.X;
                        if (item.Y > maxY) maxY = item.Y;
                    }
                    int width = (minX == short.MaxValue) ? 0 : maxX - minX + 1;
                    int height = (minY == short.MaxValue) ? 0 : maxY - minY + 1;

                    ImGui.Text($"ID: 0x{_selectedId:X4} ({_selectedId})"); ImGui.SameLine();
                    ImGui.Text($"Bounds: {width}x{height}"); ImGui.SameLine();
                    ImGui.Text($"Components: {multiComponents.Count}");
                }
                else // Multi is valid but empty
                {
                    ImGui.Text($"ID: 0x{_selectedId:X4} (Empty Multi)");
                }
            }
            else // GetMultiComponents returned null (index out of bounds or error loading/parsing)
            {
                 // We can't definitively say *why* it's null without IsValidIndex,
                 // so provide a generic error message.
                 ImGui.Text($"ID: 0x{_selectedId:X4} (Invalid Index or Error Loading)");
            }
        }
        else
        {
            ImGui.Text("Select a multi from the list.");
        }
        ImGui.EndChild(); // End MultiInfo

        // Preview Area
        ImGui.BeginChild("MultiPreview", Vector2.Zero, ImGuiChildFlags.Borders);
        DrawMultiPreview();
        HandlePreviewInput();
        ImGui.EndChild(); // End MultiPreview

        ImGui.EndChild(); // End MultiPreviewPane

        ImGui.Columns(1);
        ImGui.EndChild(); // End TopPane

        // Bottom Pane: Details/Controls
        ImGui.BeginChild("MultiDetails", Vector2.Zero, ImGuiChildFlags.Borders);
        ImGui.Text("Preview Controls:"); ImGui.SameLine();
        ImGui.PushItemWidth(100);
        if (ImGui.SliderFloat("Zoom", ref _zoom, 0.1f, 10.0f))
        {
            _zoom = Math.Max(0.1f, _zoom);
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Reset View"))
        {
            ResetPreview();
        }
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

        // Recreate RenderTarget if size changed or null
        if (_previewTarget == null || _previewTarget.Width != targetWidth || _previewTarget.Height != targetHeight)
        {
            _previewTarget?.Dispose();
            if (targetWidth > 0 && targetHeight > 0)
            {
                _previewTarget = new RenderTarget2D(_graphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.Color, DepthFormat.Depth24);
            }
            else
            {
                _previewTarget = null;
                return;
            }
        }

        var currentTarget = _graphicsDevice.GetRenderTargets();
        _graphicsDevice.SetRenderTarget(_previewTarget);
        _graphicsDevice.Clear(Color.DimGray);

        if (_selectedId != -1 && MultisManager.Instance != null)
        {
            // Get components via our manager
            List<MultiComponent>? multiComponents = MultisManager.Instance.GetMultiComponents(_selectedId); // Use MultiComponent

            if (multiComponents != null && multiComponents.Count > 0) // Check for null and empty
            {
                // Access Arts and Hues managers (ensure they are loaded)
                var arts = CEDGame.MapManager?.Arts;
                var hues = HuesManager.Instance; // Use HuesManager instance
                if (arts == null || hues == null)
                {
                    // Draw error message or skip if managers aren't ready
                    _spriteBatch.Begin();
                    // Consider drawing text here if FontSystem is available
                    _spriteBatch.End();
                    _graphicsDevice.SetRenderTargets(currentTarget); // Restore render target
                    // Display placeholder in ImGui
                    if (_previewTarget != null) ImGui.Image(CEDGame.UIManager._uiRenderer.BindTexture(_previewTarget), previewSize);
                    return;
                }

                // Calculate bounds and center dynamically
                int minX = short.MaxValue, minY = short.MaxValue;
                foreach (var item in multiComponents) // Use MultiComponent
                {
                    if (item.X < minX) minX = item.X;
                    if (item.Y < minY) minY = item.Y;
                }
                int multiCenterX = (minX == short.MaxValue) ? 0 : -minX;
                int multiCenterY = (minY == short.MaxValue) ? 0 : -minY;

                // Sort items for correct drawing order (Z -> Y -> X)
                var sortedItems = multiComponents
                    .Where(item => item.IsVisible) // Use IsVisible flag from MultiComponent
                    .OrderBy(item => item.Z)
                    .ThenBy(item => item.Y)
                    .ThenBy(item => item.X)
                    .ToList();

                float originIsoX = (minX + multiCenterX) * 22;
                float originIsoY = (minY + multiCenterY) * 22;

                float renderOriginX = (targetWidth / 2f) + _previewOffset.X;
                float renderOriginY = (targetHeight / 2f) + _previewOffset.Y;

                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone);

                foreach (var item in sortedItems) // Use MultiComponent
                {
                    // Check TileID validity against TileDataLoader
                    if (item.ID <= 0 || item.ID >= TileDataLoader.Instance.StaticData.Length) continue;

                    var tileData = TileDataLoader.Instance.StaticData[item.ID];
                    var spriteInfo = arts.GetArt(item.ID + ArtLoader.MAX_LAND_DATA_INDEX_COUNT);

                    if (spriteInfo.Texture != null)
                    {
                        // Use fields from MultiComponent
                        float isoX = (item.X - item.Y) * 22;
                        float isoY = (item.X + item.Y) * 22;
                        isoY -= item.Z * 4;
                        isoY -= tileData.Height;

                        float drawX = isoX - spriteInfo.UV.Width / 2f;
                        float drawY = isoY - spriteInfo.UV.Height;

                        drawX = renderOriginX + (drawX - originIsoX) * _zoom;
                        drawY = renderOriginY + (drawY - originIsoY) * _zoom;

                        // Apply hue using HuesManager
                        var hueVector = hues.GetHueVector(item.ID, 0); // Hue 0 for multis

                        // TODO: Implement HuesManager.VectorToColor(hueVector) or similar.
                        Color tint = Color.White; // Placeholder

                        _spriteBatch.Draw
                        (
                            spriteInfo.Texture,
                            new Vector2(drawX, drawY),
                            spriteInfo.UV,
                            tint, // Use calculated tint color
                            0f,
                            Vector2.Zero,
                            _zoom,
                            SpriteEffects.None,
                            0f
                        );
                    }
                }
                _spriteBatch.End();
            }
        }

        _graphicsDevice.SetRenderTargets(currentTarget);

        // Display the RenderTarget in ImGui
        if (_previewTarget != null)
        {
            IntPtr texPtr = CEDGame.UIManager._uiRenderer.BindTexture(_previewTarget);
            ImGui.Image(texPtr, previewSize, Vector2.UnitY, Vector2.UnitX, Vector4.One, Vector4.Zero);
        }
        else
        {
            ImGui.Text("Preview unavailable.");
        }
    }

    private void HandlePreviewInput()
    {
        if (!ImGui.IsItemHovered())
        {
            _isDraggingPreview = false;
            return;
        }

        var io = ImGui.GetIO();

        if (io.MouseWheel != 0)
        {
            float zoomFactor = 1.1f;
            float scale = (io.MouseWheel > 0) ? zoomFactor : 1.0f / zoomFactor;
            _zoom *= scale;
            _zoom = Math.Clamp(_zoom, 0.1f, 10.0f);
        }

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