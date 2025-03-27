using CentrED.Map;
using CentrED.UI;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using static CentrED.Application;

namespace CentrED.Tools;

public class GradientTool : BaseTool
{
    public override string Name => "Gradient";
    public override Keys Shortcut => Keys.F9;

    // Selection state
    private bool _isSelecting = false;
    private TileObject? _startTile = null;
    private TileObject? _endTile = null;
    private Rectangle _selectionArea = Rectangle.Empty;
    
    // Gradient settings
    private int _transitionType = 0; // 0=Linear, 1=Smooth, 2=S-Curve
    private bool _useEdgeFade = true;
    private int _edgeFadeWidth = 2;
    private bool _useHeightLimits = false;
    private int _minHeight = -128;
    private int _maxHeight = 127;
    private int _pathWidth = 5;
    private bool _respectExistingTerrain = true;
    private float _blendFactor = 0.5f;
    
    // Internal calculation fields
    private Vector2 _gradientDirection;
    private float _gradientLength;
    private int _startHeight;
    private int _endHeight;

    // Main drawing UI
    internal override void Draw()
    {
        DrawGradientSection();
        ImGui.Spacing();
        DrawLimitsSection();
        ImGui.Spacing();
        DrawInstructionsSection();
    }
    
    private void DrawGradientSection()
    {
        ImGui.Text("Gradient Settings");
        ImGui.BeginChild("GradientSection", new System.Numerics.Vector2(-1, 140), ImGuiChildFlags.Border);
        
        // Transition type dropdown
        ImGui.Text("Transition Type:");
        ImGui.SameLine(140);
        ImGui.SetNextItemWidth(150);
        
        string[] transitionTypes = { "Linear", "Smooth", "S-Curve" };
        if (ImGui.BeginCombo("##transitionType", transitionTypes[_transitionType]))
        {
            for (int i = 0; i < transitionTypes.Length; i++)
            {
                bool isSelected = (_transitionType == i);
                if (ImGui.Selectable(transitionTypes[i], isSelected))
                    _transitionType = i;
                
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        
        // Path width
        ImGui.Text("Path Width:");
        ImGui.SameLine(140);
        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("##pathWidth", ref _pathWidth);
        if (_pathWidth < 1) _pathWidth = 1;
        
        // Edge fade
        ImGui.Checkbox("Edge Fade", ref _useEdgeFade);
        if (_useEdgeFade)
        {
            ImGui.SameLine(140);
            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Width##fadeWidth", ref _edgeFadeWidth);
            if (_edgeFadeWidth < 1) _edgeFadeWidth = 1;
        }
        
        // Terrain blending
        ImGui.Checkbox("Respect Existing Terrain", ref _respectExistingTerrain);
        if (_respectExistingTerrain)
        {
            ImGui.SameLine(140);
            ImGui.SetNextItemWidth(150);
            ImGui.SliderFloat("Blend Factor", ref _blendFactor, 0.0f, 1.0f, "%.2f");
        }
        
        ImGui.EndChild();
    }

    private void DrawLimitsSection()
    {
        ImGui.Text("Height Limits");
        ImGui.BeginChild("LimitsSection", new System.Numerics.Vector2(-1, 80), ImGuiChildFlags.Border);
        
        // Height constraints checkbox
        ImGui.Checkbox("Use Height Limits", ref _useHeightLimits);
        
        // Min/Max height inputs
        ImGui.BeginDisabled(!_useHeightLimits);
        
        ImGui.Text("Min Height:");
        ImGui.SameLine(120);
        ImGui.SetNextItemWidth(80);
        ImGui.InputInt("##minHeight", ref _minHeight);
        if (_minHeight < -128) _minHeight = -128;
        
        ImGui.SameLine(220);
        ImGui.Text("Max Height:");
        ImGui.SameLine(300);
        ImGui.SetNextItemWidth(80);
        ImGui.InputInt("##maxHeight", ref _maxHeight);
        if (_maxHeight > 127) _maxHeight = 127;
        
        ImGui.EndDisabled();
        
        ImGui.EndChild();
    }

    private void DrawInstructionsSection()
    {
        ImGui.TextWrapped("Hold CTRL and drag with left mouse button to define a gradient area.");
        ImGui.TextWrapped("The tool will create a smooth height transition between start and end points.");
        
        if (_selectionArea != Rectangle.Empty)
        {
            ImGui.Separator();
            ImGui.Text($"Selection: ({_selectionArea.X},{_selectionArea.Y}) to ({_selectionArea.Right-1},{_selectionArea.Bottom-1})");
            
            if (_startHeight != _endHeight)
            {
                ImGui.Text($"Height Change: {_startHeight} to {_endHeight} ({Math.Abs(_endHeight - _startHeight)} tiles difference)");
                float slope = Math.Abs(_endHeight - _startHeight) / (float)_gradientLength;
                ImGui.Text($"Average Slope: {slope:F2}");
            }
        }
    }
    
    // Method to handle mouse press events
    public override void OnMousePressed(TileObject? o)
    {
        if (o is not LandObject landObject)
            return;
            
        bool ctrlPressed = Keyboard.GetState().IsKeyDown(Keys.LeftControl) || 
                          Keyboard.GetState().IsKeyDown(Keys.RightControl);
        
        if (ctrlPressed) 
        {
            _isSelecting = true;
            _startTile = o;
            _selectionArea = Rectangle.Empty;
        }
        else
        {
            base.OnMousePressed(o);
        }
    }
    
    // Method to handle mouse movement during selection
    public override void OnMouseEnter(TileObject? o)
    {
        if (!_isSelecting || o is not LandObject landObject || _startTile == null)
        {
            base.OnMouseEnter(o);
            return;
        }
        
        // Update selection area
        int startX = Math.Min(_startTile.Tile.X, o.Tile.X);
        int startY = Math.Min(_startTile.Tile.Y, o.Tile.Y);
        int endX = Math.Max(_startTile.Tile.X, o.Tile.X) + 1;
        int endY = Math.Max(_startTile.Tile.Y, o.Tile.Y) + 1;
        
        _selectionArea = new Rectangle(startX, startY, endX - startX, endY - startY);
        _endTile = o;
        
        // Preview the gradient
        PreviewGradient();
    }
    
    // Method to handle releasing the mouse button
    public override void OnMouseReleased(TileObject? o)
    {
        if (_isSelecting && _startTile != null && _endTile != null)
        {
            _isSelecting = false;
            
            // Apply the final gradient
            if (_selectionArea.Width > 1 || _selectionArea.Height > 1)
            {
                ApplyGradient();
            }
        }
        else
        {
            base.OnMouseReleased(o);
        }
    }
    
    // Reset the selection when leaving the current tile
    public override void OnMouseLeave(TileObject? o)
    {
        if (!_isSelecting)
        {
            // Clear all ghost tiles
            foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
            {
                pair.Key.Reset();
                MapManager.GhostLandTiles.Remove(pair.Key);
            }
        }
        
        base.OnMouseLeave(o);
    }
    
    // Preview the gradient with ghost tiles
    private void PreviewGradient()
    {
        if (_startTile == null || _endTile == null)
            return;
            
        // Clear previous ghost tiles
        foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
        {
            pair.Key.Reset();
            MapManager.GhostLandTiles.Remove(pair.Key);
        }
        
        // Calculate gradient direction and length
        float startX = _startTile.Tile.X;
        float startY = _startTile.Tile.Y;
        float endX = _endTile.Tile.X;
        float endY = _endTile.Tile.Y;
        
        _gradientDirection = new Vector2(endX - startX, endY - startY);
        _gradientLength = _gradientDirection.Length();
        
        if (_gradientLength > 0)
            _gradientDirection = Vector2.Normalize(_gradientDirection);
            
        _startHeight = _startTile.Tile.Z;
        _endHeight = _endTile.Tile.Z;
        
        // Generate ghost tiles to preview the gradient
        Dictionary<(int, int), LandObject> pendingGhostTiles = new();
        
        // Create ghosts for all tiles in the selection area
        for (int x = _selectionArea.Left; x < _selectionArea.Right; x++)
        {
            for (int y = _selectionArea.Top; y < _selectionArea.Bottom; y++)
            {
                LandObject? lo = MapManager.LandTiles[x, y];
                if (lo == null)
                    continue;
                    
                // Calculate new height based on position in gradient
                sbyte newZ = CalculateGradientHeight(x, y);
                
                // Create ghost tile
                lo.Visible = false;
                var newTile = new LandTile(lo.LandTile.Id, lo.Tile.X, lo.Tile.Y, newZ);
                var ghostTile = new LandObject(newTile);
                
                pendingGhostTiles[(x, y)] = ghostTile;
                MapManager.GhostLandTiles[lo] = ghostTile;
            }
        }
        
        // Update all tiles for visual preview
        foreach (var ghostTile in pendingGhostTiles.Values)
        {
            MapManager.OnLandTileElevated(ghostTile.LandTile, ghostTile.LandTile.Z);
        }
    }
    
    // Calculate height at a specific position based on gradient settings
    private sbyte CalculateGradientHeight(int x, int y)
    {
        LandObject? lo = MapManager.LandTiles[x, y];
        if (lo == null)
            return 0;
            
        // Calculate position along the gradient (0.0 - 1.0)
        float projectionDistance = CalculateProjectionDistance(x, y);
        
        // Calculate perpendicular distance from the gradient center line
        float perpendicularDistance = CalculatePerpendicularDistance(x, y);
        
        // If outside the path width, don't modify
        float halfPathWidth = _pathWidth / 2.0f;
        if (perpendicularDistance > halfPathWidth)
            return lo.Tile.Z;
            
        // Compute the base gradient height value
        float gradientFactor = GetGradientFactor(projectionDistance);
        
        // Apply edge fade if enabled
        if (_useEdgeFade && perpendicularDistance > halfPathWidth - _edgeFadeWidth)
        {
            float fadeDistance = perpendicularDistance - (halfPathWidth - _edgeFadeWidth);
            float fadeFactor = 1.0f - (fadeDistance / _edgeFadeWidth);
            gradientFactor = lo.Tile.Z + (gradientFactor - lo.Tile.Z) * fadeFactor;
        }
        
        // Calculate target height
        int targetHeight = (int)Math.Round(_startHeight + (_endHeight - _startHeight) * gradientFactor);
        
        // Blend with existing terrain if enabled
        if (_respectExistingTerrain)
        {
            targetHeight = (int)Math.Round(lo.Tile.Z * (1 - _blendFactor) + targetHeight * _blendFactor);
        }
        
        // Apply height limits if enabled
        if (_useHeightLimits)
        {
            targetHeight = Math.Clamp(targetHeight, _minHeight, _maxHeight);
        }
        
        return (sbyte)Math.Clamp(targetHeight, -128, 127);
    }
    
    // Calculate the normalized distance along the gradient direction
    private float CalculateProjectionDistance(int x, int y)
    {
        if (_gradientLength == 0)
            return 0;
            
        Vector2 toPoint = new Vector2(x - _startTile.Tile.X, y - _startTile.Tile.Y);
        float projection = Vector2.Dot(toPoint, _gradientDirection);
        
        // Clamp to 0-1 range
        return Math.Clamp(projection / _gradientLength, 0, 1);
    }
    
    // Calculate perpendicular distance from the gradient center line
    private float CalculatePerpendicularDistance(int x, int y)
    {
        if (_gradientLength == 0)
            return 0;
            
        Vector2 toPoint = new Vector2(x - _startTile.Tile.X, y - _startTile.Tile.Y);
        float projection = Vector2.Dot(toPoint, _gradientDirection);
        
        Vector2 projectedPoint = _gradientDirection * projection;
        return Vector2.Distance(toPoint, projectedPoint);
    }
    
    // Get gradient factor based on selected transition type
    private float GetGradientFactor(float t)
    {
        return _transitionType switch
        {
            0 => t, // Linear
            1 => SmoothStep(t), // Smooth
            2 => SCurve(t), // S-Curve
            _ => t
        };
    }
    
    // Smoothstep function for smooth transition
    private float SmoothStep(float t)
    {
        return t * t * (3 - 2 * t);
    }
    
    // S-Curve function for more pronounced easing
    private float SCurve(float t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }
    
    // Apply the final gradient
    private void ApplyGradient()
    {
        if (_selectionArea == Rectangle.Empty)
            return;
            
        // Apply the changes from ghost tiles to actual tiles
        for (int x = _selectionArea.Left; x < _selectionArea.Right; x++)
        {
            for (int y = _selectionArea.Top; y < _selectionArea.Bottom; y++)
            {
                LandObject? lo = MapManager.LandTiles[x, y];
                if (lo == null)
                    continue;
                    
                if (MapManager.GhostLandTiles.TryGetValue(lo, out var ghostTile))
                {
                    lo.LandTile.ReplaceLand(lo.LandTile.Id, ghostTile.Tile.Z);
                }
            }
        }
        
        // Clear all ghost tiles
        foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
        {
            pair.Key.Reset();
            MapManager.GhostLandTiles.Remove(pair.Key);
        }
    }
}