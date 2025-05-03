using ClassicUO.Assets;
using ClassicUO.IO;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CentrED;

public struct MultiItem
{
    public ushort TileID;
    public short X;
    public short Y;
    public short Z;
    public uint Flags; // 1 for Static, 0 for Dynamic in Multi.mul? UOFiddler uses int, check usage.
    // Cliloc ID is not part of the base multi.mul structure
}

public class MultiComponentList
{
    public int Width { get; }
    public int Height { get; }
    public int MaxX { get; }
    public int MaxY { get; }
    public int MinX { get; }
    public int MinY { get; }
    public int CenterX { get; }
    public int CenterY { get; }
    public MultiItem[] Items { get; }

    // Based on UOFiddler's MultiComponentList constructor
    public MultiComponentList(BinaryReader reader, int count)
    {
        Items = new MultiItem[count];
        int minX = short.MaxValue, minY = short.MaxValue, maxX = short.MinValue, maxY = short.MinValue;

        for (int i = 0; i < count; ++i)
        {
            Items[i].TileID = reader.ReadUInt16();
            Items[i].X = reader.ReadInt16();
            Items[i].Y = reader.ReadInt16();
            Items[i].Z = reader.ReadInt16();
            Items[i].Flags = reader.ReadUInt32(); // Read as uint (4 bytes)

            // Ignore Z for bounds calculation as per UOFiddler
            if (Items[i].X < minX) minX = Items[i].X;
            if (Items[i].Y < minY) minY = Items[i].Y;
            if (Items[i].X > maxX) maxX = Items[i].X;
            if (Items[i].Y > maxY) maxY = Items[i].Y;
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        Width = maxX - minX + 1;
        Height = maxY - minY + 1;
        CenterX = -minX;
        CenterY = -minY;
    }
}


public class MultisManager
{
    private static MultisManager _instance;
    public static MultisManager Instance => _instance;

    private static FileIndex _fileIndex;
    private static Dictionary<int, MultiComponentList> _multis;
    public static int Count => _multis?.Count ?? 0;
    public static IReadOnlyDictionary<int, MultiComponentList> Multis => _multis;


    private MultisManager()
    {
        // Private constructor for singleton
         _multis = new Dictionary<int, MultiComponentList>();
    }

    // Load multis - Call this after UOFileManager is initialized
    public static void Load()
    {
        _instance = new MultisManager();
        string indexPath = UOFileManager.GetUOFilePath("multi.idx");
        string dataPath = UOFileManager.GetUOFilePath("multi.mul");
        // TODO: Add UOP handling based on UOFileManager.IsUOPInstallation if needed

        if (!File.Exists(indexPath) || !File.Exists(dataPath))
        {
            Console.WriteLine("[ERROR] MultisManager: multi.idx or multi.mul not found.");
            // Consider logging instead of Console.WriteLine
            return;
        }

        try
        {
            // Assuming FileIndex constructor handles potential Verdata patching internally if needed
            _fileIndex = new FileIndex(indexPath, dataPath, ".mul", 0x10000); // Max ID for multis, adjust if necessary

            for (int i = 0; i < _fileIndex.Index.Length; ++i)
            {
                Stream stream = _fileIndex.Seek(i, out int length, out int extra, out bool patched); // Use ClassicUO's FileIndex method signature
                if (stream != null && length > 0)
                {
                    try
                    {
                        using (var reader = new BinaryReader(stream))
                        {
                            // Size of MultiItem: ushort(2) + short(2) + short(2) + short(2) + uint(4) = 12 bytes
                            int count = length / 12;
                            if (count > 0)
                            {
                                _multis[i] = new MultiComponentList(reader, count);
                            }
                        }
                    }
                    catch(EndOfStreamException e)
                    {
                         Console.WriteLine($"[WARN] MultisManager: EndOfStreamException reading multi {i}. Length: {length}. Error: {e.Message}");
                         // Skip this multi or handle partial read if possible
                    }
                    finally
                    {
                        stream.Dispose(); // Ensure stream is disposed if Seek returns a new stream
                    }
                }
            }
             Console.WriteLine($"[INFO] MultisManager: Loaded {_multis.Count} multis.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ERROR] MultisManager: Failed to load multis: {e.Message}\n{e.StackTrace}");
            _multis?.Clear(); // Clear partially loaded data on error
        }
    }

     public static MultiComponentList? GetMulti(int index)
    {
        if (_multis == null) return null;
        _multis.TryGetValue(index, out var multi);
        return multi;
    }
}