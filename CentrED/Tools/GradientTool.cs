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

    // Generate path tiles properly based on the first click (start point)
    private void GeneratePathTiles(int startX, int startY, int endX, int endY, sbyte startZ, sbyte endZ)
    {
        // Create a bounding box with padding for the gradient area
        int padding = _pathWidth + 2;
        int minX = Math.Min(startX, endX) - padding;
        int maxX = Math.Max(startX, endX) + padding;
        int minY = Math.Min(startY, endY) - padding;
        int maxY = Math.Max(startY, endY) + padding;
        
        // Only show scan area at debug level 3+
        if (_showDebugInfo && _debugLevel >= 3)
            Console.WriteLine($"[GRAD] Scan: ({minX},{minY})→({maxX},{maxY})");

        // Calculate path vector - ALWAYS FROM START TO END
        float dx = endX - startX; 
        float dy = endY - startY; 
        float pathLength = (float)Math.Sqrt(dx * dx + dy * dy);

        if (pathLength < 0.001f)
            return;

        // Save original non-normalized direction for debugging
        float originalDx = dx;
        float originalDy = dy;

        // Calculate path angle in world space coordinates
        float angle = (float)Math.Atan2(dy, dx);
        
        // Check if we're going in problematic directions (essentially negative X or Y)
        bool isNorthish = dy < 0;
        bool isWestish = dx < 0;
        
        // Get absolute angle from 0-90 degrees to determine cardinal direction
        float absAngle = Math.Abs(angle);
        bool isMoreHorizontal = absAngle < 0.78f || absAngle > 2.35f; // E-W dominant
        bool isMoreVertical = absAngle >= 0.78f && absAngle <= 2.35f;  // N-S dominant

        // Auto-reverse logic:
        // 1. Always reverse for NORTH-dominant (negative Y with more vertical angle)
        // 2. Always reverse for WEST-dominant (negative X with more horizontal angle)
        bool shouldReverseCalculation = (isNorthish && isMoreVertical) || (isWestish && isMoreHorizontal);
        
        // If automatic detection suggests to reverse the path calculation
        if (shouldReverseCalculation)
        {
            // Swap the direction vector but keep the same endpoints
            dx = -dx;
            dy = -dy;
            
            // Also swap the heights to maintain correct gradient
            (startZ, endZ) = (endZ, startZ);
            
            // Recalculate angle with reversed direction
            angle = (float)Math.Atan2(dy, dx);
            
            if (_showDebugInfo && _debugLevel >= 2)
                Console.WriteLine($"[GRAD] Auto-reversed direction for better results");
        }

        // Create normalized direction vector for the path
        float ndx = dx / pathLength;
        float ndy = dy / pathLength;
        
        // Direction info for debugging
        string direction = "";
        if (angle < -2.35f || angle > 2.35f) direction = "WEST";
        else if (angle < -0.78f) direction = "NORTH";
        else if (angle < 0.78f) direction = "EAST";
        else if (angle < 2.35f) direction = "SOUTH";
        
        if (_showDebugInfo && _debugLevel >= 2)
            Console.WriteLine($"[GRAD] Direction: {direction} (angle: {angle:F2}) Auto-reversed: {shouldReverseCalculation}");
            
        // Extra debugging to understand directional pattern
        if (_showDebugInfo && _debugLevel >= 3)
            Console.WriteLine($"[GRAD] Original vector: ({originalDx:F1},{originalDy:F1}) → Adjusted: ({dx:F1},{dy:F1})");

        // Dictionary to track modified tiles
        Dictionary<(int, int), LandObject> pendingGhostTiles = new();

        // Track tiles modified in each quadrant for debugging
        int nwCount = 0, neCount = 0, swCount = 0, seCount = 0;

        // Use the same constant for isometric calculations as elsewhere in the codebase
        float rsqrt2 = TileObject.RSQRT2;
        
        // Base path width adjustments
        float adjustedWidth = _pathWidth;
        
        // Apply direction-specific width factor 
        float directionWidthFactor = 1.5f;
        
        // Width adjustments by cardinal direction
        if (direction == "WEST" || direction == "EAST") {
            directionWidthFactor = 1.8f;  // Horizontal paths need more width
        }
        else if (direction == "NORTH" || direction == "SOUTH") {
            directionWidthFactor = 2.0f;  // Vertical paths need even more width
        }
        
        // Give extra width to all reversed paths
        if (shouldReverseCalculation)
            directionWidthFactor *= 1.2f;
                
        adjustedWidth *= directionWidthFactor;
        
        // Track the last calculated width multiplier for debugging
        float lastWidthMultiplier = directionWidthFactor;
        
        // Special handling for diagonal vectors: increase the width to avoid thin paths
        bool isDiagonal = (Math.Abs(dx) > 1.0f && Math.Abs(dy) > 1.0f && 
                          Math.Abs(Math.Abs(dx) - Math.Abs(dy)) < Math.Min(Math.Abs(dx), Math.Abs(dy)) * 0.5f);
        if (isDiagonal) {
            adjustedWidth *= 1.2f;
        }

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

                // Vector from START to current point
                float ptX = x - startX;
                float ptY = y - startY;
                
                // Project point onto path to find position along path (dot product)
                float dotProduct = ptX * ndx + ptY * ndy;
                
                // Skip if outside path segment with a small tolerance
                float t = dotProduct / pathLength;
                if (t < -0.05f || t > 1.05f)
                    continue;
                
                // Calculate perpendicular distance vector from path
                float perpX = ptX - dotProduct * ndx;
                float perpY = ptY - dotProduct * ndy;

                // Quadrant-specific perpendicular vector scaling with safe defaults
                bool perpInNegativeX = perpX < 0;
                bool perpInNegativeY = perpY < 0;
                
                float perpXScale = 1.0f;
                float perpYScale = 1.0f;
                
                // Base scaling adjustments by direction
                if (direction == "WEST" || direction == "EAST") 
                {
                    // For horizontal paths, we need to stretch perpendicular Y
                    perpXScale = 1.2f;
                    perpYScale = 1.4f;
                    
                    // For west/east, emphasize the side where the path widens
                    if (direction == "WEST") {
                        if (perpInNegativeY) perpYScale = 1.6f; // North side wider for WEST
                    } else { // EAST
                        if (!perpInNegativeY) perpYScale = 1.6f; // South side wider for EAST
                    }
                }
                else if (direction == "NORTH" || direction == "SOUTH") 
                {
                    // For vertical paths, we need to stretch perpendicular X
                    perpXScale = 1.4f;
                    perpYScale = 1.2f;
                    
                    // For north/south, emphasize the side where the path widens
                    if (direction == "NORTH") {
                        if (perpInNegativeX) perpXScale = 1.6f; // West side wider for NORTH
                    } else { // SOUTH
                        if (!perpInNegativeX) perpXScale = 1.6f; // East side wider for SOUTH
                    }
                }
                else if (isDiagonal) 
                {
                    // For diagonal paths, make a more uniform scaling
                    perpXScale = 1.3f;
                    perpYScale = 1.3f;
                }

                // When reversed, be even more aggressive with scaling
                if (shouldReverseCalculation) {
                    perpXScale *= 1.2f;
                    perpYScale *= 1.2f;
                }
                
                // Apply the scaled perpendicular components
                perpX *= perpXScale;
                perpY *= perpYScale;
                
                // Now apply the standard isometric transformation
                float isoX = perpX * rsqrt2 - perpY * -rsqrt2;
                float isoY = perpX * -rsqrt2 + perpY * rsqrt2;
                
                // Calculate final distance in isometric space
                float isoDistance = (float)Math.Sqrt(isoX * isoX + isoY * isoY);
                
                // Skip if too far from path
                if (isoDistance > adjustedWidth)
                    continue;
                
                // Track the last width for debugging
                lastWidthMultiplier = adjustedWidth / _pathWidth;

                // Clamp t to 0-1 range for height calculation
                t = Math.Clamp(t, 0, 1);
                
                // Calculate target height - linear gradient from start to end Z
                sbyte currentZ = lo.Tile.Z;
                int targetHeight = (int)(startZ + (endZ - startZ) * t);
                
                // Only create ghost if height changed
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
                }
            }
        }
        
        // Report summary information
        if (_showDebugInfo)
        {
            // Only show quadrant details at levels 2+
            if (_debugLevel >= 2)
            {
                Console.WriteLine($"[GRAD] Tiles: NW:{nwCount} NE:{neCount} SW:{swCount} SE:{seCount} Tot:{pendingGhostTiles.Count}");

                // Alert if any quadrant has zero tiles when it should have some
                bool hasAllDirections = Math.Abs(dx) > 1 && Math.Abs(dy) > 1;
                bool shouldHaveAllQuadrants = hasAllDirections && Math.Abs(dx) > 3 && Math.Abs(dy) > 3;
                if (shouldHaveAllQuadrants && (nwCount == 0 || neCount == 0 || swCount == 0 || seCount == 0))
                    Console.WriteLine($"[WARN] Missing quadrant! Dir:({ndx:F1},{ndy:F1}) Scale:{lastWidthMultiplier:F1}");
                
                // Report width factors used
                Console.WriteLine($"[GRAD] Width factors: base:{_pathWidth} adjusted:{adjustedWidth:F1} multiplier:{lastWidthMultiplier:F1}");
            }
            else
            {
                // Simple count for level 1
                Console.WriteLine($"[GRAD] Modified {pendingGhostTiles.Count} tiles");
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