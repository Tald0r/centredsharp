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
    private TileObject? _startTile = null;     // Anchor/center point
    private TileObject? _endTile = null;       // Target endpoint
    
    // Path settings
    private int _transitionType = 0;           // 0=Linear, 1=Smooth, 2=S-Curve
    private int _pathWidth = 5;                // Width of path in tiles
    private bool _useEdgeFade = true;          // Fade edges of path
    private int _edgeFadeWidth = 2;            // Width of fade area in tiles
    private bool _heightThreshold = true;      // Only create paths between different heights
    private int _minHeightDiff = 3;            // Minimum height difference to create path
    private bool _respectExistingTerrain = true;
    private float _blendFactor = 0.5f;
    
    // Internal calculation fields
    private Vector2 _pathDirection;            // Direction vector from start to end
    private float _pathLength;                 // Length of path in tiles

    // Main drawing UI
    internal override void Draw()
    {
        ImGui.Text("Path Settings");
        ImGui.BeginChild("PathSettings", new System.Numerics.Vector2(-1, 180), ImGuiChildFlags.Border);
        
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
        
        // Height difference threshold
        ImGui.Checkbox("Require Height Difference", ref _heightThreshold);
        if (_heightThreshold)
        {
            ImGui.SameLine(140);
            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Min Difference", ref _minHeightDiff);
            if (_minHeightDiff < 0) _minHeightDiff = 0;
        }
        
        // Edge fade
        ImGui.Checkbox("Edge Fade", ref _useEdgeFade);
        if (_useEdgeFade)
        {
            ImGui.SameLine(140);
            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Fade Width", ref _edgeFadeWidth);
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
        
        ImGui.Spacing();
        
        // Instructions
        ImGui.TextWrapped("Hold CTRL and click to set the start point, then drag to create a path to your target area.");
        ImGui.TextWrapped("The tool will create a smooth height transition between different elevations.");
        
        // Show current selection status
        if (_startTile != null && _endTile != null)
        {
            ImGui.Separator();
            ImGui.Text($"Start: ({_startTile.Tile.X},{_startTile.Tile.Y}, Z:{_startTile.Tile.Z})");
            ImGui.Text($"End: ({_endTile.Tile.X},{_endTile.Tile.Y}, Z:{_endTile.Tile.Z})");
            
            int heightDiff = Math.Abs(_endTile.Tile.Z - _startTile.Tile.Z);
            ImGui.Text($"Height Difference: {heightDiff} tiles");
            
            if (_pathLength > 0 && heightDiff > 0)
            {
                float slope = heightDiff / _pathLength;
                ImGui.Text($"Average Slope: {slope:F2} (1:{(1/slope):F1})");
            }
        }
    }
    
    // GhostApply - Required by BaseTool
    protected override void GhostApply(TileObject? o)
    {
        if (o is not LandObject landObject)
            return;
        
        bool ctrlPressed = Keyboard.GetState().IsKeyDown(Keys.LeftControl) || 
                           Keyboard.GetState().IsKeyDown(Keys.RightControl);
        
        if (ctrlPressed) 
        {
            if (!_isSelecting)
            {
                // First click - set the start point
                _isSelecting = true;
                _startTile = o;
                ClearGhosts(); // Clear any existing ghosts
            }
            else if (_startTile != null)
            {
                // Update the end point as we drag
                _endTile = o;
                
                // Calculate path direction and length
                float dx = _endTile.Tile.X - _startTile.Tile.X;
                float dy = _endTile.Tile.Y - _startTile.Tile.Y;
                _pathDirection = new Vector2(dx, dy);
                _pathLength = _pathDirection.Length();
                
                // Skip if start and end are the same tile
                if (_pathLength < 0.1f)
                    return;
                    
                // Skip if heights are the same and we require height difference
                if (_heightThreshold && 
                    Math.Abs(_endTile.Tile.Z - _startTile.Tile.Z) < _minHeightDiff)
                    return;
                
                // Normalize direction vector
                _pathDirection = Vector2.Normalize(_pathDirection);
                
                // Preview the path
                PreviewPath();
            }
        }
    }
    
    // GhostClear - Required by BaseTool
    protected override void GhostClear(TileObject? o)
    {
        if (_isSelecting && !Keyboard.GetState().IsKeyDown(Keys.LeftControl) && 
            !Keyboard.GetState().IsKeyDown(Keys.RightControl))
        {
            _isSelecting = false;
            _startTile = null;
            _endTile = null;
            ClearGhosts();
        }
    }
    
    // InternalApply - Required by BaseTool
    protected override void InternalApply(TileObject? o)
    {
        if (_startTile == null || _endTile == null || Random.Next(100) >= _chance)
            return;
            
        // Apply the changes from ghost tiles to actual tiles
        foreach (var pair in MapManager.GhostLandTiles)
        {
            pair.Key.LandTile.ReplaceLand(pair.Key.LandTile.Id, pair.Value.Tile.Z);
        }
        
        // Clean up
        ClearGhosts();
        _isSelecting = false;
        _startTile = null;
        _endTile = null;
    }
    
    // Clear all ghost tiles
    private void ClearGhosts()
    {
        foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
        {
            pair.Key.Reset();
            MapManager.GhostLandTiles.Remove(pair.Key);
        }
    }
    
    // Preview the path with ghost tiles
    private void PreviewPath()
    {
        if (_startTile == null || _endTile == null)
            return;
            
        // Clear previous ghosts
        ClearGhosts();
        
        // Calculate perpendicular vector for path width
        Vector2 perpendicular = new Vector2(-_pathDirection.Y, _pathDirection.X);
        
        // Get tile coordinates
        int startX = _startTile.Tile.X;
        int startY = _startTile.Tile.Y;
        int endX = _endTile.Tile.X; 
        int endY = _endTile.Tile.Y;
        
        // Start and end heights
        sbyte startZ = _startTile.Tile.Z;
        sbyte endZ = _endTile.Tile.Z;
        
        // Determine bounding box to check
        int minX = Math.Min(startX, endX) - _pathWidth;
        int maxX = Math.Max(startX, endX) + _pathWidth;
        int minY = Math.Min(startY, endY) - _pathWidth;
        int maxY = Math.Max(startY, endY) + _pathWidth;
        
        // Track tiles we've modified
        Dictionary<(int, int), LandObject> pendingGhostTiles = new();
        
        // Check all tiles in bounding box
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                // Skip if out of bounds
                if (!Client.IsValidX((ushort)x) || !Client.IsValidY((ushort)y))
                    continue;
                
                // Get the land object
                LandObject? lo = MapManager.LandTiles[x, y];
                if (lo == null)
                    continue;
                
                // Calculate the position relative to the path
                Vector2 relPos = new Vector2(x - startX, y - startY);
                
                // Project onto the path direction to get distance along path
                float projDist = Vector2.Dot(relPos, _pathDirection);
                
                // Calculate normalized distance (0 at start, 1 at end)
                float normDist = Math.Clamp(projDist / _pathLength, 0, 1);
                
                // Project onto perpendicular to get distance from path centerline
                float lateralDist = Math.Abs(Vector2.Dot(relPos, perpendicular));
                
                // Skip if too far from path
                if (lateralDist > _pathWidth)
                    continue;
                
                // Get current height
                sbyte currentZ = lo.Tile.Z;
                
                // Calculate target height based on gradient
                float factor = GetGradientFactor(normDist);
                int targetHeight = (int)Math.Round(startZ + (endZ - startZ) * factor);
                
                // Apply edge fade if enabled
                if (_useEdgeFade && lateralDist > _pathWidth - _edgeFadeWidth)
                {
                    float fadeDistance = lateralDist - (_pathWidth - _edgeFadeWidth);
                    float fadeFactor = 1.0f - (fadeDistance / _edgeFadeWidth);
                    
                    // Blend between current height and target height based on fade factor
                    targetHeight = (int)Math.Round(
                        currentZ * (1 - fadeFactor) + targetHeight * fadeFactor
                    );
                }
                
                // Respect existing terrain if enabled
                if (_respectExistingTerrain)
                {
                    targetHeight = (int)Math.Round(
                        currentZ * (1 - _blendFactor) + targetHeight * _blendFactor
                    );
                }
                
                // Clamp to valid height range
                targetHeight = Math.Clamp(targetHeight, -128, 127);
                
                // Create ghost tile if height changed
                if (targetHeight != currentZ)
                {
                    sbyte newZ = (sbyte)targetHeight;
                    lo.Visible = false;
                    var newTile = new LandTile(lo.LandTile.Id, lo.Tile.X, lo.Tile.Y, newZ);
                    var ghostTile = new LandObject(newTile);
                    
                    pendingGhostTiles[(x, y)] = ghostTile;
                    MapManager.GhostLandTiles[lo] = ghostTile;
                }
            }
        }
        
        // Update all ghost tiles for visual preview
        foreach (var ghostTile in pendingGhostTiles.Values)
        {
            MapManager.OnLandTileElevated(ghostTile.LandTile, ghostTile.LandTile.Z);
        }
    }
    
    // Get gradient factor based on selected transition type
    private float GetGradientFactor(float t)
    {
        return _transitionType switch
        {
            0 => t,                    // Linear
            1 => SmoothStep(t),        // Smooth
            2 => SCurve(t),            // S-Curve
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
}