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
    private TileObject? _startTile = null;
    private TileObject? _endTile = null;
    private TileObject? _lastProcessedEndTile = null; // Track last processed endpoint

    // Path settings - SIMPLIFIED
    private int _pathWidth = 5;                // Width of path in tiles
    private bool _showAdvancedOptions = false; // Toggle for showing advanced options
    private bool _disableAutoReverse = false;  // Option to disable auto-reversal

    // Internal calculation fields
    private Vector2 _pathDirection;
    private float _pathLength;
    private Vector2 _lastPathEnd = new Vector2(0, 0); // Track last end position
    private Vector2 _lastSignificantEnd = new Vector2(0, 0); // Last endpoint that triggered a path update

    // Optimization to reduce excessive recalculations
    private int _updateCounter = 0;
    private const int UPDATE_THRESHOLD = 8; // Increase threshold further to reduce updates
    private const int MIN_DISTANCE_THRESHOLD = 3; // Minimum distance before recalculating
    private DateTime _lastUpdateTime = DateTime.MinValue;
    private const int MIN_UPDATE_MS = 300; // Minimum time between updates in milliseconds

    // DEBUG options
    private bool _showDebugInfo = true;
    private int _debugLevel = 1;  // 1=Basic info only, 2=Metrics, 3=Detailed

    // Main drawing UI - SIMPLIFIED
    internal override void Draw()
    {
        ImGui.Text("Path Settings");
        ImGui.BeginChild("PathSettings", new System.Numerics.Vector2(-1, 120), ImGuiChildFlags.Border);

        // Path width setting
        ImGui.Text("Path Width:");
        ImGui.SameLine(140);
        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("##pathWidth", ref _pathWidth);
        if (_pathWidth < 1) _pathWidth = 1;
        if (_pathWidth > 20) _pathWidth = 20;

        // Replace manual reverse option with advanced options toggle
        if (ImGui.Checkbox("Show Advanced Options", ref _showAdvancedOptions))
        {
            // Toggle advanced options
        }
        
        // Only display advanced diagnostic info if selected
        if (_showAdvancedOptions)
        {
            ImGui.Checkbox("Show Debug Info", ref _showDebugInfo);
            if (_showDebugInfo)
            {
                ImGui.SameLine(140);
                ImGui.SetNextItemWidth(150);
                ImGui.SliderInt("Detail Level", ref _debugLevel, 1, 3);
            }
            
            // Add option to disable auto-reversal
            ImGui.Checkbox("Disable Auto-Reverse", ref _disableAutoReverse);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When checked, paths will be created exactly as drawn");
        }
        else
        {
            // Keep debug info but don't show it
            _showDebugInfo = false;
        }

        ImGui.EndChild();

        ImGui.Spacing();

        // Instructions
        ImGui.TextWrapped("Hold CTRL and click to set the start point, then drag to create a path to your target area.");

        // Show current selection status
        if (_startTile != null && _endTile != null)
        {
            ImGui.Separator();
            ImGui.Text($"Start: ({_startTile.Tile.X},{_startTile.Tile.Y}, Z:{_startTile.Tile.Z})");
            ImGui.Text($"End: ({_endTile.Tile.X},{_endTile.Tile.Y}, Z:{_endTile.Tile.Z})");

            int actualHeightDiff = Math.Abs(_endTile.Tile.Z - _startTile.Tile.Z);
            int displayHeightDiff = actualHeightDiff == 0 ? 5 : actualHeightDiff;

            ImGui.Text($"Height Difference: {displayHeightDiff} tiles");

            if (_pathLength > 0 && displayHeightDiff > 0)
            {
                float slope = displayHeightDiff / _pathLength;
                ImGui.Text($"Average Slope: {slope:F2} (1:{(1 / slope):F1})");
            }
        }
    }

    // GhostApply with improved debug output and optimization
    protected override void GhostApply(TileObject? o)
    {
        if (o is not LandObject landObject)
            return;

        bool ctrlPressed = Keyboard.GetState().IsKeyDown(Keys.LeftControl) ||
                           Keyboard.GetState().IsKeyDown(Keys.RightControl);
        bool leftMousePressed = Mouse.GetState().LeftButton == ButtonState.Pressed;

        if (ctrlPressed)
        {
            if (!_isSelecting && leftMousePressed)
            {
                // First click - set the start point - THIS IS THE ANCHOR POINT FOR ALL CALCULATIONS
                _isSelecting = true;
                _isDragging = true;
                _startTile = o;
                _endTile = o; 
                _lastProcessedEndTile = null;
                _lastPathEnd = new Vector2(o.Tile.X, o.Tile.Y);
                _lastSignificantEnd = _lastPathEnd;
                _lastUpdateTime = DateTime.Now;
                ClearGhosts();
                _updateCounter = 0;

                // Area operation tracking - THIS IS KEY FOR SEQUENTIAL LOGIC
                OnAreaOperationStart((ushort)o.Tile.X, (ushort)o.Tile.Y);
                if (_showDebugInfo) Console.WriteLine($"[GRAD] Start: ({o.Tile.X}, {o.Tile.Y})");
            }
            else if (_isSelecting && leftMousePressed && _startTile != null)
            {
                // Always update end point as we drag
                bool endPointChanged = (_endTile == null ||
                                       _endTile.Tile.X != o.Tile.X ||
                                       _endTile.Tile.Y != o.Tile.Y);

                if (endPointChanged)
                {
                    // End point changed - update tracking
                    _endTile = o;

                    // Update area operation tracking - THIS FOLLOWS THE DRAWTOOL PATTERN
                    OnAreaOperationUpdate((ushort)o.Tile.X, (ushort)o.Tile.Y);

                    // Calculate path vector FROM THE START TILE (fixed anchor point)
                    float dx = _endTile.Tile.X - _startTile.Tile.X;
                    float dy = _endTile.Tile.Y - _startTile.Tile.Y;
                    _pathDirection = new Vector2(dx, dy);
                    _pathLength = _pathDirection.Length();

                    if (_pathLength < 0.1f)
                        return;

                    // Current position and distance checks
                    Vector2 currentPos = new Vector2(_endTile.Tile.X, _endTile.Tile.Y);
                    float distFromSignificant = Vector2.Distance(_lastSignificantEnd, currentPos);
                    TimeSpan timeSinceLastUpdate = DateTime.Now - _lastUpdateTime;

                    // Multi-stage throttling:
                    // 1. Must pass update counter threshold 
                    // 2. Must have moved enough distance from last significant point OR
                    // 3. Sufficient time has passed since last update
                    _updateCounter++;
                    bool timeThresholdMet = timeSinceLastUpdate.TotalMilliseconds > MIN_UPDATE_MS;
                    bool distanceThresholdMet = distFromSignificant >= MIN_DISTANCE_THRESHOLD;
                    bool counterThresholdMet = _updateCounter >= UPDATE_THRESHOLD;
                    
                    // Combine criteria - must meet counter threshold AND (distance OR time threshold)
                    bool shouldUpdate = counterThresholdMet && (distanceThresholdMet || timeThresholdMet);

                    // Override for very large movements - important for usability
                    if (distFromSignificant >= MIN_DISTANCE_THRESHOLD * 2)
                        shouldUpdate = true;

                    if (!shouldUpdate)
                    {
                        _lastPathEnd = currentPos; // Track position even when not updating
                        return; // Skip this update
                    }

                    // Reset state for new update
                    _updateCounter = 0;
                    _lastPathEnd = currentPos;
                    _lastSignificantEnd = currentPos; // This is a significant position
                    _lastUpdateTime = DateTime.Now;
                    _lastProcessedEndTile = _endTile;

                    // Debug output for direction vector - only at level 2+
                    if (_showDebugInfo && _debugLevel >= 2)
                        Console.WriteLine($"[GRAD] Vector: ({dx:F0},{dy:F0}) Len={_pathLength:F1}");

                    // Normalize path direction
                    if (_pathLength > 0)
                        _pathDirection = Vector2.Normalize(_pathDirection);

                    // Generate the path since the end point changed significantly
                    PreviewPath();
                }
            }
        }
        else if (_isSelecting)
        {
            // If Ctrl is released while selecting, end the operation
            _isSelecting = false;
            _isDragging = false;
            OnAreaOperationEnd(); // End area operation tracking
        }
    }

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

            if (_isSelecting)
            {
                _isSelecting = false;
                OnAreaOperationEnd();
            }
        }
    }

    private void PreviewPath()
    {
        if (_startTile == null || _endTile == null)
            return;

        ClearGhosts();

        int startX = AreaStartX;
        int startY = AreaStartY;
        int endX = AreaEndX;
        int endY = AreaEndY;

        // Log once at beginning of generation
        if (_showDebugInfo)
            Console.WriteLine($"[GRAD] Path: ({startX},{startY}) -> ({endX},{endY})");

        // Start and end heights
        sbyte startZ = _startTile.Tile.Z;
        sbyte endZ = _endTile.Tile.Z;

        // Force height difference if same Z (using fixed value of 5)
        if (startZ == endZ)
            endZ = (sbyte)Math.Min(startZ + 5, 127);

        // Calculate path vector
        Vector2 pathVector = new Vector2(endX - startX, endY - startY);
        float pathLength = pathVector.Length();

        if (pathLength < 0.001f)
            return;

        GeneratePathTiles(startX, startY, endX, endY, startZ, endZ);
    }

    private void GeneratePathTiles(int startX, int startY, int endX, int endY, sbyte startZ, sbyte endZ)
    {
        // Create a simplified bounding box
        int padding = _pathWidth * 2; // Extra padding to be safe
        int minX = Math.Min(startX, endX) - padding;
        int maxX = Math.Max(startX, endX) + padding;
        int minY = Math.Min(startY, endY) - padding;
        int maxY = Math.Max(startY, endY) + padding;
        
        // Calculate path vector and properties
        float dx = endX - startX; 
        float dy = endY - startY; 
        float pathLength = (float)Math.Sqrt(dx * dx + dy * dy);

        if (pathLength < 0.001f)
            return;
            
        // Calculate normalized direction vector
        float ndx = dx / pathLength;
        float ndy = dy / pathLength;
        
        // Calculate perpendicular direction (rotate by 90 degrees)
        float perpX = -ndy;
        float perpY = ndx;
        
        // For debugging, check angle to ensure perpendicular is correct
        if (_showDebugInfo && _debugLevel >= 2)
        {
            float dotProduct = ndx * perpX + ndy * perpY;
            Console.WriteLine($"[GRAD] Direction check: Path=({ndx:F2},{ndy:F2}), Perp=({perpX:F2},{perpY:F2}), Dot={dotProduct:F4}");
        }
        
        // Dictionary to track modified tiles
        Dictionary<(int, int), LandObject> pendingGhostTiles = new();
        // Track ACTUAL positions of ALL tiles to verify width
        List<(int x, int y)> allPositions = new();
        
        // Track tiles modified in each quadrant for debugging
        int nwCount = 0, neCount = 0, swCount = 0, seCount = 0;
        
        // Special case for exact cardinal directions (more reliable placement)
        bool isCardinalDirection = Math.Abs(Math.Abs(ndx) - 1.0f) < 0.01f || Math.Abs(Math.Abs(ndy) - 1.0f) < 0.01f;
        
        // Hard reset - DIRECT PATH CALCULATION WITH GUARANTEED WIDTH
        // Calculate the EXACT path based on Bresenham's line algorithm
        List<(int x, int y)> pathTiles = new List<(int x, int y)>();
        
        // First, generate the centerline path
        int x = startX;
        int y = startY;
        int dx2 = Math.Abs((int)dx);
        int dy2 = Math.Abs((int)dy);
        int sx = dx > 0 ? 1 : -1;
        int sy = dy > 0 ? 1 : -1;
        int err = dx2 - dy2;
        
        // Add starting point
        pathTiles.Add((x, y));
        
        // Generate all points along the line
        while (x != endX || y != endY)
        {
            int e2 = err * 2;
            if (e2 > -dy2)
            {
                err -= dy2;
                x += sx;
            }
            if (e2 < dx2)
            {
                err += dx2;
                y += sy;
            }
            pathTiles.Add((x, y));
        }
        
        // For very short paths, log the exact tile list for debugging
        if (_showDebugInfo && _debugLevel >= 2 && pathTiles.Count < 10)
        {
            string pathStr = string.Join(", ", pathTiles.Select(p => $"({p.x},{p.y})"));
            Console.WriteLine($"[GRAD] Path points: {pathStr}");
        }
        
        // ABSOLUTE GUARANTEE OF WIDTH:
        // For each path tile, we'll add EXACTLY _pathWidth tiles in a perpendicular line
        foreach (var pathPoint in pathTiles)
        {
            // Calculate position along path for height (t value)
            int idx = pathTiles.IndexOf(pathPoint);
            float t = idx / (float)(pathTiles.Count - 1);
            int targetHeight = (int)(startZ + (endZ - startZ) * t);
            
            // For each path point, calculate a perpendicular line of EXACTLY _pathWidth tiles
            List<(int x, int y)> tilePositions = GetExactPerpendicularTiles(pathPoint.x, pathPoint.y, perpX, perpY, _pathWidth);
            
            // Map edge positions to their distance from center for edge blending
            Dictionary<(int x, int y), float> edgeDistances = new Dictionary<(int x, int y), float>();
            
            // Find center position and calculate edge distances
            var centerPos = tilePositions[tilePositions.Count / 2]; // Center or just right of center for even widths
            int centerIndex = tilePositions.Count / 2;
            
            // Calculate distance of each tile from the center for edge blending
            for (int i = 0; i < tilePositions.Count; i++)
            {
                var pos = tilePositions[i];
                // Edge factor is 0.0 at center, 1.0 at edges
                float edgeFactor = Math.Abs(i - centerIndex) / (float)Math.Max(1, centerIndex);
                edgeDistances[pos] = edgeFactor;
            }
            
            // Log the width at each point if highly detailed debug
            if (_showDebugInfo && _debugLevel >= 3)
                Console.WriteLine($"[GRAD] At ({pathPoint.x},{pathPoint.y}), width={tilePositions.Count}");
            
            foreach (var tilePos in tilePositions)
            {
                allPositions.Add(tilePos); // Track ALL positions for verification
                
                // Apply special height handling based on edge distance
                int adjustedHeight = targetHeight;
                
                // Only apply edge blending if this is an edge tile
                if (edgeDistances.TryGetValue(tilePos, out float edgeFactor) && edgeFactor > 0.6f)
                {
                    // Get the terrain height at this position
                    LandObject? lo = MapManager.LandTiles[tilePos.x, tilePos.y];
                    if (lo != null)
                    {
                        sbyte terrainHeight = lo.Tile.Z;
                        
                        // Blend between target height and terrain height based on edge factor
                        // This creates a smoother transition at the edges
                        adjustedHeight = (int)Math.Round(targetHeight * (1 - edgeFactor * 0.7f) + 
                                                      terrainHeight * (edgeFactor * 0.7f));
                        
                        // Debug output for edge blending
                        if (_showDebugInfo && _debugLevel >= 3)
                            Console.WriteLine($"[GRAD-EDGE] Tile ({tilePos.x},{tilePos.y}) Edge:{edgeFactor:F2} " +
                                           $"Terrain:{terrainHeight} Target:{targetHeight} Adjusted:{adjustedHeight}");
                    }
                }
                
                AddGhostTileIfValid(tilePos.x, tilePos.y, adjustedHeight, startX, startY, pendingGhostTiles,
                                   ref nwCount, ref neCount, ref swCount, ref seCount);
            }
        }
        
        // Count ACTUAL width at start and end
        if (_showDebugInfo)
        {
            // Verify width at start and end points
            if (pathTiles.Count > 0)
            {
                var startPoint = pathTiles[0];
                var endPoint = pathTiles[pathTiles.Count - 1];
                
                // Get all tiles at the same X coordinate for vertical paths
                var startWidthCount = allPositions.Count(p => p.x == startPoint.x && Math.Abs(p.y - startPoint.y) <= _pathWidth);
                var endWidthCount = allPositions.Count(p => p.x == endPoint.x && Math.Abs(p.y - endPoint.y) <= _pathWidth);
                
                // For horizontal paths, count tiles at the same Y coordinate
                if (startWidthCount < _pathWidth)
                {
                    startWidthCount = allPositions.Count(p => p.y == startPoint.y && Math.Abs(p.x - startPoint.x) <= _pathWidth);
                    endWidthCount = allPositions.Count(p => p.y == endPoint.y && Math.Abs(p.x - endPoint.x) <= _pathWidth);
                }
                
                Console.WriteLine($"[GRAD] PATH WIDTH CHECK: Expected={_pathWidth}, Start={startWidthCount}, End={endWidthCount}, Total={pendingGhostTiles.Count}");
                Console.WriteLine($"[GRAD] Tiles: NW:{nwCount} NE:{neCount} SW:{swCount} SE:{seCount}");
            }
        }
        
        // Update all tiles for rendering
        foreach (var kvp in pendingGhostTiles)
        {
            LandObject ghostTile = kvp.Value;

            // Mark neighbors for recalculation
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

            MapManager.OnLandTileElevated(ghostTile.LandTile, ghostTile.LandTile.Z);
        }
    }
    
    // Helper to get EXACT perpendicular tiles - GUARANTEED TO RETURN EXACTLY 'width' TILES
    private List<(int x, int y)> GetExactPerpendicularTiles(int centerX, int centerY, float perpX, float perpY, int width)
    {
        List<(int x, int y)> result = new List<(int x, int y)>();
        
        // Always add the center tile
        result.Add((centerX, centerY));
        
        // Special case with exact handling for cardinal directions
        if (Math.Abs(perpX) > 0.95f || Math.Abs(perpY) > 0.95f)
        {
            // For pure horizontal or vertical perpendicular (simple case)
            int dx = Math.Abs(perpX) > 0.7f ? (perpX > 0 ? 1 : -1) : 0;
            int dy = Math.Abs(perpY) > 0.7f ? (perpY > 0 ? 1 : -1) : 0;
            
            // Add tiles on both sides of center
            int halfWidth = width / 2;
            
            for (int i = 1; i <= halfWidth; i++)
            {
                // Add tile on one side
                result.Add((centerX + dx * i, centerY + dy * i));
                
                // Add tile on the other side (if not exactly centered)
                if (i <= width - 1 - halfWidth)
                {
                    result.Add((centerX - dx * i, centerY - dy * i));
                }
            }
        }
        else
        {
            // For diagonal paths, we need to handle fractional offsets
            // Calculate how many tiles to add on each side
            int halfWidth = width / 2;
            
            for (int i = 1; i <= halfWidth; i++)
            {
                // Round to nearest tile
                int posX = (int)Math.Round(centerX + perpX * i);
                int posY = (int)Math.Round(centerY + perpY * i);
                
                // Add it if not already in the list
                if (!result.Contains((posX, posY)))
                    result.Add((posX, posY));
                    
                // Same for negative side
                int negX = (int)Math.Round(centerX - perpX * i);
                int negY = (int)Math.Round(centerY - perpY * i);
                
                if (!result.Contains((negX, negY)) && (i <= width - 1 - halfWidth))
                    result.Add((negX, negY));
            }
        }
        
        // Ensure we have EXACTLY 'width' tiles
        while (result.Count < width)
        {
            // Add additional tiles as needed
            int lastIdx = result.Count;
            int dx = Math.Abs(perpX) > Math.Abs(perpY) ? (perpX > 0 ? 1 : -1) : 0;
            int dy = Math.Abs(perpY) > Math.Abs(perpX) ? (perpY > 0 ? 1 : -1) : 0;
            
            // Find a nearby tile that's not already in the list
            for (int i = 1; i <= 3; i++) // Try up to 3 tiles away
            {
                (int x, int y) newTile = (result[lastIdx - 1].x + dx * i, result[lastIdx - 1].y + dy * i);
                if (!result.Contains(newTile))
                {
                    result.Add(newTile);
                    break;
                }
            }
        }
        
        // If we have too many tiles, remove from the end
        while (result.Count > width)
        {
            result.RemoveAt(result.Count - 1);
        }
        
        return result;
    }

    private void AddGhostTileIfValid(int x, int y, int targetHeight, int startX, int startY,
                                   Dictionary<(int, int), LandObject> pendingGhostTiles,
                                   ref int nwCount, ref int neCount, ref int swCount, ref int seCount)
    {
        // Skip if we've already processed this tile
        if (pendingGhostTiles.ContainsKey((x, y)))
            return;
            
        // Skip if out of bounds
        if (!Client.IsValidX((ushort)x) || !Client.IsValidY((ushort)y))
            return;
        
        // Get the land object
        LandObject? lo = MapManager.LandTiles[x, y];
        if (lo == null)
            return;
            
        // Only create ghost if height changed
        sbyte currentZ = lo.Tile.Z;
        if (targetHeight != currentZ)
        {
            // Create the ghost tile
            sbyte newZ = (sbyte)Math.Clamp(targetHeight, -128, 127);
            var newTile = new LandTile(lo.LandTile.Id, lo.Tile.X, lo.Tile.Y, newZ);
            var ghostTile = new LandObject(newTile);
            
            // Count quadrants for reporting
            if (x < startX) {
                if (y < startY) nwCount++;
                else swCount++;
            } else {
                if (y < startY) neCount++;
                else seCount++;
            }
            
            pendingGhostTiles[(x, y)] = ghostTile;
            MapManager.GhostLandTiles[lo] = ghostTile;
            lo.Visible = false;
            
            if (_showDebugInfo && _debugLevel >= 3)
                Console.WriteLine($"[GRAD-TILE] Added tile at ({x},{y}) with Z={newZ}");
        }
    }

    protected override void InternalApply(TileObject? o)
    {
        if (_startTile == null || _endTile == null)
            return;

        // Make sure we don't report applying 0 changes
        if (MapManager.GhostLandTiles.Count == 0)
        {
            // Re-generate path before applying if no ghosts exist
            PreviewPath();
        }

        if (_showDebugInfo)
            Console.WriteLine($"[GRAD] Applying {MapManager.GhostLandTiles.Count} changes");

        foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
        {
            pair.Key.LandTile.ReplaceLand(pair.Key.LandTile.Id, pair.Value.Tile.Z);
        }

        ClearGhosts();
        _startTile = null;
        _endTile = null;
    }

    private void ClearGhosts()
    {
        foreach (var pair in new Dictionary<LandObject, LandObject>(MapManager.GhostLandTiles))
        {
            pair.Key.Visible = true;
        }

        MapManager.GhostLandTiles.Clear();
        MapManager.GhostStaticTiles.Clear();
    }
}