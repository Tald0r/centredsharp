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
    private bool _isDragging = false;
    private TileObject? _startTile = null;     // Anchor/center point
    private TileObject? _endTile = null;       // Target endpoint
    private bool _pathGenerated = false;       // Flag to prevent constant updates
    
    // Path settings
    private int _transitionType = 0;           // 0=Linear, 1=Smooth, 2=S-Curve
    private int _pathWidth = 5;                // Width of path in tiles
    private bool _useEdgeFade = true;          // Fade edges of path
    private int _edgeFadeWidth = 2;            // Width of fade area in tiles
    private int _forcedHeightDiff = 5;         // Default height difference when start/end are the same
    private bool _respectExistingTerrain = true;
    private float _blendFactor = 0.5f;
    
    // Path limits - prevents excessive tiles being modified
    private int _maxPathLength = 50;           // Maximum number of tiles in a single path
    
    // Internal calculation fields
    private Vector2 _pathDirection;            // Direction vector from start to end
    private float _pathLength;                 // Length of path in tiles
    private bool _isFirstGradient;            // Tracks first tile placement in gradient

    // Main drawing UI
    internal override void Draw()
    {
        ImGui.Text("Path Settings");
        ImGui.BeginChild("PathSettings", new System.Numerics.Vector2(-1, 210), ImGuiChildFlags.Border);
        
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
        if (_pathWidth > 20) _pathWidth = 20;  // Limit max width
        
        // Default height change (for paths with same elevation)
        ImGui.Text("Default Height:");
        ImGui.SameLine(140);
        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("##forcedHeightDiff", ref _forcedHeightDiff);
        if (_forcedHeightDiff < 1) _forcedHeightDiff = 1;
        
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
        
        // Maximum path length
        ImGui.Text("Max Path Length:");
        ImGui.SameLine(140);
        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("##maxPathLength", ref _maxPathLength);
        if (_maxPathLength < 10) _maxPathLength = 10;
        if (_maxPathLength > 100) _maxPathLength = 100;
        
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
            
            int actualHeightDiff = Math.Abs(_endTile.Tile.Z - _startTile.Tile.Z);
            int displayHeightDiff = actualHeightDiff;
            
            // If heights are the same, show the forced diff
            if (actualHeightDiff == 0)
                displayHeightDiff = _forcedHeightDiff;
                
            ImGui.Text($"Height Difference: {displayHeightDiff} tiles");
            
            if (_pathLength > 0 && displayHeightDiff > 0)
            {
                float slope = displayHeightDiff / _pathLength;
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
        bool leftMousePressed = Mouse.GetState().LeftButton == ButtonState.Pressed;
        
        // Only start selection when CTRL+Left mouse button is pressed
        if (ctrlPressed) 
        {
            if (!_isSelecting && leftMousePressed)
            {
                // First click - set the start point
                _isSelecting = true;
                _isDragging = true;
                _startTile = o;
                _endTile = o; // Initialize end tile to start tile
                _pathGenerated = false;
                ClearGhosts();
                
                // Start tracking area operation - critical for all directions
                OnAreaOperationStart((ushort)o.Tile.X, (ushort)o.Tile.Y);
                Console.WriteLine($"Starting at ({o.Tile.X}, {o.Tile.Y})");
            }
            else if (_isSelecting && leftMousePressed && _startTile != null)
            {
                // Always update end point as we drag, even if same tile
                _endTile = o;
                
                // Update area operation - critical for tracking
                OnAreaOperationUpdate((ushort)o.Tile.X, (ushort)o.Tile.Y);
                Console.WriteLine($"Dragging to ({o.Tile.X}, {o.Tile.Y})");
                
                // Calculate path direction and length
                float dx = _endTile.Tile.X - _startTile.Tile.X;
                float dy = _endTile.Tile.Y - _startTile.Tile.Y;
                _pathDirection = new Vector2(dx, dy);
                _pathLength = _pathDirection.Length();
                
                // Skip if start and end are the same tile
                if (_pathLength < 0.1f)
                    return;
                
                // Always regenerate the path when mouse moves
                // This is key to making it work in all quadrants
                _pathGenerated = false;
                
                // Limit maximum path length to prevent lag
                if (_pathLength > _maxPathLength)
                {
                    // Scale down the path to maximum allowed length
                    float scaleFactor = _maxPathLength / _pathLength;
                    dx = (int)(dx * scaleFactor);
                    dy = (int)(dy * scaleFactor);
                    _endTile = new LandObject(new LandTile(0, 
                                        (ushort)(_startTile.Tile.X + dx), 
                                        (ushort)(_startTile.Tile.Y + dy),
                                        o.Tile.Z));
                    _pathDirection = new Vector2(dx, dy);
                    _pathLength = _pathDirection.Length();
                    
                    // Make sure to update area operation with new endpoint
                    OnAreaOperationUpdate((ushort)_endTile.Tile.X, (ushort)_endTile.Tile.Y);
                }
                
                // Normalize direction vector
                if (_pathLength > 0)
                    _pathDirection = Vector2.Normalize(_pathDirection);
                
                // Generate new path every time the mouse moves (important!)
                if (!_pathGenerated)
                {
                    PreviewPath();
                    _pathGenerated = true;
                }
            }
        }
        else if (_isSelecting)
        {
            // If Ctrl is released while selecting, end the operation
            _isSelecting = false;
            _isDragging = false;
            OnAreaOperationEnd();
        }
    }
    
    // Update GhostClear to handle events properly
    protected override void GhostClear(TileObject? o)
    {
        // When Ctrl key is released, end the area operation
        if (_isSelecting && !Keyboard.GetState().IsKeyDown(Keys.LeftControl) && 
            !Keyboard.GetState().IsKeyDown(Keys.RightControl))
        {
            _isSelecting = false;
            _isDragging = false;
            OnAreaOperationEnd();
        }
        
        // When left mouse button is released, apply the changes
        if (_isDragging && Mouse.GetState().LeftButton == ButtonState.Released)
        {
            _isDragging = false;
            
            // This is important: ensure area operation is properly closed
            if (_isSelecting)
            {
                _isSelecting = false;
                OnAreaOperationEnd();
            }
        }
    }
    
    // Fix PreviewPath to use area operation coordinates correctly
    private void PreviewPath()
    {
        if (_startTile == null || _endTile == null)
            return;
            
        // Clear previous ghosts
        ClearGhosts();
        
        // Get tile coordinates - MUST use BaseTool's area coordinates for consistency
        int startX = AreaStartX;
        int startY = AreaStartY;
        int endX = AreaEndX;
        int endY = AreaEndY;
        
        Console.WriteLine($"Preview Path: ({startX},{startY}) to ({endX},{endY})");
        
        // Start and end heights
        sbyte startZ = _startTile.Tile.Z;
        sbyte endZ = _endTile.Tile.Z;
        
        // Force height difference if needed (for same-height paths)
        if (startZ == endZ)
        {
            // Just move the end point up by the forced difference amount
            endZ = (sbyte)Math.Min(startZ + _forcedHeightDiff, 127);
        }
        
        // Calculate path vector
        Vector2 pathVector = new Vector2(endX - startX, endY - startY);
        float pathLength = pathVector.Length();
        
        if (pathLength < 0.001f)
            return;
        
        // Create modified tiles with BaseTool's coordinates
        GeneratePathTiles(startX, startY, endX, endY, startZ, endZ);
    }

    // Generate path tiles using a better, universal algorithm
    private void GeneratePathTiles(int startX, int startY, int endX, int endY, sbyte startZ, sbyte endZ)
    {
        // Start area operation tracking in BaseTool - this sets AreaStartX, AreaStartY
        OnAreaOperationStart((ushort)startX, (ushort)startY);
        OnAreaOperationUpdate((ushort)endX, (ushort)endY);

        // Use BaseTool's stored coordinates for consistency
        startX = AreaStartX;
        startY = AreaStartY;
        endX = AreaEndX;
        endY = AreaEndY;

        // Create a bounding box with padding for the gradient area
        int padding = _pathWidth + 2;
        int minX = Math.Min(startX, endX) - padding;
        int maxX = Math.Max(startX, endX) + padding;
        int minY = Math.Min(startY, endY) - padding;
        int maxY = Math.Max(startY, endY) + padding;
        
        Console.WriteLine($"Scanning area: ({minX},{minY}) to ({maxX},{maxY})");
        Console.WriteLine($"Path width: {_pathWidth}");
        
        // Calculate path vector using BaseTool's coordinates
        float dx = endX - startX;
        float dy = endY - startY;
        float pathLengthSquared = dx * dx + dy * dy;
        
        // Dictionary to track modified tiles
        Dictionary<(int, int), LandObject> pendingGhostTiles = new();
        
        // Track tiles modified in each quadrant for debugging
        int nwCount = 0, neCount = 0, swCount = 0, seCount = 0;
        
        // Create line equation ax + by + c = 0 for distance calculation
        float a = endY - startY;
        float b = startX - endX;
        float c = endX * startY - startX * endY;
        float lineLengthFactor = (float)Math.Sqrt(a * a + b * b);
        
        // Process all tiles in bounding box
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
                
                // Calculate perpendicular distance using line equation
                float perpDistance = Math.Abs(a * x + b * y + c) / lineLengthFactor;
                
                // Skip if too far from path
                if (perpDistance > _pathWidth)
                    continue;
                
                // Calculate position along the path (parameter t)
                float t;
                
                // Important: Always calculate relative to start point consistently
                int normX = x - startX;
                int normY = y - startY;
                
                // Choose the most stable calculation method based on path direction
                if (Math.Abs(dx) > Math.Abs(dy))
                {
                    // More horizontal path - avoid division by zero
                    t = dx != 0 ? normX / dx : 0;
                }
                else
                {
                    // More vertical path - avoid division by zero
                    t = dy != 0 ? normY / dy : 0;
                }
                
                // Skip if outside path segment (not between start and end)
                if (t < 0.0f || t > 1.0f)
                    continue;
                
                // Calculate target height based on gradient
                float gradientFactor = GetGradientFactor(t);
                sbyte currentZ = lo.Tile.Z;
                int targetHeight = (int)(startZ + (endZ - startZ) * gradientFactor);
                
                // Apply edge fade if enabled
                if (_useEdgeFade && perpDistance > (_pathWidth - _edgeFadeWidth))
                {
                    float fadeDistance = perpDistance - (_pathWidth - _edgeFadeWidth);
                    float fadeFactor = 1.0f - (fadeDistance / _edgeFadeWidth);
                    fadeFactor = Math.Max(0, Math.Min(1, fadeFactor));
                    targetHeight = (int)(currentZ * (1 - fadeFactor) + targetHeight * fadeFactor);
                }
                
                // Respect existing terrain if enabled
                if (_respectExistingTerrain)
                {
                    targetHeight = (int)(currentZ * (1 - _blendFactor) + targetHeight * _blendFactor);
                }
                
                // Only create ghost if height changed
                if (targetHeight != currentZ)
                {
                    sbyte newZ = (sbyte)Math.Clamp(targetHeight, -128, 127);
                    var newTile = new LandTile(lo.LandTile.Id, lo.Tile.X, lo.Tile.Y, newZ);
                    var ghostTile = new LandObject(newTile);
                    
                    // Use normalized coordinates for quadrant detection - always relative to start!
                    // This matches how BaseTool's GetSequentialTileId works
                    bool isXPositive = normX >= 0;
                    bool isYPositive = normY >= 0;
                    
                    // Count quadrants for reporting purposes
                    if (isXPositive) {
                        if (isYPositive) seCount++;
                        else neCount++;
                    } else {
                        if (isYPositive) swCount++;
                        else nwCount++;
                    }
                    
                    pendingGhostTiles[(x, y)] = ghostTile;
                    MapManager.GhostLandTiles[lo] = ghostTile;
                    lo.Visible = false;
                }
            }
        }
        
        // End area operation tracking
        OnAreaOperationEnd();
        
        // Print counts for each quadrant for debugging
        Console.WriteLine($"Modified tiles by quadrant - NW: {nwCount}, NE: {neCount}, SW: {swCount}, SE: {seCount}");
        
        // Update all tiles to ensure proper rendering
        foreach (var kvp in pendingGhostTiles)
        {
            LandObject ghostTile = kvp.Value;
            
            // Mark the tile and its neighbors for recalculation
            for (int nx = ghostTile.Tile.X - 3; nx <= ghostTile.Tile.X + 3; nx++)
            {
                for (int ny = ghostTile.Tile.Y - 3; ny <= ghostTile.Tile.Y + 3; ny++)
                {
                    if (Client.IsValidX((ushort)nx) && Client.IsValidY((ushort)ny))
                    {
                        LandObject? neighborTile = MapManager.LandTiles[nx, ny];
                        if (neighborTile != null)
                            MapManager._ToRecalculate.Add(neighborTile);
                    }
                }
            }
            
            // This triggers proper recalculation of the tile
            MapManager.OnLandTileElevated(ghostTile.LandTile, ghostTile.LandTile.Z);
        }
        
        // Log the total count of modified tiles
        Console.WriteLine($"Total modified: {pendingGhostTiles.Count} tiles");
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

    protected override void InternalApply(TileObject? o)
    {
        // Don't check for chance here, as we want to apply all ghost tiles
        if (_startTile == null || _endTile == null)
            return;
    
        Console.WriteLine($"Applying changes from {MapManager.GhostLandTiles.Count} ghost tiles");
            
        // Apply the changes from ghost tiles to actual tiles
        foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
        {
            pair.Key.LandTile.ReplaceLand(pair.Key.LandTile.Id, pair.Value.Tile.Z);
        }
    
        // Clean up
        ClearGhosts();
        _startTile = null;
        _endTile = null;
        _pathGenerated = false;
    }
    
    private void ClearGhosts()
    {
        // Clear any existing ghost tiles
        foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
        {
            pair.Key.Visible = true;
        }
        
        MapManager.GhostLandTiles.Clear();
        MapManager.GhostStaticTiles.Clear();
    }
}
